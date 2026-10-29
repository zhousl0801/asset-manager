using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Quotes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/funds")]
public sealed class FundsController : AssetOperationsControllerBase
{
    private readonly FundService _fundService;

    public FundsController(FundService fundService)
    {
        _fundService = fundService;
    }

    [HttpGet]
    [ProducesResponseType<FundOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _fundService.GetOverviewAsync(cancellationToken);

        return overview is null ? LedgerNotFound() : Ok(overview);
    }

    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<FundSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(Array.Empty<FundSearchResultDto>());
        }

        try
        {
            var results = await _fundService.SearchAsync(query.Trim(), cancellationToken);
            return Ok(results);
        }
        catch (QuoteUnavailableException exception)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "基金搜索暂不可用", detail: exception.Message);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "基金搜索暂不可用", detail: "基金数据源连接失败，请稍后重试。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "基金搜索暂不可用", detail: "基金数据源请求超时，请稍后重试。");
        }
    }

    [HttpPost("positions")]
    [ProducesResponseType<FundMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePosition(
        [FromBody] CreateFundPositionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.CreatePositionAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("buys")]
    [ProducesResponseType<FundMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Buy(
        [FromBody] BuyFundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.BuyAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("buys/{transactionId:guid}/confirm")]
    [ProducesResponseType<FundMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmBuy(
        Guid transactionId,
        [FromBody] ConfirmFundBuyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.ConfirmBuyAsync(transactionId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("pending/{transactionId:guid}/cancel")]
    public async Task<IActionResult> CancelPending(
        Guid transactionId,
        [FromBody] CancelFundPendingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.CancelPendingAsync(transactionId, request.RequestId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("positions/correct-shares")]
    public async Task<IActionResult> CorrectShares(
        [FromBody] CorrectFundSharesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.CorrectSharesAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("sells")]
    [ProducesResponseType<FundMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Sell(
        [FromBody] SellFundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.ApplySellAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("sells/{transactionId:guid}/confirm")]
    [ProducesResponseType<FundMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmSell(
        Guid transactionId,
        [FromBody] ConfirmFundSellRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.ConfirmSellAsync(transactionId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("sells/{transactionId:guid}/arrival")]
    [ProducesResponseType<FundMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArriveSell(
        Guid transactionId,
        [FromBody] FundSellArrivalRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.ArriveSellAsync(transactionId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{positionId:guid}/quote/refresh")]
    [ProducesResponseType<FundQuoteRefreshDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> RefreshQuote(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        var result = await _fundService.RefreshQuoteAsync(positionId, cancellationToken);

        return ToActionResult(result);
    }

}
