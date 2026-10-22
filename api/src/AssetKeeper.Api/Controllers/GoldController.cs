using AssetKeeper.Application.Gold;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/gold")]
public sealed class GoldController : AssetOperationsControllerBase
{
    private readonly GoldService _goldService;

    public GoldController(GoldService goldService)
    {
        _goldService = goldService;
    }

    [HttpGet]
    [ProducesResponseType<GoldOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _goldService.GetOverviewAsync(cancellationToken);

        return overview is null ? LedgerNotFound() : Ok(overview);
    }

    [HttpPost("buys")]
    [ProducesResponseType<GoldMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Buy(
        [FromBody] BuyGoldRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _goldService.BuyAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("sells")]
    [ProducesResponseType<GoldMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Sell(
        [FromBody] SellGoldRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _goldService.SellAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("sells/{transactionId:guid}/arrival")]
    [ProducesResponseType<GoldMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ArriveSell(
        Guid transactionId,
        [FromBody] GoldSellArrivalRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _goldService.ArriveSellAsync(transactionId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("quote/refresh")]
    [ProducesResponseType<GoldQuoteRefreshDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> RefreshQuote(CancellationToken cancellationToken)
    {
        var result = await _goldService.RefreshQuoteAsync(cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("quote")]
    [ProducesResponseType<GoldMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateQuote(
        [FromBody] UpdateGoldQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _goldService.UpdateQuoteAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("holding")]
    [ProducesResponseType<GoldMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveHolding(
        [FromBody] SaveGoldHoldingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _goldService.SaveHoldingAsync(request, cancellationToken);

        return ToActionResult(result);
    }
}
