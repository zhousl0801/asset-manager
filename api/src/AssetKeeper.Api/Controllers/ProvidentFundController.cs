using AssetKeeper.Application.Provident;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/provident")]
public sealed class ProvidentFundController : AssetOperationsControllerBase
{
    private readonly ProvidentFundService _providentFundService;

    public ProvidentFundController(ProvidentFundService providentFundService)
    {
        _providentFundService = providentFundService;
    }

    [HttpGet]
    [ProducesResponseType<ProvidentOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _providentFundService.GetOverviewAsync(cancellationToken);

        return overview is null ? LedgerNotFound() : Ok(overview);
    }

    [HttpPost("opening")]
    public async Task<IActionResult> Opening([FromBody] ProvidentOpeningRequest request, CancellationToken cancellationToken)
    {
        return ToActionResult(await _providentFundService.SetOpeningAsync(request, cancellationToken));
    }

    [HttpPost("deposits")]
    [ProducesResponseType<ProvidentMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deposit(
        [FromBody] ProvidentDepositRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _providentFundService.DepositAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("withdrawals")]
    [ProducesResponseType<ProvidentMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(
        [FromBody] ProvidentWithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _providentFundService.WithdrawAsync(request, cancellationToken);

        return ToActionResult(result);
    }
}
