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

    public LedgerController(LedgerService ledgerService, TrendService trendService)
    {
        _ledgerService = ledgerService;
        _trendService = trendService;
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
