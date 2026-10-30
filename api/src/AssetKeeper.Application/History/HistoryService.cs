using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Mutations;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.History;

public sealed class HistoryService
{
    // A preview is read-only. A process restart requires a fresh preview.
    private static readonly byte[] PreviewKey = RandomNumberGenerator.GetBytes(32);
    private readonly ILedgerRepository _repository;
    private readonly BusinessDateProvider _dates;

    public HistoryService(
        ILedgerRepository repository,
        BusinessDateProvider dates)
    {
        _repository = repository;
        _dates = dates;
    }

    public async Task<HistoryOverviewDto?> GetAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        var ledger = await _repository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var date = asOf ?? _dates.Today;

        if (date < ledger.StartDate || date > _dates.Today)
        {
            throw new HistoryValidationException("查看日期须在账本起始日期与今天之间。");
        }

        var data = await _repository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var facts = BusinessFact.From(data);
        var cash = ledger.CashAccount;
        var opening = new BusinessFact
        {
            Id = cash.Id,
            Source = "cashOpening",
            Date = cash.OpeningDate ?? ledger.StartDate,
            Amount = cash.OpeningBalance ?? cash.Balance,
            NeedsDateConfirmation = !cash.HistoryConfirmed,
            Version = cash.Version
        };
        var audit = await _repository.GetHistoryAuditAsync(ledger.Id, cancellationToken);
        return new HistoryOverviewDto(ledger.Version, opening, facts.OrderByDescending(fact => fact.Date).ThenByDescending(fact => fact.Sequence).ToList(),
            data.Prices.OrderByDescending(price => price.EffectiveDate).ThenByDescending(price => price.RevisionOrder)
                .Select(price => new HistoryPriceDto(price.Id, price.PositionId, price.AssetType.ToString(), price.Price, price.EffectiveDate,
                    price.Source, price.ObtainedAt, price.IsSuperseded)).ToList(), audit,
            new LedgerReplayCalculator().Calculate(data, facts, date, currentDate: _dates.Today));
    }

    public async Task<AssetOperationResult<HistoryPreviewDto>> PreviewAsync(HistoryChangeRequest request, CancellationToken cancellationToken)
    {
        var ledger = await _repository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<HistoryPreviewDto>.NotFound("账本尚未初始化。");
        }

        if (request.ExpectedLedgerVersion != ledger.Version)
        {
            return AssetOperationResult<HistoryPreviewDto>.Conflict("账本已变化，请刷新记录后重新预览。");
        }

        try
        {
            if (request.Source == "entry" && request.Action == "correct")
            {
                if (request.EntryKind is null)
                {
                    throw new HistoryValidationException("请选择收支方向。");
                }

                var category = await _repository.GetCategoryAsync(
                    ledger.Id,
                    request.EntryKind.Value,
                    request.Category ?? string.Empty,
                    cancellationToken);

                if (category is null)
                {
                    throw new HistoryValidationException("请选择有效分类。");
                }
            }

            var data = await _repository.GetReplayDataAsync(ledger.Id, cancellationToken);
            var before = new LedgerReplayCalculator().Calculate(data, BusinessFact.From(data), _dates.Today, currentDate: _dates.Today);
            var after = Preview(data, request);
            return AssetOperationResult<HistoryPreviewDto>.Success(new(ledger.Version, before, after, Sign(request)));
        }
        catch (Exception exception) when (exception is HistoryValidationException or ArgumentException or InvalidOperationException)
        {
            return AssetOperationResult<HistoryPreviewDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<HistoryPreviewDto>> ChangeAsync(ConfirmHistoryChangeRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_repository, _dates).ExecuteAsync("history.change", request.Change.RequestId, request,
            () => ChangeCoreAsync(request, cancellationToken), result => result.Status == AssetOperationStatus.Success,
            result => result, AssetOperationResult<HistoryPreviewDto>.Conflict,
            message => AssetOperationResult<HistoryPreviewDto>.Invalid(new[] { message }), cancellationToken);
    }

    private async Task<AssetOperationResult<HistoryPreviewDto>> ChangeCoreAsync(ConfirmHistoryChangeRequest request, CancellationToken cancellationToken)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(request.Change)), Encoding.UTF8.GetBytes(request.PreviewToken ?? string.Empty)))
        {
            return AssetOperationResult<HistoryPreviewDto>.Conflict("预览已失效或内容已修改，请重新预览后确认。");
        }

        var preview = await PreviewAsync(request.Change, cancellationToken);

        if (preview.Status != AssetOperationStatus.Success)
        {
            return preview;
        }

        var change = request.Change;
        var ledger = (await _repository.GetForUpdateAsync(cancellationToken))!;
        var data = await _repository.GetReplayDataAsync(ledger.Id, cancellationToken);
        var rebuilder = new LedgerHistoryRebuilder(_repository, _dates);

        try
        {
            if (change.Action == "correct" && change.Source != "cashOpening")
            {
                ledger.ReserveFactSequence(change.Sequence);
            }

            switch (change.Source)
            {
                case "cashOpening":
                    ledger.CashAccount.SetOpening(change.Amount, change.Date);
                    break;
                case "entry":
                    var entry = data.Entries.Single(item => item.Id == change.FactId);

                    if (change.Action == "reverse")
                    {
                        entry.Reverse(change.Reason, _dates.UtcNow);
                    }
                    else
                    {
                        entry.Update(change.EntryKind!.Value, change.Category!, change.Amount, change.Date, _dates.Today, change.Note, _dates.UtcNow);
                        entry.SetHistoryMetadata(change.Sequence, change.RelatedCashAdjustmentId);
                    }

                    break;
                case "adjustment":
                    var adjustment = data.Adjustments.Single(item => item.Id == change.FactId);

                    if (change.Action == "reverse")
                    {
                        adjustment.Reverse();
                    }
                    else
                    {
                        adjustment.Correct(change.Amount, change.Date, change.Note ?? change.Reason);
                        adjustment.SetSequence(change.Sequence);
                    }

                    break;
                case "asset":
                    var transaction = data.Transactions.Single(item => item.Id == change.FactId);

                    foreach (var price in data.Prices.Where(price => price.SourceTransactionId == transaction.Id && !price.IsSuperseded))
                    {
                        price.Supersede();
                    }

                    if (change.Action == "reverse")
                    {
                        transaction.Reverse();
                    }
                    else
                    {
                        var previousBusinessDate = transaction.BusinessDate;
                        transaction.Correct(change.Quantity, change.Amount, change.Price, change.Date, change.PriceDate, change.Sequence, change.Note, change.Fee);
                        SyncFundTradeDateAfterCorrection(transaction, previousBusinessDate);
                        transaction.LinkCashAdjustment(change.RelatedCashAdjustmentId);
                        await rebuilder.AddPriceAsync(ledger, transaction, cancellationToken);
                    }

                    break;
            }

            await rebuilder.RebuildAsync(ledger.Id, change.FactId, cancellationToken, confirmedImpact: true);
            return AssetOperationResult<HistoryPreviewDto>.Success(preview.Result! with { LedgerVersion = ledger.Version + 1 });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return AssetOperationResult<HistoryPreviewDto>.Invalid(new[] { exception.Message });
        }
    }

    private ReplayResult Preview(LedgerReplayData data, HistoryChangeRequest request)
    {
        var facts = BusinessFact.From(data).ToList();
        var cash = data.Ledger.CashAccount;
        var original = request.Source == "cashOpening" ? new BusinessFact { Id = cash.Id, Source = "cashOpening", Version = cash.Version }
            : facts.SingleOrDefault(fact => fact.Id == request.FactId && fact.Source == request.Source);

        if (original is null || original.Id != request.FactId || original.IsReversed)
        {
            throw new HistoryValidationException("记录不存在或已撤销。");
        }

        if (request.RequestId == Guid.Empty || request.ExpectedFactVersion != original.Version)
        {
            throw new HistoryValidationException("请求标识无效或记录已变化，请刷新。");
        }

        if (request.Action is not ("correct" or "reverse") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 200)
        {
            throw new HistoryValidationException("请选择更正或撤销，并填写不超过 200 字的原因。");
        }

        if (request.Source == "cashOpening" && request.Action == "reverse")
        {
            throw new HistoryValidationException("起始现金不能撤销，可更正金额或日期。");
        }

        if (original.AssetType == AssetType.Fund
            && original.Kind != AssetTransactionKind.FundSellConfirm
            && request.Action == "correct"
            && (request.Price != original.Price || request.PriceDate != original.PriceDate))
        {
            throw new HistoryValidationException("基金净值来自行情数据源，不能手动更正；请刷新净值或撤销错误的记录。");
        }

        if (original.AssetType == AssetType.Stock
            && original.Kind == AssetTransactionKind.StockQuote
            && request.Action == "correct"
            && (request.Price != original.Price || request.PriceDate != original.PriceDate))
        {
            throw new HistoryValidationException("股票行情来自行情数据源，不能手动更正；请刷新行情或撤销错误的记录。");
        }

        if (original.Source == "asset" && !original.NeedsDateConfirmation && facts.Any(fact => !fact.IsReversed && fact.NeedsDateConfirmation
            && fact.AssetType == original.AssetType && fact.PositionId == original.PositionId)
            && original.Kind is not (AssetTransactionKind.FundQuote or AssetTransactionKind.GoldQuote
                or AssetTransactionKind.StockQuote))
        {
            throw new HistoryValidationException("请先确认该资产原始持仓记录的日期。");
        }

        ValidateFundBuyChange(original, facts, request);
        ValidateFundSellChange(original, facts, request);
        ValidateGoldSellChange(original, facts, request);

        if (request.Action == "reverse")
        {
            facts[facts.IndexOf(original)] = original with { IsReversed = true };
            return new LedgerReplayCalculator().Calculate(data, facts, _dates.Today, currentDate: _dates.Today);
        }

        var errors = Assets.AssetValidation.ValidateBusinessDate(request.Date, data.Ledger.StartDate, _dates.Today);

        if (request.PriceDate is DateOnly priceDate)
        {
            errors.AddRange(Assets.AssetValidation.ValidateValueDate(priceDate, _dates.Today));
        }

        if (errors.Count > 0)
        {
            throw new HistoryValidationException(string.Join(" ", errors));
        }

        if (request.Source == "cashOpening")
        {
            if (request.Amount < 0 || request.Amount > Domain.Cash.CashAccount.MaximumBalance || decimal.Round(request.Amount, 2) != request.Amount)
            {
                throw new HistoryValidationException("起始现金金额无效。");
            }

            return new LedgerReplayCalculator().Calculate(data, facts, _dates.Today, openingCash: request.Amount, cashOpeningDate: request.Date, currentDate: _dates.Today);
        }

        if (request.Sequence <= 0 || request.Sequence == long.MaxValue || facts.Any(fact => fact.Id != original.Id && fact.Sequence == request.Sequence))
        {
            throw new HistoryValidationException("同日顺序须为未被其他业务占用的正整数。");
        }

        var cashEffect = original.CashEffect;

        if (original.Source == "entry")
        {
            errors = Domain.Ledgers.LedgerEntry.Validate(request.EntryKind ?? 0, request.Category ?? string.Empty, request.Amount,
                request.Date, _dates.Today, request.Note);

            if (errors.Count > 0)
            {
                throw new HistoryValidationException(string.Join(" ", errors));
            }

            cashEffect = request.EntryKind == Domain.Ledgers.LedgerEntryKind.Income ? request.Amount : -request.Amount;
        }
        else if (original.Source == "asset")
        {
            var candidate = AssetTransaction.CreateSnapshot(data.Ledger.Id, original.PositionId, original.AssetType!.Value, original.Kind!.Value,
                request.Quantity, request.Amount, request.Price, request.Date, request.PriceDate, request.Note, request.RequestId, _dates.UtcNow);
            cashEffect = candidate.CashEffect;
        }
        else if (original.Source == "adjustment")
        {
            _ = Domain.Cash.CashAdjustment.Create(data.Ledger.Id, request.Amount == 0 ? 1 : 0, request.Amount,
                request.Date, request.Note ?? request.Reason, _dates.UtcNow);
        }

        var replacement = original with
        {
            Date = request.Date,
            Sequence = request.Sequence,
            Amount = request.Amount,
            Quantity = request.Quantity,
            Price = request.Price,
            PriceDate = request.PriceDate,
            CashEffect = cashEffect,
            EntryKind = request.EntryKind,
            Category = request.Category,
            TargetBalance = original.Source == "adjustment" ? request.Amount : null,
            NeedsDateConfirmation = false,
            RelatedCashAdjustmentId = request.RelatedCashAdjustmentId,
            Note = request.Note,
            Fee = original.Source == "asset" ? request.Fee ?? original.Fee : original.Fee
        };
        facts[facts.IndexOf(original)] = replacement;
        var prices = data.Prices.Where(price => price.SourceTransactionId != original.Id).ToList();

        if (original.Source == "asset" && request.Price is decimal value && request.PriceDate is DateOnly date)
        {
            prices.Add(PriceObservation.Create(data.Ledger.Id, original.AssetType!.Value, original.PositionId, original.Id,
                value, date, _dates.UtcNow, data.Ledger.NextFactSequence + 1));
        }

        return new LedgerReplayCalculator().Calculate(data, facts, _dates.Today, prices, currentDate: _dates.Today);
    }

    private static void ValidateFundBuyChange(BusinessFact original, IReadOnlyList<BusinessFact> facts, HistoryChangeRequest request)    {
        if (original.Source != "asset")
        {
            return;
        }

        var linkedConfirm = facts.FirstOrDefault(fact => !fact.IsReversed
            && fact.Kind == AssetTransactionKind.FundBuyConfirm
            && fact.LinkedTransactionId == original.Id);

        if (original.Kind == AssetTransactionKind.FundBuyPending && linkedConfirm is not null)
        {
            throw new HistoryValidationException("该买入已确认，请先撤销买入确认，再处理待确认记录。");
        }

        if (original.Kind != AssetTransactionKind.FundBuyConfirm)
        {
            return;
        }

        if (request.Action == "correct" && request.Amount != original.Amount)
        {
            throw new HistoryValidationException("买入确认金额由对应的待确认记录决定；如需修改扣款金额，请先撤销确认。");
        }

        if (request.Action == "correct" && request.Fee is not null && (request.Fee < 0 || request.Fee > original.Amount))
        {
            throw new HistoryValidationException("买入确认费用必须在 0 与买入金额之间。");
        }
    }

    private static void ValidateFundSellChange(BusinessFact original, IReadOnlyList<BusinessFact> facts, HistoryChangeRequest request)
    {
        if (original.Source != "asset")
        {
            return;
        }

        var linkedConfirm = facts.FirstOrDefault(fact => !fact.IsReversed
            && fact.Kind == AssetTransactionKind.FundSellConfirm && fact.LinkedTransactionId == original.Id);
        var linkedArrival = facts.FirstOrDefault(fact => !fact.IsReversed
            && fact.Kind == AssetTransactionKind.FundSellArrival && fact.LinkedTransactionId == original.Id);

        if (original.Kind == AssetTransactionKind.FundSellApply && linkedConfirm is not null)
        {
            throw new HistoryValidationException("该赎回申请已确认，请先撤销赎回确认，再处理申请记录。");
        }

        if (original.Kind == AssetTransactionKind.FundSellApply && request.Action == "correct" && request.Amount != 0m)
        {
            throw new HistoryValidationException("赎回申请只记录申请份额，不填写金额。");
        }

        if (original.Kind == AssetTransactionKind.FundSellConfirm && linkedArrival is not null)
        {
            throw new HistoryValidationException("该赎回确认已到账，请先撤销赎回到账，再处理确认记录。");
        }

        if (original.Kind != AssetTransactionKind.FundSellArrival || request.Action != "correct")
        {
            return;
        }

        var confirm = original.LinkedTransactionId is Guid confirmId
            ? facts.FirstOrDefault(fact => fact.Id == confirmId && !fact.IsReversed)
            : null;

        if (confirm is null)
        {
            throw new HistoryValidationException("赎回到账关联的确认记录不存在或已撤销。");
        }

        if (request.Amount != confirm.Amount)
        {
            throw new HistoryValidationException("赎回到账金额必须与待到账金额一致；如有差额，请先更正赎回确认。");
        }
    }

    private static void ValidateGoldSellChange(BusinessFact original, IReadOnlyList<BusinessFact> facts, HistoryChangeRequest request)
    {
        if (original.Source != "asset")
        {
            return;
        }

        var linkedArrival = facts.FirstOrDefault(fact => !fact.IsReversed
            && fact.Kind == AssetTransactionKind.GoldSellArrival && fact.LinkedTransactionId == original.Id);

        if (original.Kind == AssetTransactionKind.GoldSellPending && linkedArrival is not null)
        {
            throw new HistoryValidationException("该积存金卖出已到账，请先撤销到账，再处理卖出记录。");
        }

        if (original.Kind != AssetTransactionKind.GoldSellArrival || request.Action != "correct")
        {
            return;
        }

        var pending = original.LinkedTransactionId is Guid pendingId
            ? facts.FirstOrDefault(fact => fact.Id == pendingId && !fact.IsReversed)
            : null;

        if (pending is null)
        {
            throw new HistoryValidationException("积存金卖到账关联的卖出记录不存在或已撤销。");
        }

        if (request.Amount != pending.Amount)
        {
            throw new HistoryValidationException("积存金卖到账金额必须与待到账金额一致；如有差额，请先更正卖出记录。");
        }
    }

    /// <summary>
    /// 更正下单日期后，买入待确认的 T 日（净值归属日）必须跟着走：
    /// 改为历史日期时直接以该日为 T 日；改为今天时保留 15:00 规则算出的结果（但不能早于新的下单日）。
    /// 否则 T 日会停在旧的位置，自动确认按错误日期的净值折算份额。
    /// </summary>
    private void SyncFundTradeDateAfterCorrection(AssetTransaction transaction, DateOnly previousBusinessDate)
    {
        if (transaction.Kind != AssetTransactionKind.FundBuyPending || transaction.BusinessDate == previousBusinessDate)
        {
            return;
        }

        var businessDate = transaction.BusinessDate;
        var current = transaction.FundTradeDate ?? businessDate;
        var target = businessDate == _dates.Today
            ? (current < businessDate ? businessDate : current)
            : businessDate;

        if (target != current)
        {
            transaction.SyncFundTradeDate(target);
        }
    }

    private static string Sign(HistoryChangeRequest request)
    {
        return Convert.ToHexString(HMACSHA256.HashData(PreviewKey, JsonSerializer.SerializeToUtf8Bytes(request, MutationExecutor.JsonOptions)));
    }
}
