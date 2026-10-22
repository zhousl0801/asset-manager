using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;

namespace AssetKeeper.Application.History;

// Pure replay: it never mutates tracked entities or writes to a database.
public sealed class LedgerReplayCalculator
{
    public ReplayResult Calculate(LedgerReplayData data, IReadOnlyList<BusinessFact> facts, DateOnly asOfDate,
        IReadOnlyList<PriceObservation>? priceOverride = null, decimal? openingCash = null, DateOnly? cashOpeningDate = null, DateOnly? currentDate = null)
    {
        var cashAccount = data.Ledger.CashAccount;
        var cashStart = cashOpeningDate ?? cashAccount.OpeningDate;
        var cashKnown = (openingCash is not null || cashAccount.HistoryConfirmed) && cashStart is not null && (openingCash ?? cashAccount.OpeningBalance) is not null;
        var cash = openingCash ?? cashAccount.OpeningBalance ?? 0m;
        var cashDate = cashStart ?? data.Ledger.StartDate;
        var provident = 0m;
        DateOnly? providentStart = null;
        var providentDate = data.Ledger.StartDate;
        var adjustments = new List<AdjustmentReplay>();
        var active = facts.Where(fact => !fact.IsReversed).ToList();
        if (active.Where(fact => fact.Sequence > 0).GroupBy(fact => fact.Sequence).Any(group => group.Count() > 1))
        {
            throw new HistoryValidationException("业务顺序重复，请核对同日业务顺序后重试。");
        }

        var allAnchors = facts.Where(fact => fact.Source == "adjustment").ToDictionary(fact => fact.Id);
        ValidateLinks(active, allAnchors);
        var pendingBuys = ValidateFundBuyLinks(active);
        var fundStates = data.Positions.ToDictionary(position => position.Id, position => new MutableHolding());
        var goldState = data.Gold is null ? null : new MutableHolding();
        var unknownFunds = active.Where(fact => fact.NeedsDateConfirmation && fact.AssetType == AssetType.Fund)
            .Select(fact => fact.PositionId).ToHashSet();
        var unknownGold = active.Any(fact => fact.NeedsDateConfirmation && fact.AssetType == AssetType.Gold);

        foreach (var fact in active.Where(fact => fact.Date <= asOfDate)
            .OrderBy(fact => fact.Date).ThenBy(fact => EffectiveSequence(fact, allAnchors)).ThenBy(fact => fact.RelatedCashAdjustmentId is null ? 1 : 0).ThenBy(fact => fact.Sequence))
        {
            if (fact.Source == "adjustment")
            {
                if (!cashKnown)
                {
                    continue;
                }

                RequireCashCoverage(cashKnown, cashStart, fact);
                var previous = cash;
                cash = fact.TargetBalance!.Value;
                adjustments.Add(new AdjustmentReplay(fact.Id, previous, cash - previous));
                cashDate = fact.Date;
                continue;
            }

            if (fact.CashEffect != 0m)
            {
                if (!cashKnown)
                {
                    continue;
                }

                RequireCashCoverage(cashKnown, cashStart, fact);
                cash += fact.CashEffect;
                cashDate = fact.Date;
                Ensure(cash >= 0m && cash <= CashAccount.MaximumBalance, fact, "现金余额不足或超出范围");
            }

            if (fact.AssetType == AssetType.ProvidentFund)
            {
                if (fact.Kind == AssetTransactionKind.ProvidentOpening)
                {
                    Ensure(providentStart is null, fact, "起始公积金必须早于其他缴存和提取，且只能有一笔有效起始记录");
                    provident = fact.Amount;
                }
                else
                {
                    provident += fact.Kind == AssetTransactionKind.ProvidentDeposit ? fact.Amount : -fact.Amount;
                }

                providentStart ??= fact.Date;
                providentDate = fact.Date;
                Ensure(provident >= 0m && provident <= ProvidentFundAccount.MaximumBalance, fact, "公积金余额不足或超出范围");
            }

            if (fact.AssetType == AssetType.Fund && fact.PositionId is not null && !unknownFunds.Contains(fact.PositionId))
            {
                ApplyHolding(fundStates[fact.PositionId.Value], fact, facts, pendingBuys, false);
            }

            if (fact.AssetType == AssetType.Gold && goldState is not null && !unknownGold)
            {
                ApplyHolding(goldState, fact, facts, pendingBuys, true);
            }
        }

        var observations = (priceOverride ?? data.Prices).Where(price => !price.IsSuperseded && price.EffectiveDate <= asOfDate
            && (price.SourceTransactionId is null || active.Any(fact => fact.Id == price.SourceTransactionId))).ToList();
        var funds = data.Positions.Select(position =>
        {
            var state = fundStates[position.Id];
            var known = !unknownFunds.Contains(position.Id);
            var quote = Latest(observations, AssetType.Fund, position.Id);
            return new HoldingReplay(position.Id, known ? state.Quantity : position.Shares, known ? state.Cost : position.Cost,
                known ? state.Realized : position.RealizedProfit, known ? state.CoverageFrom : null, known,
                (known ? state.CoverageFrom is null : asOfDate != currentDate) ? null : quote?.Price, quote?.EffectiveDate, quote?.Source,
                known ? state.PendingAmount : 0m);
        }).ToList();
        HoldingReplay? gold = null;

        if (data.Gold is not null && goldState is not null)
        {
            var quote = Latest(observations, AssetType.Gold, null);
            gold = new HoldingReplay(null, unknownGold ? data.Gold.Grams : goldState.Quantity, unknownGold ? data.Gold.Cost : goldState.Cost,
                unknownGold ? data.Gold.RealizedProfit : goldState.Realized, unknownGold ? null : goldState.CoverageFrom, !unknownGold,
                (unknownGold ? asOfDate != currentDate : goldState.CoverageFrom is null) ? null : quote?.Price, quote?.EffectiveDate, quote?.Source);
        }

        var issues = new List<string>();

        if (!cashKnown || cashStart > asOfDate)
        {
            issues.Add("现金起点尚未覆盖该日期");
        }

        if (providentStart is null)
        {
            issues.Add("公积金尚无该日期的起始依据");
        }

        if (funds.Count == 0 || funds.Any(fund => !fund.CanRebuild || fund.CoverageFrom is null || fund.Value is null))
        {
            issues.Add("部分基金持仓日期或行情依据不足");
        }

        if (gold is null || !gold.CanRebuild || gold.CoverageFrom is null || gold.Value is null)
        {
            issues.Add("积存金持仓日期或行情依据不足");
        }

        return new ReplayResult(asOfDate, cashKnown && cashStart <= asOfDate ? cash : null, cashStart, cashDate,
            providentStart is null ? null : provident, providentStart, providentDate, funds, gold, adjustments, issues);
    }

