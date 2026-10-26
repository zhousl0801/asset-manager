using AssetKeeper.Application.Plans;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/plans")]
public sealed class PlansController : AssetOperationsControllerBase
{
    private readonly PlanService _planService;
    private readonly PlanExecutionService _executionService;

    public PlansController(PlanService planService, PlanExecutionService executionService)
    {
        _planService = planService;
        _executionService = executionService;
    }

    [HttpGet]
    [ProducesResponseType<PlanOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var overview = await _planService.GetOverviewAsync(cancellationToken);

        return overview is null ? LedgerNotFound() : Ok(overview);
    }

    [HttpPost]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromBody] SavePlanRequest request, CancellationToken cancellationToken)
    {
        var result = await _planService.SaveAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{planId:guid}/pause")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pause(
        Guid planId,
        [FromBody] PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _planService.PauseAsync(planId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{planId:guid}/resume")]
    [ProducesResponseType<PlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(
        Guid planId,
        [FromBody] PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _planService.ResumeAsync(planId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("occurrences/{occurrenceId:guid}/skip")]
    [ProducesResponseType<PlanOccurrenceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SkipOccurrence(
        Guid occurrenceId,
        [FromBody] PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _planService.SkipOccurrenceAsync(occurrenceId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("occurrences/{occurrenceId:guid}/retry")]
    [ProducesResponseType<PlanOccurrenceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryOccurrence(
        Guid occurrenceId,
        [FromBody] PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _planService.RetryOccurrenceAsync(occurrenceId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("occurrences/{occurrenceId:guid}/record")]
    [ProducesResponseType<RecordOccurrenceResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordOccurrence(
        Guid occurrenceId,
        [FromBody] PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _executionService.RecordOccurrenceAsync(occurrenceId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("preview")]
    [ProducesResponseType<PlanPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(
        [FromBody] PlanPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _planService.PreviewAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("run")]
    [ProducesResponseType<PlanRunResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        return Ok(await _executionService.RunDueAsync(cancellationToken));
    }
}
