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
        var (sellApplies, sellConfirms) = ValidateFundSellLinks(active);
        var goldSellPendings = ValidateGoldSellLinks(active);
        var fundStates = data.Positions.ToDictionary(position => position.Id, position => new MutableHolding());
        var stockStates = data.Stocks.ToDictionary(position => position.Id, position => new MutableHolding());
        var goldState = data.Gold is null ? null : new MutableHolding();
        var unknownFunds = active.Where(fact => fact.NeedsDateConfirmation && fact.AssetType == AssetType.Fund)
            .Select(fact => fact.PositionId).ToHashSet();
        var unknownStocks = active.Where(fact => fact.NeedsDateConfirmation && fact.AssetType == AssetType.Stock)
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
                ApplyHolding(fundStates[fact.PositionId.Value], fact, facts, pendingBuys, sellApplies, sellConfirms, goldSellPendings, false);
            }

            if (fact.AssetType == AssetType.Stock && fact.PositionId is not null && !unknownStocks.Contains(fact.PositionId))
            {
                ApplyHolding(stockStates[fact.PositionId.Value], fact, facts, pendingBuys, sellApplies, sellConfirms, goldSellPendings, true);
            }

            if (fact.AssetType == AssetType.Gold && goldState is not null && !unknownGold)
            {
                ApplyHolding(goldState, fact, facts, pendingBuys, sellApplies, sellConfirms, goldSellPendings, true);
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
                known ? state.PendingAmount : 0m, known ? state.AppliedShares : 0m, known ? state.PendingArrivalAmount : 0m);
        }).ToList();
        var stocks = data.Stocks.Select(position =>
        {
            var state = stockStates[position.Id];
            var known = !unknownStocks.Contains(position.Id);
            var quote = Latest(observations, AssetType.Stock, position.Id);
            return new HoldingReplay(position.Id, known ? state.Quantity : position.Shares, known ? state.Cost : position.Cost,
                known ? state.Realized : position.RealizedProfit, known ? state.CoverageFrom : null, known,
                (known ? state.CoverageFrom is null : asOfDate != currentDate) ? null : quote?.Price, quote?.EffectiveDate, quote?.Source);
        }).ToList();
        HoldingReplay? gold = null;

        if (data.Gold is not null && goldState is not null)
        {
            var quote = Latest(observations, AssetType.Gold, null);
            gold = new HoldingReplay(null, unknownGold ? data.Gold.Grams : goldState.Quantity, unknownGold ? data.Gold.Cost : goldState.Cost,
                unknownGold ? data.Gold.RealizedProfit : goldState.Realized, unknownGold ? null : goldState.CoverageFrom, !unknownGold,
                (unknownGold ? asOfDate != currentDate : goldState.CoverageFrom is null) ? null : quote?.Price, quote?.EffectiveDate, quote?.Source,
                PendingArrivalAmount: unknownGold ? 0m : goldState.PendingArrivalAmount);
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

        if (stocks.Any(stock => !stock.CanRebuild || stock.CoverageFrom is null || stock.Value is null))
        {
            issues.Add("部分股票持仓日期或行情依据不足");
        }

        if (gold is null || !gold.CanRebuild || gold.CoverageFrom is null || gold.Value is null)
        {
            issues.Add("积存金持仓日期或行情依据不足");
        }

        return new ReplayResult(asOfDate, cashKnown && cashStart <= asOfDate ? cash : null, cashStart, cashDate,
            providentStart is null ? null : provident, providentStart, providentDate, funds, stocks, gold, adjustments, issues);
    }

    private static void ApplyHolding(MutableHolding state, BusinessFact fact, IReadOnlyList<BusinessFact> allFacts,
        IReadOnlyDictionary<Guid, BusinessFact> pendingBuys, IReadOnlyDictionary<Guid, BusinessFact> sellApplies,
        IReadOnlyDictionary<Guid, BusinessFact> sellConfirms, IReadOnlyDictionary<Guid, BusinessFact> goldSellPendings,
        bool canStartWithBuy)
    {
        if (fact.Kind is AssetTransactionKind.FundQuote or AssetTransactionKind.GoldQuote or AssetTransactionKind.StockQuote)
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

        if (fact.Kind == AssetTransactionKind.FundSellApply)
        {
            state.AppliedShares += fact.Quantity ?? 0m;
            Ensure(state.AppliedShares >= 0m && state.AppliedShares <= state.Quantity, fact, "申请赎回份额超过该时点持有份额");
            return;
        }

        if (fact.Kind == AssetTransactionKind.FundSellConfirm)
        {
            var apply = sellApplies[fact.LinkedTransactionId!.Value];
            Ensure(state.AppliedShares >= apply.Quantity, fact, "赎回申请已被确认，不能重复结转");
            state.AppliedShares -= apply.Quantity!.Value;
            var quantity = fact.Quantity ?? 0m;
            Ensure(quantity > 0m && quantity <= state.Quantity, fact, "赎回确认份额超过该时点持有份额");
            var reducedCost = quantity == state.Quantity ? state.Cost : decimal.Round(state.Cost * quantity / state.Quantity, 2);
            state.Quantity -= quantity;
            state.Cost -= reducedCost;
            state.Realized += fact.Amount - reducedCost;
            state.PendingArrivalAmount += fact.Amount;
            Ensure(state.Quantity >= 0m && state.Cost >= 0m && state.PendingArrivalAmount <= AssetTransaction.MaximumAmount,
                fact, "赎回确认后的持仓或待到账金额无效");
            return;
        }

        if (fact.Kind == AssetTransactionKind.FundSellArrival)
        {
            var confirm = sellConfirms[fact.LinkedTransactionId!.Value];
            Ensure(state.PendingArrivalAmount >= confirm.Amount, fact, "赎回到账金额超过待到账金额");
            state.PendingArrivalAmount -= confirm.Amount;
            return;
        }

        if (fact.Kind == AssetTransactionKind.GoldSellPending)
        {
            var quantity = fact.Quantity ?? 0m;
            Ensure(state.CoverageFrom is not null && quantity > 0m && quantity <= state.Quantity,
                fact, "该时点持有克数不足，卖出将超卖");
            var reducedCost = quantity == state.Quantity ? state.Cost : decimal.Round(state.Cost * quantity / state.Quantity, 2);
            state.Quantity -= quantity;
            state.Cost -= reducedCost;
            state.Realized += fact.Amount - reducedCost;
            state.PendingArrivalAmount += fact.Amount;
            Ensure(state.Quantity >= 0m && state.Cost >= 0m && state.PendingArrivalAmount <= AssetTransaction.MaximumAmount,
                fact, "积存金卖出后的持仓或待到账金额无效");
            return;
        }

        if (fact.Kind == AssetTransactionKind.GoldSellArrival)
        {
            var pending = goldSellPendings[fact.LinkedTransactionId!.Value];
            Ensure(state.PendingArrivalAmount >= pending.Amount, fact, "积存金卖到账金额超过待到账金额");
            state.PendingArrivalAmount -= pending.Amount;
            return;
        }

        var opening = fact.Kind is AssetTransactionKind.FundOpening or AssetTransactionKind.GoldOpening
            or AssetTransactionKind.StockOpening;
        var correction = fact.Kind is AssetTransactionKind.FundCorrection or AssetTransactionKind.GoldCorrection
            or AssetTransactionKind.StockCorrection;
        var buy = fact.Kind is AssetTransactionKind.FundBuy or AssetTransactionKind.FundBuyConfirm
            or AssetTransactionKind.GoldBuy or AssetTransactionKind.StockBuy;

        if (opening)
        {
            Ensure(state.CoverageFrom is null, fact, "起始持仓必须早于其他持仓变动，且只能有一笔有效起始记录");
        }

        if (opening || correction)
        {
            state.Quantity = fact.Quantity ?? 0m;
            state.Cost = fact.Amount;
            state.CoverageFrom ??= fact.Date;
            Ensure(state.AppliedShares <= state.Quantity, fact, "起始持仓或核对数量低于已申请赎回份额");
        }
        else if (buy)
        {
            if (state.CoverageFrom is null)
            {
                var hadOpening = allFacts.Any(other => other.AssetType == fact.AssetType && other.PositionId == fact.PositionId
                    && other.Kind is AssetTransactionKind.FundOpening or AssetTransactionKind.GoldOpening
                        or AssetTransactionKind.GoldCorrection or AssetTransactionKind.StockOpening);
                Ensure(canStartWithBuy && !hadOpening, fact, "缺少有效起始持仓，须先处理关联起始记录");
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
            Ensure(state.AppliedShares <= state.Quantity, fact, "卖出份额与已申请赎回份额冲突");
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

    private static (IReadOnlyDictionary<Guid, BusinessFact> Applies, IReadOnlyDictionary<Guid, BusinessFact> Confirms)
        ValidateFundSellLinks(IReadOnlyList<BusinessFact> active)
    {
        var applies = active.Where(fact => fact.Kind == AssetTransactionKind.FundSellApply)
            .ToDictionary(fact => fact.Id);
        var confirms = active.Where(fact => fact.Kind == AssetTransactionKind.FundSellConfirm)
            .ToDictionary(fact => fact.Id);

        foreach (var confirm in confirms.Values)
        {
            if (confirm.LinkedTransactionId is not Guid applyId || !applies.TryGetValue(applyId, out var apply))
            {
                throw new HistoryValidationException("赎回确认关联的申请不存在、已撤销或已确认。");
            }

            if (active.Any(fact => fact.Id != confirm.Id && fact.Kind == AssetTransactionKind.FundSellConfirm
                && fact.LinkedTransactionId == applyId))
            {
                throw new HistoryValidationException("同一笔赎回申请只能确认一次。");
            }

            if (apply.AssetType != AssetType.Fund || apply.PositionId != confirm.PositionId)
            {
                throw new HistoryValidationException("赎回确认与申请记录的基金持仓不一致。");
            }

            if (apply.Quantity is null || apply.Quantity <= 0m || confirm.Quantity is null
                || confirm.Quantity <= 0m || confirm.Quantity > apply.Quantity)
            {
                throw new HistoryValidationException("赎回确认份额必须大于 0 且不超过申请份额。");
            }

            if (confirm.Amount <= 0m)
            {
                throw new HistoryValidationException("赎回确认必须提供大于 0 的净到账金额。");
            }

            if (confirm.Date < apply.Date || (confirm.Date == apply.Date && confirm.Sequence <= apply.Sequence))
            {
                throw new HistoryValidationException("赎回确认必须晚于对应的赎回申请。");
            }
        }

        foreach (var arrival in active.Where(fact => fact.Kind == AssetTransactionKind.FundSellArrival))
        {
            if (arrival.LinkedTransactionId is not Guid confirmId || !confirms.TryGetValue(confirmId, out var confirm))
            {
                throw new HistoryValidationException("赎回到账关联的确认记录不存在、已撤销或已到账。");
            }

            if (active.Any(fact => fact.Id != arrival.Id && fact.Kind == AssetTransactionKind.FundSellArrival
                && fact.LinkedTransactionId == confirmId))
            {
                throw new HistoryValidationException("同一笔赎回确认只能到账一次。");
            }

            if (confirm.AssetType != AssetType.Fund || confirm.PositionId != arrival.PositionId)
            {
                throw new HistoryValidationException("赎回到账与确认记录的基金持仓不一致。");
            }

            if (arrival.Quantity is not null || arrival.Amount <= 0m)
            {
                throw new HistoryValidationException("赎回到账只能记录到账金额。");
            }

            if (arrival.Amount != confirm.Amount)
            {
                throw new HistoryValidationException("赎回到账金额必须与待到账金额一致；如有差额，请先更正赎回确认。");
            }

            if (arrival.Date < confirm.Date || (arrival.Date == confirm.Date && arrival.Sequence <= confirm.Sequence))
            {
                throw new HistoryValidationException("赎回到账必须晚于对应的赎回确认。");
            }
        }

        return (applies, confirms);
    }

    private static IReadOnlyDictionary<Guid, BusinessFact> ValidateGoldSellLinks(IReadOnlyList<BusinessFact> active)
    {
        var pendings = active.Where(fact => fact.Kind == AssetTransactionKind.GoldSellPending)
            .ToDictionary(fact => fact.Id);

        foreach (var pending in pendings.Values)
        {
            if (pending.Quantity is null || pending.Quantity <= 0m || pending.Amount <= 0m)
            {
                throw new HistoryValidationException("积存金卖出待到账必须包含克数和净回款金额。");
            }
        }

        foreach (var arrival in active.Where(fact => fact.Kind == AssetTransactionKind.GoldSellArrival))
        {
            if (arrival.LinkedTransactionId is not Guid pendingId || !pendings.TryGetValue(pendingId, out var pending))
            {
                throw new HistoryValidationException("积存金卖到账关联的记录不存在、已撤销或已到账。");
            }

            if (active.Any(fact => fact.Id != arrival.Id && fact.Kind == AssetTransactionKind.GoldSellArrival
                && fact.LinkedTransactionId == pendingId))
            {
                throw new HistoryValidationException("同一笔积存金卖出只能到账一次。");
            }

            if (pending.AssetType != AssetType.Gold || arrival.AssetType != AssetType.Gold)
            {
                throw new HistoryValidationException("积存金卖到账与卖出记录的资产类型不一致。");
            }

            if (arrival.Quantity is not null || arrival.Amount <= 0m)
            {
                throw new HistoryValidationException("积存金卖到账只能记录到账金额。");
            }

            if (arrival.Amount != pending.Amount)
            {
                throw new HistoryValidationException("积存金卖到账金额必须与待到账金额一致；如有差额，请先更正卖出记录。");
            }

            if (arrival.Date < pending.Date || (arrival.Date == pending.Date && arrival.Sequence <= pending.Sequence))
            {
                throw new HistoryValidationException("积存金卖到账必须晚于对应的卖出记录。");
            }
        }

        return pendings;
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
        return prices.Where(price => price.AssetType == assetType && price.PositionId == positionId)
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

        public decimal AppliedShares { get; set; }

        public decimal PendingArrivalAmount { get; set; }

        public DateOnly? CoverageFrom { get; set; }
    }
}