    private static void ApplyHolding(MutableHolding state, BusinessFact fact, IReadOnlyList<BusinessFact> allFacts,
        IReadOnlyDictionary<Guid, BusinessFact> pendingBuys, bool gold)
    {
        if (fact.Kind is AssetTransactionKind.FundQuote or AssetTransactionKind.GoldQuote)
        {
            return;
        }

        if (fact.Kind == AssetTransactionKind.FundBuyPending)
        {
            state.PendingAmount += fact.Amount;
            Ensure(state.PendingAmount >= 0m && state.PendingAmount <= AssetTransaction.MaximumAmount, fact, "待确认买入金额无效");
            return;
        }

        if (fact.Kind == AssetTransactionKind.FundBuyConfirm)
        {
            var pending = pendingBuys[fact.LinkedTransactionId!.Value];
            Ensure(state.PendingAmount >= pending.Amount, fact, "待确认买入已被确认，不能重复结转");
            state.PendingAmount -= pending.Amount;
        }

        var opening = fact.Kind is AssetTransactionKind.FundOpening or AssetTransactionKind.GoldOpening;
        var correction = fact.Kind is AssetTransactionKind.FundCorrection or AssetTransactionKind.GoldCorrection;
        var buy = fact.Kind is AssetTransactionKind.FundBuy or AssetTransactionKind.FundBuyConfirm or AssetTransactionKind.GoldBuy;

        if (opening)
        {
            Ensure(state.CoverageFrom is null, fact, "起始持仓必须早于其他持仓变动，且只能有一笔有效起始记录");
        }

        if (opening || correction)
        {
            state.Quantity = fact.Quantity ?? 0m;
            state.Cost = fact.Amount;
            state.CoverageFrom ??= fact.Date;
        }
        else if (buy)
        {
            if (state.CoverageFrom is null)
            {
                var hadOpening = allFacts.Any(other => other.AssetType == fact.AssetType && other.PositionId == fact.PositionId
                    && other.Kind is AssetTransactionKind.FundOpening or AssetTransactionKind.GoldOpening or AssetTransactionKind.GoldCorrection);
                Ensure(gold && !hadOpening, fact, "缺少有效起始持仓，须先处理关联起始记录");
                state.CoverageFrom = fact.Date;
            }

            state.Quantity += fact.Quantity!.Value;
            state.Cost += fact.Amount;
        }
        else
        {
            var quantity = fact.Quantity ?? 0m;
            Ensure(state.CoverageFrom is not null && quantity > 0m && quantity <= state.Quantity, fact, "该时点持有数量不足，卖出将超卖");
            var reducedCost = quantity == state.Quantity ? state.Cost : decimal.Round(state.Cost * quantity / state.Quantity, 2);
            state.Quantity -= quantity;
            state.Cost -= reducedCost;
            state.Realized += fact.Amount - reducedCost;
        }

        Ensure(state.Quantity >= 0m && state.Quantity <= AssetTransaction.MaximumQuantity
            && state.Cost >= 0m && state.Cost <= AssetTransaction.MaximumAmount && (state.Quantity != 0m || state.Cost == 0m), fact, "持仓数量或成本无效");
    }

