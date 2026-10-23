using System.Text.Json;
using AssetKeeper.Application.Backup;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Infrastructure.Backup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssetKeeper.Api.Controllers;

[ApiController]
[Route("api/ledger")]
public sealed class BackupController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BackupService _backupService;
    private readonly BusinessDateProvider _dates;

    public BackupController(BackupService backupService, BusinessDateProvider dates)
    {
        _backupService = backupService;
        _dates = dates;
    }

    [HttpGet("backup")]
    [ProducesResponseType<BackupDocument>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var document = await _backupService.ExportAsync(cancellationToken);

        if (document is null)
        {
            return LedgerNotFound();
        }

        var content = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);

        return File(content, "application/json; charset=utf-8", $"asset-keeper-backup-{_dates.Today:yyyyMMdd}.json");
    }

    [HttpGet("export/details.csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportDetails(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _backupService.ExportDetailsAsync(from, to, cancellationToken);

            return result is null
                ? LedgerNotFound()
                : File(result.Content, "text/csv; charset=utf-8", result.FileName);
        }
        catch (BackupValidationException exception)
        {
            return ValidationProblemResponse(exception.Errors);
        }
    }

    [HttpPost("backup/validate")]
    [ProducesResponseType<BackupValidationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Validate(
        [FromBody] BackupDocument? backup,
        CancellationToken cancellationToken)
    {
        if (backup is null)
        {
            return ValidationProblemResponse(["备份内容为空。"]);
        }

        var result = await _backupService.ValidateAsync(backup, cancellationToken);

        return Ok(result);
    }

    [HttpPost("backup/restore")]
    [ProducesResponseType<RestoreBackupResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Restore(
        [FromBody] RestoreBackupRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValidationProblemResponse(["恢复请求为空。"]);
        }

        try
        {
            var result = await _backupService.RestoreAsync(request, cancellationToken);

            return Ok(result);
        }
        catch (BackupValidationException exception)
        {
            return ValidationProblemResponse(exception.Errors);
        }
        catch (BackupConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "需要确认替换账本",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "恢复失败",
                Detail = "恢复未完成，当前账本保持不变。请核对备份后重试。",
                Status = StatusCodes.Status500InternalServerError
            });
        }
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

    private IActionResult ValidationProblemResponse(IReadOnlyList<string> errors)
    {
        var modelState = new ModelStateDictionary();

        foreach (var error in errors)
        {
            modelState.AddModelError(string.Empty, error);
        }

        return ValidationProblem(modelState);
    }
}
