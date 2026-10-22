using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Ledgers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/entries")]
public sealed class LedgerEntriesController : ControllerBase
{
    private readonly LedgerEntryService _ledgerEntryService;

    public LedgerEntriesController(LedgerEntryService ledgerEntryService)
    {
        _ledgerEntryService = ledgerEntryService;
    }

    [HttpGet]
    [ProducesResponseType<EntryPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPeriod(
        [FromQuery] EntryPeriod period = EntryPeriod.Month,
        [FromQuery] DateOnly? anchor = null,
        [FromQuery] LedgerEntryKind? kind = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _ledgerEntryService.GetPeriodAsync(
            period,
            anchor,
            kind,
            category,
            search,
            cancellationToken,
            page,
            pageSize);

        if (result is null)
        {
            return LedgerNotFound();
        }

        return Ok(result);
    }

    [HttpGet("{entryId:guid}")]
    [ProducesResponseType<LedgerEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await _ledgerEntryService.GetEntryAsync(entryId, cancellationToken);

        if (entry is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "记录不存在",
                Detail = "该记录不存在或已被移除。",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(entry);
    }

    [HttpPost]
    [ProducesResponseType<EntryMutationResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<EntryMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _ledgerEntryService.CreateAsync(request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{entryId:guid}")]
    [ProducesResponseType<EntryMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid entryId,
        [FromBody] UpdateLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _ledgerEntryService.UpdateAsync(entryId, request, cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{entryId:guid}/reverse")]
    [ProducesResponseType<EntryMutationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reverse(
        Guid entryId,
        [FromBody] ReverseLedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _ledgerEntryService.ReverseAsync(entryId, request, cancellationToken);

        return ToActionResult(result);
    }

    private IActionResult ToActionResult(EntryOperationResult result)
    {
        return result.Status switch
        {
            EntryOperationStatus.Created => CreatedAtAction(
                nameof(GetById),
                new { entryId = result.Result!.Entry.Id },
                result.Result),
            EntryOperationStatus.Success => Ok(result.Result),
            EntryOperationStatus.NotFound => NotFound(new ProblemDetails
            {
                Title = "操作未完成",
                Detail = result.Errors[0],
                Status = StatusCodes.Status404NotFound
            }),
            EntryOperationStatus.Invalid => ValidationProblem(BuildModelState(result.Errors)),
            EntryOperationStatus.Conflict => Conflict(new ProblemDetails
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
