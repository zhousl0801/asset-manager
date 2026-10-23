using AssetKeeper.Application.Quotes;
using Microsoft.AspNetCore.Mvc;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/quotes")]
public sealed class QuotesController : ControllerBase
{
    private readonly QuoteRefreshService _quoteRefreshService;

    public QuotesController(QuoteRefreshService quoteRefreshService)
    {
        _quoteRefreshService = quoteRefreshService;
    }

    [HttpGet("status")]
    [ProducesResponseType<QuoteStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _quoteRefreshService.GetStatusAsync(cancellationToken);

        return Ok(status);
    }

    [HttpPost("refresh")]
    [ProducesResponseType<QuoteRefreshRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refresh(
        [FromBody] QuoteRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _quoteRefreshService.RefreshAsync(
                request?.Scope ?? QuoteRefreshScope.All,
                "manual",
                _quoteRefreshService.RetryCount,
                _quoteRefreshService.RetryDelay,
                cancellationToken);

            return result is null ? LedgerNotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "行情刷新进行中",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
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
}
