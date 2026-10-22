using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/funds/returns")]
public sealed class FundReturnsController : ControllerBase
{
    private readonly FundReturnService _fundReturnService;

    public FundReturnsController(FundReturnService fundReturnService)
    {
        _fundReturnService = fundReturnService;
    }

    [HttpGet]
    [ProducesResponseType<FundReturnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] TrendRange range = TrendRange.Month,
        [FromQuery] DateOnly? anchor = null,
        [FromQuery] Guid? groupId = null,
        [FromQuery] bool ungrouped = false,
        CancellationToken cancellationToken = default)
    {
        if (groupId is not null && ungrouped)
        {
            return ValidationProblemResponse(BuildModelState(["分组和未分组不能同时选择。"]));
        }

        var result = await _fundReturnService.GetAsync(range, anchor, groupId, ungrouped, cancellationToken);

        return result is null ? LedgerNotFound() : Ok(result);
    }

    [HttpGet("day")]
    [ProducesResponseType<FundReturnDayDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDay(
        [FromQuery] DateOnly? date = null,
        [FromQuery] Guid? groupId = null,
        [FromQuery] bool ungrouped = false,
        CancellationToken cancellationToken = default)
    {
        if (date is null)
        {
            return ValidationProblemResponse(BuildModelState(["请选择收益日期。"]));
        }

        if (groupId is not null && ungrouped)
        {
            return ValidationProblemResponse(BuildModelState(["分组和未分组不能同时选择。"]));
        }

        var result = await _fundReturnService.GetDayAsync(date.Value, groupId, ungrouped, cancellationToken);

        return result is null ? LedgerNotFound() : Ok(result);
    }

    private IActionResult LedgerNotFound()
    {
        return NotFound(new ProblemDetails
        {
            Title = "账本尚未初始化",
            Detail = "请先设置账本起始日期和现金余额。",
            Status = StatusCodes.Status404NotFound
        });
    }

    private IActionResult ValidationProblemResponse(ModelStateDictionary modelState)
    {
        return ValidationProblem(modelState);
    }

    private static ModelStateDictionary BuildModelState(IReadOnlyList<string> errors)
    {
        var modelState = new ModelStateDictionary();

        foreach (var error in errors)
        {
            modelState.AddModelError(string.Empty, error);
        }

        return modelState;
    }
}
