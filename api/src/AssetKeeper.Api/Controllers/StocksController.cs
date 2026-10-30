using AssetKeeper.Application.Quotes;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Application.Stocks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/stocks")]
public sealed class StocksController : AssetOperationsControllerBase
{
    private readonly StockService _stockService;

    public StocksController(StockService stockService)
    {
        _stockService = stockService;
    }

    [HttpGet]
    [ProducesResponseType<StockOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _stockService.GetOverviewAsync(cancellationToken);

        return overview is null ? LedgerNotFound() : Ok(overview);
    }

    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<StockSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(Array.Empty<StockSearchResultDto>());
        }

        try
        {
            var results = await _stockService.SearchAsync(query, cancellationToken);
            return Ok(results);
        }
        catch (QuoteUnavailableException exception)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "股票搜索暂不可用", detail: exception.Message);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "股票搜索暂不可用", detail: "股票数据源连接失败，请稍后重试。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "股票搜索暂不可用", detail: "股票数据源请求超时，请稍后重试。");
        }
    }

    [HttpGet("quote")]
    [ProducesResponseType<StockQuoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Quote(
        [FromQuery] StockMarket market,
        [FromQuery] string? code,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.GetQuoteAsync(market, code ?? string.Empty, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("buys")]
    [ProducesResponseType<StockMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Buy(
        [FromBody] BuyStockRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.BuyAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("sells")]
    [ProducesResponseType<StockMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Sell(
        [FromBody] SellStockRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.SellAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("positions")]
    [ProducesResponseType<StockMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveHolding(
        [FromBody] SaveStockHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.SaveHoldingAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("positions/{positionId:guid}/holding")]
    [ProducesResponseType<StockMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CorrectHolding(
        Guid positionId,
        [FromBody] SaveStockHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.SaveHoldingAsync(
            request with { PositionId = positionId },
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("quotes/refresh")]
    [ProducesResponseType<StockQuoteRefreshBatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RefreshQuotes(CancellationToken cancellationToken)
    {
        var result = await _stockService.RefreshAllAsync(cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{positionId:guid}/quote/refresh")]
    [ProducesResponseType<StockQuoteRefreshDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> RefreshQuote(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        var result = await _stockService.RefreshQuoteAsync(positionId, cancellationToken);

        return ToActionResult(result);
    }
}
