using System.Text.Json;
using AssetKeeper.Application.Ledgers;
using Microsoft.AspNetCore.Mvc;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/requests")]
public sealed class MutationRequestsController : ControllerBase
{
    private readonly ILedgerRepository _repository;

    public MutationRequestsController(ILedgerRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("{requestId:guid}")]
    public async Task<IActionResult> Get(Guid requestId, CancellationToken cancellationToken)
    {
        var ledger = await _repository.GetAsync(cancellationToken);
        var receipt = ledger is null ? null : await _repository.FindMutationReceiptAsync(ledger.Id, requestId, cancellationToken);

        if (receipt is null)
        {
            return NotFound(new ProblemDetails { Title = "尚未查到该请求的入账结果", Status = 404 });
        }

        using var response = JsonDocument.Parse(receipt.ResponseJson);
        return Ok(new { receipt.RequestId, receipt.Operation, Result = response.RootElement.GetProperty("result").Clone() });
    }
}
