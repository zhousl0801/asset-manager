using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetKeeper.Application.Backup;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Domain.Plans;
using AssetKeeper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AssetKeeper.Infrastructure.Backup;

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AppDbContext _db;
    private readonly BusinessDateProvider _dates;

    public BackupService(AppDbContext db, BusinessDateProvider dates)
    {
        _db = db;
        _dates = dates;
    }

    public async Task<BackupDocument?> ExportAsync(CancellationToken cancellationToken)
    {
        return await BuildDocumentAsync(cancellationToken);
    }

    public async Task<BackupValidationDto> ValidateAsync(
        BackupDocument? backup,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var duplicateRisks = new List<string>();

        if (backup is null)
        {
            return new BackupValidationDto(
                false,
                false,
                null,
                EmptySummary(),
                null,
                ["备份内容为空。"],
                [],
                []);
        }

        if (!string.Equals(backup.Format, BackupDocument.CurrentFormat, StringComparison.Ordinal))
        {
            errors.Add($"备份格式不受支持：{backup.Format}。");
        }

        if (backup.Version < BackupDocument.MinimumSupportedVersion
            || backup.Version > BackupDocument.CurrentVersion)
        {
            errors.Add($"备份版本 {backup.Version} 不受支持，当前支持版本 {BackupDocument.MinimumSupportedVersion}-{BackupDocument.CurrentVersion}。");
        }

        if (backup.ExportedAt == default)
        {
            errors.Add("备份缺少导出时间。");
        }

        var parsed = Parse(backup, errors);
        var currentDocument = await BuildDocumentAsync(cancellationToken);
        var currentParsed = currentDocument is null ? null : Parse(currentDocument, new List<string>());
        var currentSummary = currentParsed is null ? null : Summarize(currentParsed, Replay(currentParsed, new List<string>(), new List<string>()));

        if (errors.Count == 0)
        {
            ValidateBasics(parsed, errors);
            ValidateLinks(parsed, errors, duplicateRisks);
            var replay = ValidateReplay(parsed, errors, warnings);
            var summary = Summarize(parsed, replay);

            if (currentDocument is not null)
            {
                if (currentDocument.Ledger.Id != backup.Ledger.Id)
                {
                    errors.Add("备份属于另一个账本，不能直接覆盖当前账本。请先导出并清空当前账本，或使用相同账本的备份。");
                }
                else
                {
                    duplicateRisks.Add(
                        $"恢复将替换当前账本：{currentDocument.Entries.Count} 条收支、{currentDocument.AssetTransactions.Count} 条资产记录、{currentDocument.Plans.Count} 个计划、{currentDocument.PlanOccurrences.Count} 个执行期次。");
                    duplicateRisks.Add("恢复采用替换而不是追加；恢复后请求回执和计划期次均按备份保留，不会再次生成。");
                }
            }

            var token = errors.Count == 0
                ? ComputeToken(backup, currentSummary, currentDocument?.Ledger.Version ?? 0)
                : null;

            return new BackupValidationDto(
                errors.Count == 0,
                currentDocument is not null,
                token,
                summary,
                currentSummary,
                errors.Distinct().ToList(),
                warnings.Distinct().ToList(),
                duplicateRisks.Distinct().ToList());
        }

        var fallbackSummary = Summarize(parsed, null);

        return new BackupValidationDto(
            false,
            currentDocument is not null,
            null,
            fallbackSummary,
            currentSummary,
            errors.Distinct().ToList(),
            warnings.Distinct().ToList(),
            duplicateRisks.Distinct().ToList());
    }

    public async Task<RestoreBackupResultDto> RestoreAsync(
        RestoreBackupRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Backup is null)
        {
            throw new BackupValidationException(["备份内容为空。"]);
        }

        var validation = await ValidateAsync(request.Backup, cancellationToken);

        if (!validation.IsValid)
        {
            throw new BackupValidationException(validation.Errors);
        }

        if (!string.Equals(validation.ValidationToken, request.ValidationToken, StringComparison.Ordinal))
        {
            throw new BackupValidationException(["备份或当前账本已变化，请重新校验后再恢复。"]);
        }

        if (validation.WillReplace && !request.ReplaceExisting)
        {
            throw new BackupConflictException("账本已存在，请确认替换当前账本后再恢复。");
        }

        var errors = new List<string>();
        var parsed = Parse(request.Backup, errors);

        if (errors.Count > 0)
        {
            throw new BackupValidationException(errors);
        }

        await PersistAsync(parsed, cancellationToken);

        return new RestoreBackupResultDto(
            true,
            validation.WillReplace,
            validation.Summary,
            _dates.UtcNow,
            validation.Warnings);
    }

    public async Task<CsvExportResult?> ExportDetailsAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var ledger = await _db.Ledgers
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var fromDate = from ?? ledger.StartDate;
        var toDate = to ?? _dates.Today;

        if (fromDate < ledger.StartDate)
        {
            fromDate = ledger.StartDate;
        }

        if (toDate > _dates.Today)
        {
            toDate = _dates.Today;
        }

        if (toDate < fromDate)
        {
            throw new BackupValidationException(["导出的结束日期不能早于开始日期。"]);
        }

        var entries = await _db.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.LedgerId == ledger.Id
                && entry.BusinessDate >= fromDate
                && entry.BusinessDate <= toDate)
            .OrderBy(entry => entry.BusinessDate)
            .ThenBy(entry => entry.BusinessSequence)
            .ThenBy(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
        var adjustments = await _db.CashAdjustments
            .AsNoTracking()
            .Where(adjustment => adjustment.LedgerId == ledger.Id
                && adjustment.BusinessDate >= fromDate
                && adjustment.BusinessDate <= toDate)
            .OrderBy(adjustment => adjustment.BusinessDate)
            .ThenBy(adjustment => adjustment.BusinessSequence)
            .ToListAsync(cancellationToken);
        var transactions = await _db.AssetTransactions
            .AsNoTracking()
            .Where(transaction => transaction.LedgerId == ledger.Id
                && transaction.BusinessDate >= fromDate
                && transaction.BusinessDate <= toDate)
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence)
            .ToListAsync(cancellationToken);
        var occurrences = await _db.PlanOccurrences
            .AsNoTracking()
            .Where(occurrence => occurrence.LedgerId == ledger.Id
                && occurrence.DueDate >= fromDate
                && occurrence.DueDate <= toDate)
            .OrderBy(occurrence => occurrence.DueDate)
            .ThenBy(occurrence => occurrence.DueTime)
            .ToListAsync(cancellationToken);

        var rows = new List<CsvRow>();

        rows.AddRange(entries.Select(entry => new CsvRow(
            entry.BusinessDate,
            entry.Kind == LedgerEntryKind.Income ? "日常收入" : "日常支出",
            entry.Kind == LedgerEntryKind.Income ? "收入" : "支出",
            entry.Amount,
            entry.Kind == LedgerEntryKind.Income ? entry.Amount : -entry.Amount,
            null,
            null,
            null,
            entry.Category,
            BuildEntryStatus(entry),
            SourceLabel(entry.Source),
            BuildEntryLinks(entry))));

        rows.AddRange(adjustments.Select(adjustment => new CsvRow(
            adjustment.BusinessDate,
            "现金核对",
            adjustment.Difference > 0 ? "调增" : adjustment.Difference < 0 ? "调减" : "一致",
            adjustment.Difference,
            adjustment.Difference,
            null,
            null,
            null,
            adjustment.Reason,
            adjustment.IsReversed ? "已撤销" : "有效",
            "手动",
            adjustment.Id.ToString())));

        rows.AddRange(transactions.Select(transaction => new CsvRow(
            transaction.BusinessDate,
            TransactionLabel(transaction.Kind),
            transaction.CashEffect > 0 ? "转入现金" : transaction.CashEffect < 0 ? "转出资金" : "不改变现金",
            transaction.Amount,
            transaction.CashEffect,
            transaction.Quantity,
            transaction.Price,
            transaction.Fee,
            transaction.Subject ?? "基金 / 积存金 / 股票",
            transaction.IsReversed ? "已撤销" : "有效",
            "业务记录",
            BuildTransactionLinks(transaction))));

        rows.AddRange(occurrences.Select(occurrence => new CsvRow(
            occurrence.DueDate,
            "自动计划",
            PlanTypeLabel(occurrence.Type),
            occurrence.Amount,
            null,
            null,
            null,
            null,
            occurrence.PlanName,
            occurrence.Status == PlanOccurrenceStatus.Completed
                ? "已记账"
                : occurrence.Status == PlanOccurrenceStatus.Waiting
                    ? "待处理"
                    : occurrence.Status == PlanOccurrenceStatus.Failed
                        ? "执行失败"
                        : occurrence.Status == PlanOccurrenceStatus.Skipped ? "已跳过" : "待执行",
            "自动",
            BuildOccurrenceLinks(occurrence))));

        var ordered = rows
            .OrderBy(row => row.Date)
            .ThenBy(row => row.Type, StringComparer.Ordinal)
            .ThenBy(row => row.Subject, StringComparer.Ordinal)
            .ToList();
        var builder = new StringBuilder();
        builder.AppendLine("日期,业务类型,方向,金额,现金影响,数量,净值或单价,费用,分类或标的,状态,来源,关联标识");

        foreach (var row in ordered)
        {
            builder.AppendJoin(',', new[]
            {
                Csv(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(row.Type),
                Csv(row.Direction),
                Csv(FormatAmount(row.Amount)),
                Csv(row.CashEffect is null ? string.Empty : FormatAmount(row.CashEffect.Value)),
                Csv(row.Quantity?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.Price?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.Fee?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(row.Subject),
                Csv(row.Status),
                Csv(row.Source),
                Csv(row.Links)
            });
            builder.AppendLine();
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(builder.ToString());
        var bytes = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);

        return new CsvExportResult(
            bytes,
            $"asset-keeper-details-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv",
            ordered.Count);
    }

    private async Task<BackupDocument?> BuildDocumentAsync(CancellationToken cancellationToken)
    {
        var ledger = await _db.Ledgers
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var ledgerId = ledger.Id;
        var cashAccount = await _db.CashAccounts.AsNoTracking().SingleAsync(item => item.LedgerId == ledgerId, cancellationToken);
        var provident = await _db.ProvidentFundAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.LedgerId == ledgerId, cancellationToken);
        var gold = await _db.GoldHoldings.AsNoTracking().SingleOrDefaultAsync(item => item.LedgerId == ledgerId, cancellationToken);
        var categories = await _db.EntryCategories.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.Kind).ThenBy(item => item.SortOrder).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        var entries = await _db.LedgerEntries.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.BusinessDate).ThenBy(item => item.BusinessSequence).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var adjustments = await _db.CashAdjustments.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.BusinessDate).ThenBy(item => item.BusinessSequence).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var groups = await _db.FundGroups.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        var positions = await _db.FundPositions.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.Name).ThenBy(item => item.ShareClass).ThenBy(item => item.Platform).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var stockPositions = await _db.StockPositions.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.Market).ThenBy(item => item.Code).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var transactions = await _db.AssetTransactions.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.BusinessDate).ThenBy(item => item.BusinessSequence).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var prices = await _db.PriceObservations.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.EffectiveDate).ThenBy(item => item.RevisionOrder).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var plans = await _db.AutoPlans.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var occurrences = await _db.PlanOccurrences.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.DueDate).ThenBy(item => item.DueTime).ThenBy(item => item.Id).ToListAsync(cancellationToken);
        var receipts = await _db.MutationReceipts.AsNoTracking().Where(item => item.LedgerId == ledgerId).OrderBy(item => item.CreatedAt).ThenBy(item => item.RequestId).ToListAsync(cancellationToken);

        return new BackupDocument
        {
            Format = BackupDocument.CurrentFormat,
            Version = BackupDocument.CurrentVersion,
            ExportedAt = _dates.UtcNow,
            Ledger = ToDto<BackupLedger>(ledger),
            CashAccount = ToDto<BackupCashAccount>(cashAccount),
            ProvidentAccount = provident is null ? null : ToDto<BackupProvidentFundAccount>(provident),
            GoldHolding = gold is null ? null : ToDto<BackupGoldHolding>(gold),
            Categories = categories.Select(ToDto<BackupEntryCategory>).ToList(),
            Entries = entries.Select(ToDto<BackupLedgerEntry>).ToList(),
            CashAdjustments = adjustments.Select(ToDto<BackupCashAdjustment>).ToList(),
            FundGroups = groups.Select(ToDto<BackupFundGroup>).ToList(),
            FundPositions = positions.Select(ToDto<BackupFundPosition>).ToList(),
            StockPositions = stockPositions.Select(ToDto<BackupStockPosition>).ToList(),
            AssetTransactions = transactions.Select(ToDto<BackupAssetTransaction>).ToList(),
            PriceObservations = prices.Select(ToDto<BackupPriceObservation>).ToList(),
            Plans = plans.Select(ToDto<BackupPlan>).ToList(),
            PlanOccurrences = occurrences.Select(ToDto<BackupPlanOccurrence>).ToList(),
            MutationReceipts = receipts.Select(ToDto<BackupMutationReceipt>).ToList()
        };
    }

    private static ParsedBackup Parse(BackupDocument document, List<string> errors)
    {
        try
        {
            if (document.Ledger is null || document.CashAccount is null)
            {
                errors.Add("备份缺少账本或现金账户。");
                document.Ledger ??= new BackupLedger();
                document.CashAccount ??= new BackupCashAccount();
            }

            var parsed = new ParsedBackup
            {
                Ledger = ToEntity<Ledger>(document.Ledger),
                CashAccount = ToEntity<CashAccount>(document.CashAccount),
                ProvidentAccount = document.ProvidentAccount is null ? null : ToEntity<ProvidentFundAccount>(document.ProvidentAccount),
                GoldHolding = document.GoldHolding is null ? null : ToEntity<GoldHolding>(document.GoldHolding),
                Categories = document.Categories?.Select(ToEntity<EntryCategory>).ToList() ?? [],
                Entries = document.Entries?.Select(ToEntity<LedgerEntry>).ToList() ?? [],
                CashAdjustments = document.CashAdjustments?.Select(ToEntity<CashAdjustment>).ToList() ?? [],
                FundGroups = document.FundGroups?.Select(ToEntity<FundGroup>).ToList() ?? [],
                FundPositions = document.FundPositions?.Select(ToEntity<FundPosition>).ToList() ?? [],
                StockPositions = document.StockPositions?.Select(ToEntity<StockPosition>).ToList() ?? [],
                AssetTransactions = document.AssetTransactions?.Select(ToEntity<AssetTransaction>).ToList() ?? [],
                PriceObservations = document.PriceObservations?.Select(ToEntity<PriceObservation>).ToList() ?? [],
                Plans = document.Plans?.Select(ToEntity<AutoPlan>).ToList() ?? [],
                PlanOccurrences = document.PlanOccurrences?.Select(ToEntity<PlanOccurrence>).ToList() ?? [],
                MutationReceipts = document.MutationReceipts?.Select(ToEntity<MutationReceipt>).ToList() ?? []
            };
            parsed.Ledger.SetCashAccount(parsed.CashAccount);

            return parsed;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or TargetInvocationException)
        {
            errors.Add($"备份字段格式无效：{exception.InnerException?.Message ?? exception.Message}");
            return new ParsedBackup();
        }
    }

    private void ValidateBasics(ParsedBackup parsed, List<string> errors)
    {
        if (parsed.Ledger.Id == Guid.Empty)
        {
            errors.Add("账本标识为空。");
        }

        if (parsed.Ledger.StartDate == default)
        {
            errors.Add("账本起始日期为空。");
        }

        if (parsed.Ledger.StartDate > _dates.Today)
        {
            errors.Add("账本起始日期晚于今天。");
        }

        if (parsed.Ledger.SingletonSlot != 1)
        {
            errors.Add("账本单例标识必须为 1。");
        }

        if (!string.Equals(parsed.Ledger.Currency, Ledger.DefaultCurrency, StringComparison.Ordinal))
        {
            errors.Add($"备份币种 {parsed.Ledger.Currency} 不受支持。");
        }

        if (!string.Equals(parsed.Ledger.TimeZone, Ledger.DefaultTimeZone, StringComparison.Ordinal))
        {
            errors.Add($"备份时区 {parsed.Ledger.TimeZone} 不受支持。");
        }

        if (parsed.CashAccount.Id == Guid.Empty || parsed.CashAccount.LedgerId != parsed.Ledger.Id)
        {
            errors.Add("现金账户缺少有效标识或不属于该账本。");
        }

        if (parsed.CashAccount.Balance < 0m || parsed.CashAccount.Balance > CashAccount.MaximumBalance)
        {
            errors.Add("现金余额超出有效范围。");
        }

        if (!parsed.CashAccount.HistoryConfirmed
            || parsed.CashAccount.OpeningBalance is null
            || parsed.CashAccount.OpeningDate is null)
        {
            errors.Add("现金起始余额或起始日期缺失，无法可靠恢复。");
        }

        if (parsed.ProvidentAccount is not null
            && (parsed.ProvidentAccount.LedgerId != parsed.Ledger.Id || parsed.ProvidentAccount.Balance < 0m))
        {
            errors.Add("公积金账户不属于该账本或余额无效。");
        }

        if (parsed.GoldHolding is not null)
        {
            if (parsed.GoldHolding.LedgerId != parsed.Ledger.Id
                || parsed.GoldHolding.Grams < 0m
                || parsed.GoldHolding.Cost < 0m
                || parsed.GoldHolding.Quote <= 0m
                || parsed.GoldHolding.Grams == 0m && parsed.GoldHolding.Cost != 0m)
            {
                errors.Add("积存金持仓数据无效。");
            }
        }

        foreach (var stock in parsed.StockPositions)
        {
            if (stock.LedgerId != parsed.Ledger.Id
                || string.IsNullOrWhiteSpace(stock.Code)
                || string.IsNullOrWhiteSpace(stock.Name)
                || stock.Shares < 0m
                || stock.Cost < 0m
                || stock.Price <= 0m
                || stock.Shares == 0m && stock.Cost != 0m
                || stock.PreviousClose is <= 0m
                || stock.PreviousCloseDate is not null && stock.PreviousClose is null)
            {
                errors.Add($"股票持仓 {stock.Id} 的数据无效。");
            }
        }

        if (parsed.Ledger.NextFactSequence < 0)
        {
            errors.Add("账本业务顺序号无效。");
        }
    }

    private void ValidateLinks(
        ParsedBackup parsed,
        List<string> errors,
        List<string> duplicateRisks)
    {
        CheckDuplicateIds(parsed.Categories.Select(item => item.Id), "分类", errors, duplicateRisks);
        CheckDuplicateIds(parsed.Entries.Select(item => item.Id), "收支记录", errors, duplicateRisks);
        CheckDuplicateIds(parsed.CashAdjustments.Select(item => item.Id), "现金核对", errors, duplicateRisks);
        CheckDuplicateIds(parsed.FundGroups.Select(item => item.Id), "投资分组", errors, duplicateRisks);
        CheckDuplicateIds(parsed.FundPositions.Select(item => item.Id), "基金持仓", errors, duplicateRisks);
        CheckDuplicateIds(parsed.StockPositions.Select(item => item.Id), "股票持仓", errors, duplicateRisks);
        CheckDuplicateIds(parsed.AssetTransactions.Select(item => item.Id), "资产交易", errors, duplicateRisks);
        CheckDuplicateIds(parsed.PriceObservations.Select(item => item.Id), "行情观测", errors, duplicateRisks);
        CheckDuplicateIds(parsed.Plans.Select(item => item.Id), "自动计划", errors, duplicateRisks);
        CheckDuplicateIds(parsed.PlanOccurrences.Select(item => item.Id), "计划期次", errors, duplicateRisks);
        CheckDuplicateIds(parsed.Entries.Select(item => item.RequestId), "收支请求标识", errors, duplicateRisks);
        CheckDuplicateIds(parsed.AssetTransactions.Select(item => item.RequestId), "资产请求标识", errors, duplicateRisks);
        CheckDuplicateIds(parsed.MutationReceipts.Select(item => item.RequestId), "请求回执标识", errors, duplicateRisks);

        foreach (var category in parsed.Categories)
        {
            if (category.LedgerId != parsed.Ledger.Id || string.IsNullOrWhiteSpace(category.Name))
            {
                errors.Add($"分类「{category.Name}」不属于该账本或名称为空。");
            }
        }

        var categoryKeys = parsed.Categories
            .GroupBy(item => (item.Kind, item.Name))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Kind}/{group.Key.Name}")
            .ToList();

        if (categoryKeys.Count > 0)
        {
            var message = $"分类存在重复：{string.Join("、", categoryKeys.Take(5))}。";
            errors.Add(message);
            duplicateRisks.Add(message);
        }

        var categorySet = parsed.Categories.Select(item => (item.Kind, item.Name)).ToHashSet();
        var entryIds = parsed.Entries.Select(item => item.Id).ToHashSet();
        var adjustmentIds = parsed.CashAdjustments.Select(item => item.Id).ToHashSet();
        var groupIds = parsed.FundGroups.Select(item => item.Id).ToHashSet();
        var positionIds = parsed.FundPositions.Select(item => item.Id).ToHashSet();
        var stockPositionIds = parsed.StockPositions.Select(item => item.Id).ToHashSet();
        var transactionIds = parsed.AssetTransactions.Select(item => item.Id).ToHashSet();
        var planIds = parsed.Plans.Select(item => item.Id).ToHashSet();

        foreach (var entry in parsed.Entries)
        {
            if (entry.LedgerId != parsed.Ledger.Id
                || entry.Amount <= 0m
                || entry.BusinessDate < parsed.Ledger.StartDate
                || entry.BusinessDate > _dates.Today
                || !categorySet.Contains((entry.Kind, entry.Category)))
            {
                errors.Add($"收支记录 {entry.Id} 的账本、金额、日期或分类无效。");
            }

            if (entry.RelatedCashAdjustmentId is not null && !adjustmentIds.Contains(entry.RelatedCashAdjustmentId.Value))
            {
                errors.Add($"收支记录 {entry.Id} 关联的现金核对不存在。");
            }

            if (entry.Status == LedgerEntryStatus.Reversed && entry.ReversedAt is null)
            {
                errors.Add($"已撤销的收支记录 {entry.Id} 缺少撤销时间。");
            }
        }

        foreach (var adjustment in parsed.CashAdjustments)
        {
            if (adjustment.LedgerId != parsed.Ledger.Id
                || adjustment.BusinessDate < parsed.Ledger.StartDate
                || adjustment.BusinessDate > _dates.Today
                || string.IsNullOrWhiteSpace(adjustment.Reason))
            {
                errors.Add($"现金核对 {adjustment.Id} 的账本、日期或原因无效。");
            }
        }

        foreach (var group in parsed.FundGroups)
        {
            if (group.LedgerId != parsed.Ledger.Id || string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add($"投资分组 {group.Id} 不属于该账本或名称为空。");
            }
        }

        var groupNames = parsed.FundGroups
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (groupNames.Count > 0)
        {
            var message = $"投资分组存在重名：{string.Join("、", groupNames)}。";
            errors.Add(message);
            duplicateRisks.Add(message);
        }

        foreach (var position in parsed.FundPositions)
        {
            if (position.LedgerId != parsed.Ledger.Id
                || string.IsNullOrWhiteSpace(position.Name)
                || position.Shares < 0m
                || position.Cost < 0m
                || position.NetValue <= 0m
                || position.Shares == 0m && position.Cost != 0m)
            {
                errors.Add($"基金持仓 {position.Id} 的数据无效。");
            }

            if (position.FundGroupId is not null && !groupIds.Contains(position.FundGroupId.Value))
            {
                errors.Add($"基金持仓 {position.Id} 关联的投资分组不存在。");
            }
        }

        foreach (var stock in parsed.StockPositions)
        {
            if (stock.LedgerId != parsed.Ledger.Id
                || string.IsNullOrWhiteSpace(stock.Code)
                || string.IsNullOrWhiteSpace(stock.Name)
                || stock.Shares < 0m
                || stock.Cost < 0m
                || stock.Price <= 0m
                || stock.Shares == 0m && stock.Cost != 0m)
            {
                errors.Add($"股票持仓 {stock.Id} 的数据无效。");
            }
        }

        var stockKeys = parsed.StockPositions
            .GroupBy(item => $"{item.Market}/{item.Code.ToUpperInvariant()}")
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (stockKeys.Count > 0)
        {
            var message = $"股票持仓存在重复：{string.Join("、", stockKeys.Take(5))}。";
            errors.Add(message);
            duplicateRisks.Add(message);
        }

        foreach (var transaction in parsed.AssetTransactions)
        {
            if (transaction.LedgerId != parsed.Ledger.Id
                || transaction.BusinessDate < parsed.Ledger.StartDate
                || transaction.BusinessDate > _dates.Today)
            {
                errors.Add($"资产交易 {transaction.Id} 的账本或日期无效。");
            }

            if (transaction.FundPositionId is not null && !positionIds.Contains(transaction.FundPositionId.Value))
            {
                errors.Add($"资产交易 {transaction.Id} 关联的基金持仓不存在。");
            }

            if (transaction.StockPositionId is not null && !stockPositionIds.Contains(transaction.StockPositionId.Value))
            {
                errors.Add($"资产交易 {transaction.Id} 关联的股票持仓不存在。");
            }

            if (transaction.LinkedTransactionId is not null && !transactionIds.Contains(transaction.LinkedTransactionId.Value))
            {
                errors.Add($"资产交易 {transaction.Id} 关联的原始交易不存在。");
            }

            if (transaction.RelatedCashAdjustmentId is not null && !adjustmentIds.Contains(transaction.RelatedCashAdjustmentId.Value))
            {
                errors.Add($"资产交易 {transaction.Id} 关联的现金核对不存在。");
            }
        }

        foreach (var price in parsed.PriceObservations)
        {
            if (price.LedgerId != parsed.Ledger.Id
                || price.Price <= 0m
                || price.EffectiveDate < parsed.Ledger.StartDate
                || price.EffectiveDate > _dates.Today)
            {
                errors.Add($"行情观测 {price.Id} 的账本、价格或日期无效。");
            }

            if (price.FundPositionId is not null && !positionIds.Contains(price.FundPositionId.Value))
            {
                errors.Add($"行情观测 {price.Id} 关联的基金持仓不存在。");
            }

            if (price.StockPositionId is not null && !stockPositionIds.Contains(price.StockPositionId.Value))
            {
                errors.Add($"行情观测 {price.Id} 关联的股票持仓不存在。");
            }

            if (price.SourceTransactionId is not null && !transactionIds.Contains(price.SourceTransactionId.Value))
            {
                errors.Add($"行情观测 {price.Id} 关联的资产交易不存在。");
            }
        }

        foreach (var plan in parsed.Plans)
        {
            if (plan.LedgerId != parsed.Ledger.Id
                || string.IsNullOrWhiteSpace(plan.Name)
                || plan.Amount <= 0m)
            {
                errors.Add($"自动计划 {plan.Id} 的账本、名称或金额无效。");
            }

            if (plan.FundPositionId is not null && !positionIds.Contains(plan.FundPositionId.Value))
            {
                errors.Add($"自动计划 {plan.Id} 关联的基金持仓不存在。");
            }
        }

        foreach (var occurrence in parsed.PlanOccurrences)
        {
            if (occurrence.LedgerId != parsed.Ledger.Id
                || occurrence.Amount <= 0m
                || !planIds.Contains(occurrence.PlanId)
                || string.IsNullOrWhiteSpace(occurrence.PeriodKey))
            {
                errors.Add($"计划期次 {occurrence.Id} 的账本、金额、计划或周期无效。");
            }

            if (occurrence.FundPositionId is not null && !positionIds.Contains(occurrence.FundPositionId.Value))
            {
                errors.Add($"计划期次 {occurrence.Id} 关联的基金持仓不存在。");
            }

            if (occurrence.RelatedRecordId is not null
                && !entryIds.Contains(occurrence.RelatedRecordId.Value)
                && !transactionIds.Contains(occurrence.RelatedRecordId.Value)
                && !adjustmentIds.Contains(occurrence.RelatedRecordId.Value))
            {
                errors.Add($"计划期次 {occurrence.Id} 关联的业务记录不存在。");
            }
        }

        var duplicatePeriods = parsed.PlanOccurrences
            .GroupBy(item => (item.PlanId, item.PeriodKey))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.PlanId}/{group.Key.PeriodKey}")
            .ToList();

        if (duplicatePeriods.Count > 0)
        {
            var message = $"计划期次存在重复周期：{string.Join("、", duplicatePeriods.Take(5))}。";
            errors.Add(message);
            duplicateRisks.Add(message);
        }

        foreach (var receipt in parsed.MutationReceipts)
        {
            if (receipt.LedgerId != parsed.Ledger.Id
                || string.IsNullOrWhiteSpace(receipt.Operation)
                || string.IsNullOrWhiteSpace(receipt.Fingerprint)
                || string.IsNullOrWhiteSpace(receipt.ResponseJson))
            {
                errors.Add($"请求回执 {receipt.RequestId} 的账本或内容无效。");
            }
        }

        var sequences = parsed.Entries
            .Where(item => item.BusinessSequence > 0)
            .Select(item => item.BusinessSequence)
            .Concat(parsed.CashAdjustments.Where(item => item.BusinessSequence > 0).Select(item => item.BusinessSequence))
            .Concat(parsed.AssetTransactions.Where(item => item.BusinessSequence > 0).Select(item => item.BusinessSequence))
            .ToList();
        var duplicateSequences = sequences
            .GroupBy(sequence => sequence)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString(CultureInfo.InvariantCulture))
            .ToList();

        if (duplicateSequences.Count > 0)
        {
            var message = $"业务顺序号存在重复：{string.Join("、", duplicateSequences.Take(5))}。";
            errors.Add(message);
            duplicateRisks.Add(message);
        }

        if (sequences.Count > 0 && parsed.Ledger.NextFactSequence < sequences.Max())
        {
            errors.Add("账本业务顺序号小于备份中的最大已用顺序。");
        }
    }

    private ReplayResult? ValidateReplay(
        ParsedBackup parsed,
        List<string> errors,
        List<string> warnings)
    {
        var replay = Replay(parsed, warnings, new List<string>());

        if (replay is null)
        {
            errors.Add("备份内部业务链无法重放，拒绝恢复。");
            return null;
        }

        if (replay.CashBalance is decimal cash && cash != parsed.CashAccount.Balance)
        {
            errors.Add($"备份现金余额 {parsed.CashAccount.Balance:0.00} 与业务重放结果 {cash:0.00} 不一致。");
        }

        if (parsed.ProvidentAccount is not null
            && replay.ProvidentBalance is decimal provident
            && provident != parsed.ProvidentAccount.Balance)
        {
            errors.Add($"备份公积金余额 {parsed.ProvidentAccount.Balance:0.00} 与业务重放结果 {provident:0.00} 不一致。");
        }

        foreach (var position in parsed.FundPositions)
        {
            var state = replay.Funds.FirstOrDefault(fund => fund.PositionId == position.Id);

            if (state is null)
            {
                errors.Add($"基金持仓 {position.Id} 未参与业务重放。");
                continue;
            }

            if (!state.CanRebuild)
            {
                warnings.Add($"基金「{position.Name}」的起始日期待确认，保留原值和待确认状态。");
                continue;
            }

            if (state.Quantity != position.Shares
                || state.Cost != position.Cost
                || state.RealizedProfit != position.RealizedProfit)
            {
                errors.Add($"基金「{position.Name}」的份额、成本或已实现收益与业务重放结果不一致。");
            }

            if (state.Price is decimal price && price != position.NetValue)
            {
                errors.Add($"基金「{position.Name}」的净值与业务重放结果不一致。");
            }

            if (state.PriceDate is DateOnly priceDate && priceDate != position.NetValueDate)
            {
                errors.Add($"基金「{position.Name}」的净值日期与业务重放结果不一致。");
            }
        }

        foreach (var position in parsed.StockPositions)
        {
            var state = replay.Stocks.FirstOrDefault(stock => stock.PositionId == position.Id);

            if (state is null)
            {
                errors.Add($"股票持仓 {position.Id} 未参与业务重放。");
                continue;
            }

            if (!state.CanRebuild)
            {
                warnings.Add($"股票「{position.Name}」的起始日期待确认，保留原值和待确认状态。");
                continue;
            }

            if (state.Quantity != position.Shares
                || state.Cost != position.Cost
                || state.RealizedProfit != position.RealizedProfit)
            {
                errors.Add($"股票「{position.Name}」的数量、成本或已实现收益与业务重放结果不一致。");
            }

            if (state.Price is decimal price && price != position.Price)
            {
                errors.Add($"股票「{position.Name}」的最新价与业务重放结果不一致。");
            }

            if (state.PriceDate is DateOnly priceDate && priceDate != position.PriceDate)
            {
                errors.Add($"股票「{position.Name}」的行情日期与业务重放结果不一致。");
            }
        }

        if (parsed.GoldHolding is not null && replay.Gold is not null)
        {
            if (replay.Gold.CanRebuild
                && (replay.Gold.Quantity != parsed.GoldHolding.Grams
                    || replay.Gold.Cost != parsed.GoldHolding.Cost
                    || replay.Gold.RealizedProfit != parsed.GoldHolding.RealizedProfit))
            {
                errors.Add("积存金克数、成本或已实现收益与业务重放结果不一致。");
            }

            if (replay.Gold.Price is decimal goldPrice && goldPrice != parsed.GoldHolding.Quote)
            {
                errors.Add("积存金报价与业务重放结果不一致。");
            }
        }

        if (replay.Issues.Count > 0)
        {
            warnings.AddRange(replay.Issues
                .Distinct()
                .Select(issue => $"业务重放提示：{issue}。"));
        }

        return replay;
    }

    private ReplayResult? Replay(
        ParsedBackup parsed,
        List<string> warnings,
        List<string> errors)
    {
        try
        {
            var data = new LedgerReplayData(
                parsed.Ledger,
                parsed.Entries,
                parsed.CashAdjustments,
                parsed.AssetTransactions,
                parsed.PriceObservations,
                parsed.FundPositions,
                parsed.StockPositions,
                parsed.ProvidentAccount,
                parsed.GoldHolding);
            var facts = BusinessFact.From(data);
            var today = _dates.Today;

            return new LedgerReplayCalculator().Calculate(data, facts, today, currentDate: today);
        }
        catch (Exception exception) when (exception is HistoryValidationException or ArgumentException or InvalidOperationException)
        {
            errors.Add(exception.Message);
            warnings.Add($"业务重放失败：{exception.Message}");
            return null;
        }
    }

    private static BackupSummaryDto Summarize(ParsedBackup parsed, ReplayResult? replay)
    {
        var cash = replay?.CashBalance ?? parsed.CashAccount.Balance;
        var provident = replay?.ProvidentBalance ?? parsed.ProvidentAccount?.Balance;
        var fundValue = replay is null
            ? parsed.FundPositions.Sum(position => position.Value)
            : replay.Funds.Sum(fund => fund.Value ?? 0m);
        var fundPending = replay?.Funds.Sum(fund => fund.PendingAmount) ?? 0m;
        var fundArrival = replay?.Funds.Sum(fund => fund.PendingArrivalAmount) ?? 0m;
        var goldValue = replay?.Gold?.Value
            ?? (parsed.GoldHolding is null ? null : decimal.Round(parsed.GoldHolding.Grams * parsed.GoldHolding.Quote, 2));
        var goldArrival = replay?.Gold?.PendingArrivalAmount ?? 0m;
        var total = replay?.TotalAssets;

        return new BackupSummaryDto(
            parsed.Ledger.Id,
            parsed.Ledger.StartDate,
            decimal.Round(cash, 2),
            provident is null ? null : decimal.Round(provident.Value, 2),
            decimal.Round(fundValue, 2),
            decimal.Round(fundPending, 2),
            decimal.Round(fundArrival, 2),
            goldValue is null ? null : decimal.Round(goldValue.Value, 2),
            decimal.Round(goldArrival, 2),
            total is null ? null : decimal.Round(total.Value, 2),
            replay is not null && replay.Issues.Count == 0,
            parsed.Entries.Count,
            parsed.AssetTransactions.Count,
            parsed.Plans.Count,
            parsed.PlanOccurrences.Count,
            parsed.MutationReceipts.Count,
            parsed.PriceObservations.Count);
    }

    private async Task PersistAsync(ParsedBackup parsed, CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var current = await _db.Ledgers.SingleOrDefaultAsync(cancellationToken);

            if (current is not null)
            {
                await RemoveAsync(_db.PriceObservations, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.AssetTransactions, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.PlanOccurrences, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.AutoPlans, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.StockPositions, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.FundPositions, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.FundGroups, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.GoldHoldings, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.ProvidentFundAccounts, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.CashAdjustments, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.LedgerEntries, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.EntryCategories, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.MutationReceipts, item => item.LedgerId == current.Id, cancellationToken);
                await RemoveAsync(_db.CashAccounts, item => item.LedgerId == current.Id, cancellationToken);
                _db.Ledgers.Remove(current);
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }

            _db.Ledgers.Add(parsed.Ledger);

            if (_db.Entry(parsed.CashAccount).State == EntityState.Detached)
            {
                _db.CashAccounts.Add(parsed.CashAccount);
            }

            if (parsed.ProvidentAccount is not null)
            {
                _db.ProvidentFundAccounts.Add(parsed.ProvidentAccount);
            }

            if (parsed.GoldHolding is not null)
            {
                _db.GoldHoldings.Add(parsed.GoldHolding);
            }

            _db.EntryCategories.AddRange(parsed.Categories);
            _db.FundGroups.AddRange(parsed.FundGroups);
            _db.FundPositions.AddRange(parsed.FundPositions);
            _db.StockPositions.AddRange(parsed.StockPositions);
            await _db.SaveChangesAsync(cancellationToken);

            _db.LedgerEntries.AddRange(parsed.Entries);
            _db.CashAdjustments.AddRange(parsed.CashAdjustments);
            _db.AssetTransactions.AddRange(parsed.AssetTransactions);
            await _db.SaveChangesAsync(cancellationToken);

            _db.PriceObservations.AddRange(parsed.PriceObservations);
            _db.AutoPlans.AddRange(parsed.Plans);
            _db.PlanOccurrences.AddRange(parsed.PlanOccurrences);
            _db.MutationReceipts.AddRange(parsed.MutationReceipts);
            await _db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static async Task RemoveAsync<T>(
        DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken)
        where T : class
    {
        var items = await set.Where(predicate).ToListAsync(cancellationToken);
        set.RemoveRange(items);
    }

    private static string ComputeToken(
        BackupDocument backup,
        BackupSummaryDto? current,
        int currentVersion)
    {
        var payload = JsonSerializer.Serialize(backup, JsonOptions)
            + "|"
            + (current is null ? "none" : JsonSerializer.Serialize(current, JsonOptions))
            + "|"
            + currentVersion.ToString(CultureInfo.InvariantCulture);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static BackupSummaryDto EmptySummary()
    {
        return new BackupSummaryDto(
            Guid.Empty,
            default,
            0m,
            null,
            0m,
            0m,
            0m,
            null,
            0m,
            null,
            false,
            0,
            0,
            0,
            0,
            0,
            0);
    }

    private static void CheckDuplicateIds(
        IEnumerable<Guid> values,
        string label,
        List<string> errors,
        List<string> duplicateRisks)
    {
        var materialized = values.ToList();

        if (materialized.Any(value => value == Guid.Empty))
        {
            var emptyMessage = $"{label}存在空标识。";
            errors.Add(emptyMessage);
            duplicateRisks.Add(emptyMessage);
        }

        var duplicates = materialized
            .Where(value => value != Guid.Empty)
            .GroupBy(value => value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .ToList();

        if (duplicates.Count == 0)
        {
            return;
        }

        var message = $"{label}存在重复标识：{string.Join("、", duplicates.Take(5))}。";
        errors.Add(message);
        duplicateRisks.Add(message);
    }

    private static TDto ToDto<TDto>(object source)
        where TDto : new()
    {
        var dto = new TDto();

        foreach (var property in typeof(TDto).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var sourceProperty = source.GetType().GetProperty(property.Name);

            if (sourceProperty is null || !property.CanWrite)
            {
                continue;
            }

            var value = sourceProperty.GetValue(source);

            if (property.PropertyType == typeof(string) && sourceProperty.PropertyType.IsEnum)
            {
                value = value?.ToString();
            }

            property.SetValue(dto, value);
        }

        return dto;
    }

    private static TEntity ToEntity<TEntity>(object source)
        where TEntity : class
    {
        var entity = (TEntity)Activator.CreateInstance(typeof(TEntity), nonPublic: true)!;

        foreach (var property in typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var sourceProperty = source.GetType().GetProperty(property.Name);

            if (sourceProperty is null || !property.CanWrite)
            {
                continue;
            }

            var raw = sourceProperty.GetValue(source);
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (raw is null)
            {
                if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
                {
                    property.SetValue(entity, null);
                }

                continue;
            }

            if (targetType.IsEnum)
            {
                if (!Enum.TryParse(targetType, raw.ToString(), true, out var parsed))
                {
                    throw new FormatException($"字段 {property.Name} 的值 {raw} 不是有效枚举。");
                }

                property.SetValue(entity, parsed);
                continue;
            }

            if (targetType == typeof(string))
            {
                property.SetValue(entity, raw.ToString());
                continue;
            }

            property.SetValue(entity, raw);
        }

        return entity;
    }

    private static string BuildEntryStatus(LedgerEntry entry)
    {
        if (entry.Status != LedgerEntryStatus.Reversed)
        {
            return "有效";
        }

        return string.IsNullOrWhiteSpace(entry.ReversalReason)
            ? "已撤销"
            : $"已撤销：{entry.ReversalReason}";
    }

    private static string SourceLabel(LedgerEntrySource source)
    {
        return source switch
        {
            LedgerEntrySource.Automatic => "自动",
            LedgerEntrySource.Import => "导入",
            _ => "手动"
        };
    }

    private static string BuildEntryLinks(LedgerEntry entry)
    {
        var links = new List<string> { $"request={entry.RequestId}" };

        if (entry.RelatedCashAdjustmentId is not null)
        {
            links.Add($"adjustment={entry.RelatedCashAdjustmentId}");
        }

        return string.Join("; ", links);
    }

    private static string BuildTransactionLinks(AssetTransaction transaction)
    {
        var links = new List<string> { $"request={transaction.RequestId}" };

        if (transaction.FundPositionId is not null)
        {
            links.Add($"position={transaction.FundPositionId}");
        }

        if (transaction.StockPositionId is not null)
        {
            links.Add($"stockPosition={transaction.StockPositionId}");
        }

        if (transaction.LinkedTransactionId is not null)
        {
            links.Add($"linked={transaction.LinkedTransactionId}");
        }

        if (transaction.RelatedCashAdjustmentId is not null)
        {
            links.Add($"adjustment={transaction.RelatedCashAdjustmentId}");
        }

        return string.Join("; ", links);
    }

    private static string BuildOccurrenceLinks(PlanOccurrence occurrence)
    {
        var links = new List<string>
        {
            $"plan={occurrence.PlanId}",
            $"period={occurrence.PeriodKey}"
        };

        if (occurrence.RelatedRecordId is not null)
        {
            links.Add($"record={occurrence.RelatedRecordId}");
        }

        return string.Join("; ", links);
    }

    private static string TransactionLabel(AssetTransactionKind kind)
    {
        return kind switch
        {
            AssetTransactionKind.ProvidentDeposit => "公积金缴存",
            AssetTransactionKind.ProvidentWithdrawal => "公积金提取",
            AssetTransactionKind.FundOpening => "基金起始持仓",
            AssetTransactionKind.FundBuy => "基金买入",
            AssetTransactionKind.FundBuyPending => "基金买入待确认",
            AssetTransactionKind.FundBuyConfirm => "基金买入确认",
            AssetTransactionKind.FundSell => "基金卖出",
            AssetTransactionKind.FundSellApply => "基金赎回申请",
            AssetTransactionKind.FundSellConfirm => "基金赎回确认",
            AssetTransactionKind.FundSellArrival => "基金赎回到账",
            AssetTransactionKind.FundQuote => "基金净值更新",
            AssetTransactionKind.FundCorrection => "基金持仓更正",
            AssetTransactionKind.GoldBuy => "积存金买入",
            AssetTransactionKind.GoldSell => "积存金卖出",
            AssetTransactionKind.GoldSellPending => "积存金卖出待到账",
            AssetTransactionKind.GoldSellArrival => "积存金到账",
            AssetTransactionKind.GoldQuote => "积存金报价更新",
            AssetTransactionKind.GoldCorrection => "积存金持仓更正",
            AssetTransactionKind.GoldOpening => "积存金起始持仓",
            AssetTransactionKind.ProvidentOpening => "公积金起始余额",
            AssetTransactionKind.StockOpening => "股票起始持仓",
            AssetTransactionKind.StockBuy => "股票买入",
            AssetTransactionKind.StockSell => "股票卖出",
            AssetTransactionKind.StockQuote => "股票行情更新",
            AssetTransactionKind.StockCorrection => "股票持仓更正",
            _ => kind.ToString()
        };
    }

    private static string PlanTypeLabel(AutoPlanType type)
    {
        return type switch
        {
            AutoPlanType.SalaryIncome => "工资到账",
            AutoPlanType.ProvidentDeposit => "公积金缴存",
            AutoPlanType.ProvidentWithdrawal => "公积金提取",
            AutoPlanType.FundSip => "基金定投",
            _ => type.ToString()
        };
    }

    private static string PlanStatusLabel(PlanOccurrenceStatus status)
    {
        return status switch
        {
            PlanOccurrenceStatus.Completed => "已完成",
            PlanOccurrenceStatus.Waiting => "待处理",
            PlanOccurrenceStatus.Failed => "失败",
            PlanOccurrenceStatus.Skipped => "已跳过",
            _ => "待执行"
        };
    }

    private static string FormatAmount(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed class ParsedBackup
    {
        public Ledger Ledger { get; init; } = null!;
        public CashAccount CashAccount { get; init; } = null!;
        public ProvidentFundAccount? ProvidentAccount { get; init; }
        public GoldHolding? GoldHolding { get; init; }
        public IReadOnlyList<EntryCategory> Categories { get; init; } = [];
        public IReadOnlyList<LedgerEntry> Entries { get; init; } = [];
        public IReadOnlyList<CashAdjustment> CashAdjustments { get; init; } = [];
        public IReadOnlyList<FundGroup> FundGroups { get; init; } = [];
        public IReadOnlyList<FundPosition> FundPositions { get; init; } = [];
        public IReadOnlyList<StockPosition> StockPositions { get; init; } = [];
        public IReadOnlyList<AssetTransaction> AssetTransactions { get; init; } = [];
        public IReadOnlyList<PriceObservation> PriceObservations { get; init; } = [];
        public IReadOnlyList<AutoPlan> Plans { get; init; } = [];
        public IReadOnlyList<PlanOccurrence> PlanOccurrences { get; init; } = [];
        public IReadOnlyList<MutationReceipt> MutationReceipts { get; init; } = [];
    }

    private sealed record CsvRow(
        DateOnly Date,
        string Type,
        string Direction,
        decimal Amount,
        decimal? CashEffect,
        decimal? Quantity,
        decimal? Price,
        decimal? Fee,
        string Subject,
        string Status,
        string Source,
        string Links);
}

public sealed record CsvExportResult(
    byte[] Content,
    string FileName,
    int RowCount);
