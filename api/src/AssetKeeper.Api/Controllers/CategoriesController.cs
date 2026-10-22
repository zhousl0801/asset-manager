using AssetKeeper.Application.Ledgers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger/categories")]
public sealed class CategoriesController : ControllerBase
{
    private readonly CategoryService _categoryService;

    public CategoriesController(CategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EntryCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetAsync(cancellationToken);

        if (categories is null)
        {
            return LedgerNotFound();
        }

        return Ok(categories);
    }

    [HttpPost]
    [ProducesResponseType<EntryCategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _categoryService.CreateAsync(request, cancellationToken);

        return result.Status switch
        {
            CategoryOperationStatus.Created => Created(string.Empty, result.Category),
            CategoryOperationStatus.Conflict => ConflictProblem(result.Errors),
            CategoryOperationStatus.NotFound => LedgerNotFound(),
            _ => ValidationProblem(result.Errors)
        };
    }

    [HttpPut("{categoryId:guid}")]
    [ProducesResponseType<EntryCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid categoryId,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _categoryService.UpdateAsync(categoryId, request, cancellationToken);

        return result.Status switch
        {
            CategoryOperationStatus.Success => Ok(result.Category),
            CategoryOperationStatus.Conflict => ConflictProblem(result.Errors),
            CategoryOperationStatus.NotFound => NotFoundProblem("分类不存在", result.Errors.FirstOrDefault() ?? "分类不存在。"),
            _ => ValidationProblem(result.Errors)
        };
    }

    [HttpPut("order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(
        [FromBody] ReorderCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _categoryService.ReorderAsync(request, cancellationToken);

        return result.Status switch
        {
            CategoryOperationStatus.Success => NoContent(),
            CategoryOperationStatus.NotFound => LedgerNotFound(),
            _ => ValidationProblem(result.Errors)
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
            Title = "分类冲突",
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