    private static IReadOnlyDictionary<Guid, BusinessFact> ValidateFundBuyLinks(IReadOnlyList<BusinessFact> active)
    {
        var pendingBuys = active.Where(fact => fact.Kind == AssetTransactionKind.FundBuyPending)
            .ToDictionary(fact => fact.Id);

        foreach (var confirm in active.Where(fact => fact.Kind == AssetTransactionKind.FundBuyConfirm))
        {
            if (confirm.LinkedTransactionId is not Guid pendingId || !pendingBuys.TryGetValue(pendingId, out var pending))
            {
                throw new HistoryValidationException("买入确认关联的待确认记录不存在、已撤销或已结转。");
            }

            if (active.Any(fact => fact.Id != confirm.Id && fact.Kind == AssetTransactionKind.FundBuyConfirm
                && fact.LinkedTransactionId == pendingId))
            {
                throw new HistoryValidationException("同一笔买入待确认只能确认一次。");
            }

            if (pending.AssetType != AssetType.Fund || pending.PositionId != confirm.PositionId)
            {
                throw new HistoryValidationException("买入确认与待确认记录的基金持仓不一致。");
            }

            if (pending.Quantity is not null || pending.Price is not null)
            {
                throw new HistoryValidationException("买入待确认记录不能包含份额或成交净值。");
            }

            if (confirm.Amount != pending.Amount)
            {
                throw new HistoryValidationException("买入确认金额必须与待确认金额一致；如需修改扣款金额，请先撤销确认。");
            }

            if (confirm.Date < pending.Date || (confirm.Date == pending.Date && confirm.Sequence <= pending.Sequence))
            {
                throw new HistoryValidationException("买入确认必须晚于对应的待确认记录。");
            }
        }

        return pendingBuys;
    }

    private static void ValidateLinks(IReadOnlyList<BusinessFact> active, IReadOnlyDictionary<Guid, BusinessFact> anchors)
    {
        foreach (var group in active.Where(fact => fact.RelatedCashAdjustmentId is not null).GroupBy(fact => fact.RelatedCashAdjustmentId!.Value))
        {
            if (!anchors.TryGetValue(group.Key, out var anchor))
            {
                throw new HistoryValidationException("关联的现金核对记录不存在。");
            }

            var effect = group.Sum(fact => fact.CashEffect);

            if (group.Any(fact => fact.Date > anchor.Date || fact.CashEffect == 0m || Math.Sign(fact.CashEffect) != Math.Sign(anchor.RecordedDifference ?? 0m))
                || Math.Abs(effect) > Math.Abs(anchor.RecordedDifference ?? 0m))
            {
                throw new HistoryValidationException($"关联补录必须发生在核对之前、方向一致且不超过原差额；核对记录 {anchor.Id}。");
            }
        }
    }

    private static long EffectiveSequence(BusinessFact fact, IReadOnlyDictionary<Guid, BusinessFact> anchors)
    {
        return fact.RelatedCashAdjustmentId is Guid id && anchors.TryGetValue(id, out var anchor)
            && !anchor.IsReversed && fact.Date == anchor.Date ? anchor.Sequence : fact.Sequence;
    }

    private static PriceObservation? Latest(IEnumerable<PriceObservation> prices, AssetType assetType, Guid? positionId)
    {
        return prices.Where(price => price.AssetType == assetType && price.FundPositionId == positionId)
            .OrderByDescending(price => price.EffectiveDate).ThenByDescending(price => price.RevisionOrder).FirstOrDefault();
    }

    private static void RequireCashCoverage(bool known, DateOnly? openingDate, BusinessFact fact)
    {
        Ensure(known && openingDate <= fact.Date, fact, "现金起始日期或金额待核对，无法验证该时点现金");
    }

    private static void Ensure(bool valid, BusinessFact fact, string message)
    {
        if (!valid)
        {
            throw new HistoryValidationException($"{fact.Date:yyyy-MM-dd}：{message}（关联记录 {fact.Id}）。");
        }
    }

    private sealed class MutableHolding
    {
        public decimal Quantity { get; set; }

        public decimal Cost { get; set; }

        public decimal Realized { get; set; }

        public decimal PendingAmount { get; set; }

        public DateOnly? CoverageFrom { get; set; }
    }
}
