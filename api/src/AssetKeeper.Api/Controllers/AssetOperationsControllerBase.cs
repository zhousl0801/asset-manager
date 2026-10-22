using AssetKeeper.Application.Assets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

public abstract class AssetOperationsControllerBase : ControllerBase
{
    protected IActionResult ToActionResult<T>(AssetOperationResult<T> result)
        where T : class
    {
        return result.Status switch
        {
            AssetOperationStatus.Success => Ok(result.Result),
            AssetOperationStatus.Created => Ok(result.Result),
            AssetOperationStatus.NotFound => NotFound(new ProblemDetails
            {
                Title = "操作未完成",
                Detail = result.Errors[0],
                Status = StatusCodes.Status404NotFound
            }),
            AssetOperationStatus.Invalid => ValidationProblem(BuildModelState(result.Errors)),
            AssetOperationStatus.Conflict => Conflict(new ProblemDetails
            {
                Title = "操作冲突",
                Detail = result.Errors[0],
                Status = StatusCodes.Status409Conflict
            }),
            AssetOperationStatus.Unavailable => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "行情服务暂不可用",
                Detail = result.Errors[0],
                Status = StatusCodes.Status502BadGateway
            }),
            _ => throw new InvalidOperationException("未知的操作结果。")
        };
    }

    protected IActionResult LedgerNotFound()
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
