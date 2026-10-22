using AssetKeeper.Application.History;
using Microsoft.AspNetCore.Mvc;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/history")]
public sealed class HistoryController : AssetOperationsControllerBase
{
    private readonly HistoryService _service;

    public HistoryController(HistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetAsync(asOf, cancellationToken);
            return result is null ? LedgerNotFound() : Ok(result);
        }
        catch (HistoryValidationException exception)
        {
            return BadRequest(new ProblemDetails { Title = "日期无效", Detail = exception.Message });
        }
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] HistoryChangeRequest request, CancellationToken cancellationToken)
    {
        return ToActionResult(await _service.PreviewAsync(request, cancellationToken));
    }

    [HttpPost("change")]
    public async Task<IActionResult> Change([FromBody] ConfirmHistoryChangeRequest request, CancellationToken cancellationToken)
    {
        return ToActionResult(await _service.ChangeAsync(request, cancellationToken));
    }
}
