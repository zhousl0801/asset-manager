using AssetKeeper.Application.Ledgers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger")]
public sealed class LedgerController : ControllerBase
{
    private readonly LedgerService _ledgerService;
    private readonly TrendService _trendService;
    private readonly MonthChangeService _monthChangeService;

    public LedgerController(
        LedgerService ledgerService,
        TrendService trendService,
        MonthChangeService monthChangeService)
    {
        _ledgerService = ledgerService;
        _trendService = trendService;
        _monthChangeService = monthChangeService;
    }

    [HttpGet]
    [ProducesResponseType<LedgerOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _ledgerService.GetOverviewAsync(cancellationToken);

        if (overview is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "账本尚未初始化",
                Detail = "请先设置账本起始日期和现金余额。",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(overview);
    }

    [HttpGet("trend")]
    [ProducesResponseType<AssetTrendDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrend(
        [FromQuery] TrendRange range,
        CancellationToken cancellationToken)
    {
        var trend = await _trendService.GetAsync(range, cancellationToken);

        if (trend is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "账本尚未初始化",
                Detail = "请先设置账本起始日期和现金余额。",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(trend);
    }

    [HttpGet("month-change")]
    [ProducesResponseType<MonthChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMonthChange(CancellationToken cancellationToken)
    {
        var change = await _monthChangeService.GetAsync(cancellationToken);

        if (change is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "账本尚未初始化",
                Detail = "请先设置账本起始日期和现金余额。",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(change);
    }

    [HttpPut("coverage")]
    [ProducesResponseType<LedgerOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCoverage(
        [FromBody] UpdateAssetCoverageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _ledgerService.UpdateCoverageAsync(request, cancellationToken);

        if (result.Status == AssetCoverageStatus.NotFound)
        {
            return NotFound(new ProblemDetails
            {
                Title = "账本尚未初始化",
                Detail = "请先设置账本起始日期和现金余额。",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (result.Status == AssetCoverageStatus.Conflict)
        {
            return Conflict(new ProblemDetails
            {
                Title = "账本已发生变化",
                Detail = string.Join(" ", result.Errors),
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Status == AssetCoverageStatus.Invalid || result.Overview is null)
        {
            var modelState = new ModelStateDictionary();

            foreach (var error in result.Errors)
            {
                modelState.AddModelError(string.Empty, error);
            }

            return ValidationProblem(modelState);
        }

        return Ok(result.Overview);
    }

    [HttpPost("initialize")]
    [ProducesResponseType<LedgerOverviewDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Initialize(
        [FromBody] InitializeLedgerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _ledgerService.InitializeAsync(request, cancellationToken);

        if (result.Status == InitializeLedgerStatus.AlreadyExists)
        {
            return Conflict(new ProblemDetails
            {
                Title = "账本已存在",
                Detail = "当前版本只支持一个个人账本。",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Status == InitializeLedgerStatus.Invalid)
        {
            var modelState = new ModelStateDictionary();

            foreach (var error in result.Errors)
            {
                modelState.AddModelError(string.Empty, error);
            }

            return ValidationProblem(modelState);
        }

        if (result.Overview is null)
        {
            throw new InvalidOperationException("初始化成功后缺少账本信息。");
        }

        return CreatedAtAction(nameof(Get), result.Overview);
    }
}
