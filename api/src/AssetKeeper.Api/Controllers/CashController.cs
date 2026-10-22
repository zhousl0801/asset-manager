using AssetKeeper.Application.Cash;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/cash")]
public sealed class CashController : ControllerBase
{
    private readonly CashService _cashService;

    public CashController(CashService cashService)
    {
        _cashService = cashService;
    }

    [HttpGet]
    [ProducesResponseType<CashOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _cashService.GetOverviewAsync(cancellationToken);

        if (overview is null)
        {
            return LedgerNotFound();
        }

        return Ok(overview);
    }

    [HttpPost("reconcile")]
    [ProducesResponseType<CashReconcileResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reconcile(
        [FromBody] ReconcileCashRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _cashService.ReconcileAsync(request, cancellationToken);

        return result.Status switch
        {
            CashReconcileStatus.Success => Ok(result.Result),
            CashReconcileStatus.NotFound => NotFound(new ProblemDetails
            {
                Title = "操作未完成",
                Detail = result.Errors[0],
                Status = StatusCodes.Status404NotFound
            }),
            CashReconcileStatus.Invalid => ValidationProblem(BuildModelState(result.Errors)),
            CashReconcileStatus.Conflict => Conflict(new ProblemDetails
            {
                Title = "操作冲突",
                Detail = result.Errors[0],
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("未知的操作结果。")
        };
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
