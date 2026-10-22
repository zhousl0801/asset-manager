using AssetKeeper.Application.Funds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/fund-groups")]
public sealed class FundGroupsController : ControllerBase
{
    private readonly FundGroupService _fundGroupService;

    public FundGroupsController(FundGroupService fundGroupService)
    {
        _fundGroupService = fundGroupService;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FundGroupDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var groups = await _fundGroupService.GetAsync(cancellationToken);

        return groups is null ? LedgerNotFound() : Ok(groups);
    }

    [HttpPost]
    [ProducesResponseType<FundGroupDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFundGroupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundGroupService.CreateAsync(request, cancellationToken);

        return result.Status switch
        {
            FundGroupOperationStatus.Created => Created(string.Empty, result.Group),
            FundGroupOperationStatus.Conflict => ConflictProblem(result.Errors),
            FundGroupOperationStatus.NotFound => LedgerNotFound(),
            _ => ValidationProblem(result.Errors)
        };
    }

    [HttpPut("{groupId:guid}")]
    [ProducesResponseType<FundGroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(
        Guid groupId,
        [FromBody] RenameFundGroupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundGroupService.RenameAsync(groupId, request, cancellationToken);

        return result.Status switch
        {
            FundGroupOperationStatus.Success => Ok(result.Group),
            FundGroupOperationStatus.Conflict => ConflictProblem(result.Errors),
            FundGroupOperationStatus.NotFound => NotFoundProblem("分组不存在", result.Errors.FirstOrDefault() ?? "分组不存在。"),
            _ => ValidationProblem(result.Errors)
        };
    }

    [HttpDelete("{groupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _fundGroupService.DeleteAsync(groupId, cancellationToken);

        return result.Status switch
        {
            FundGroupOperationStatus.Success => NoContent(),
            FundGroupOperationStatus.Conflict => ConflictProblem(result.Errors),
            _ => NotFoundProblem("分组不存在", result.Errors.FirstOrDefault() ?? "分组不存在。")
        };
    }

    [HttpPut("positions/{positionId:guid}")]
    [ProducesResponseType<FundGroupAssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(
        Guid positionId,
        [FromBody] AssignFundGroupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _fundGroupService.AssignAsync(positionId, request, cancellationToken);

        return result.Status switch
        {
            FundGroupOperationStatus.Success => Ok(result.Assignment),
            FundGroupOperationStatus.Conflict => ConflictProblem(result.Errors),
            _ => NotFoundProblem("持仓不存在", result.Errors.FirstOrDefault() ?? "持仓不存在。")
        };
    }

    private IActionResult ValidationProblem(IReadOnlyList<string> errors)
    {
        var modelState = new ModelStateDictionary();

        foreach (var error in errors)
        {
            modelState.AddModelError(string.Empty, error);
        }

        return ValidationProblem(modelState);
    }

    private IActionResult ConflictProblem(IReadOnlyList<string> errors)
    {
        return Conflict(new ProblemDetails
        {
            Title = "分组冲突",
            Detail = string.Join(" ", errors),
            Status = StatusCodes.Status409Conflict
        });
    }

    private IActionResult NotFoundProblem(string title, string detail)
    {
        return NotFound(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status404NotFound
        });
    }

    private IActionResult LedgerNotFound()
    {
        return NotFoundProblem("账本尚未初始化", "请先设置账本起始日期和现金余额。");
    }
}
