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

    [HttpGet("calendar")]
    [ProducesResponseType<PlanCalendarDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult Calendar([FromQuery] int year, [FromQuery] int month)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            ModelState.AddModelError("month", "年份需在 2000 至 2100 之间，月份需在 1 至 12 之间。");
            return ValidationProblem(ModelState);
        }

        return Ok(_planService.GetCalendar(year, month));
    }

    [HttpGet("occurrences")]
    [ProducesResponseType<PlanOccurrencePageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Occurrences(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            ModelState.AddModelError("page", "页码需从 1 开始。");
        }

        if (pageSize is < 1 or > 50)
        {
            ModelState.AddModelError("pageSize", "每页数量需在 1 至 50 之间。");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await _planService.GetOccurrencePageAsync(page, pageSize, cancellationToken);

        return result is null ? LedgerNotFound() : Ok(result);
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

    [HttpPost("run")]
    [ProducesResponseType<PlanRunResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        return Ok(await _executionService.RunDueAsync(cancellationToken));
    }
}
