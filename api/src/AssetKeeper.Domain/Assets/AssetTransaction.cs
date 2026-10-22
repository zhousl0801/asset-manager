using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Assets;

public sealed class AssetTransaction
{
    public const decimal MaximumAmount = 1_000_000_000m;
    public const decimal MaximumQuantity = 1_000_000m;
    public const decimal MaximumPrice = 1_000_000_000m;
    public const int MaximumSubjectLength = 100;
    public const int MaximumNoteLength = 200;
    public const int MaximumQuoteSourceLength = 40;

    private AssetTransaction()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public Guid RequestId { get; private set; }

    public AssetType AssetType { get; private set; }

    public AssetTransactionKind Kind { get; private set; }

    public Guid? FundPositionId { get; private set; }

    public Guid? LinkedTransactionId { get; private set; }

    public decimal? Quantity { get; private set; }

    public decimal Amount { get; private set; }

    public decimal CashEffect { get; private set; }

    public decimal Fee { get; private set; }

    public decimal? Price { get; private set; }

    public string? QuoteSource { get; private set; }

    public string? Subject { get; private set; }

    public string? Note { get; private set; }

    public DateOnly BusinessDate { get; private set; }

    public Guid? RelatedCashAdjustmentId { get; private set; }

    public void LinkCashAdjustment(Guid? id) => RelatedCashAdjustmentId = id;

    public DateOnly? PriceDate { get; private set; }

    public long BusinessSequence { get; private set; }

    public bool NeedsDateConfirmation { get; private set; }

    public bool IsReversed { get; private set; }

    public int Version { get; private set; }

    public void SetHistoryMetadata(DateOnly businessDate, DateOnly? priceDate, long sequence)
    {
        if (businessDate == default || priceDate == default(DateOnly))
        {
            throw new ArgumentException("业务日期或行情日期无效。");
        }

        BusinessDate = businessDate;
        PriceDate = priceDate;
        BusinessSequence = sequence;
        NeedsDateConfirmation = false;
    }

    public void Correct(decimal? quantity, decimal amount, decimal? price, DateOnly date, DateOnly? priceDate, long sequence, string? note, decimal? fee = null)
    {
        if (IsReversed)
        {
            throw new InvalidOperationException("已撤销记录不能更正。");
        }

        var opening = Kind is AssetTransactionKind.FundOpening or AssetTransactionKind.FundCorrection
            or AssetTransactionKind.GoldOpening or AssetTransactionKind.GoldCorrection or AssetTransactionKind.ProvidentOpening;
        var quote = Kind is AssetTransactionKind.FundQuote or AssetTransactionKind.GoldQuote;
        var pendingBuy = Kind == AssetTransactionKind.FundBuyPending;
        var confirmedBuy = Kind == AssetTransactionKind.FundBuyConfirm;
        var sellApply = Kind == AssetTransactionKind.FundSellApply;
        var sellConfirm = Kind == AssetTransactionKind.FundSellConfirm;
        var sellArrival = Kind == AssetTransactionKind.FundSellArrival;
        var goldSellPending = Kind == AssetTransactionKind.GoldSellPending;
        var goldSellArrival = Kind == AssetTransactionKind.GoldSellArrival;
        EnsureAmount(amount, nameof(amount));

        if (amount < 0 || (!opening && !quote && !sellApply && amount == 0))
        {
            throw new ArgumentException("交易金额必须大于 0；起始余额、赎回申请或核对成本允许为 0。");
        }

        if (AssetType != AssetType.ProvidentFund && !quote)
        {
            if (pendingBuy || sellArrival || goldSellArrival)
            {
                if (quantity is not null)
                {
                    throw new ArgumentException(pendingBuy
                        ? "买入待确认不包含份额，确认时再录入实际份额。"
                        : "到账记录不包含份额或克数，卖出确认时已记录实际数量。");
                }
            }
            else if (quantity is null || quantity < 0 || quantity > MaximumQuantity || decimal.Round(quantity.Value, 4) != quantity.Value
                || (!opening && quantity == 0) || (quantity == 0 && amount != 0))
            {
                throw new ArgumentException("份额、克数或对应成本无效。");
            }
        }

        if ((pendingBuy || sellApply || sellArrival || goldSellPending || goldSellArrival)
            && (price is not null || priceDate is not null))
        {
            throw new ArgumentException("该记录不包含成交净值，净值在确认时填写。");
        }

        if ((confirmedBuy || sellConfirm) && (price is null || priceDate is null))
        {
            throw new ArgumentException("确认记录必须提供成交净值和净值日期。");
        }

        if (price is not null && (price <= 0 || price > MaximumPrice || decimal.Round(price.Value, 4) != price.Value))
        {
            throw new ArgumentException("价格必须大于 0，最多四位小数。");
        }

        if (price is not null && priceDate is null)
        {
            throw new ArgumentException("有价格时必须提供独立的行情有效日期。");
        }

        if (quote && (amount != 0 || quantity is not null || price is null || date != priceDate))
        {
            throw new ArgumentException("行情记录只更新价格与有效日期。");
        }

        if (fee is not null && fee != 0m)
        {
            if (!confirmedBuy && !sellConfirm)
            {
                throw new ArgumentException("只有确认记录可以记录费用。");
            }

            EnsureFee(fee.Value, amount);
            Fee = fee.Value;
        }
        else if ((confirmedBuy || sellConfirm) && fee is not null)
        {
            Fee = 0m;
        }
        else if (pendingBuy || sellApply || sellArrival || goldSellPending || goldSellArrival)
        {
            Fee = 0m;
        }

        if (confirmedBuy || sellConfirm)
        {
            EnsureFee(Fee, amount);
        }

        if (note?.Trim().Length > MaximumNoteLength)
        {
            throw new ArgumentException("备注不能超过 200 个字符。");
        }

        Quantity = AssetType == AssetType.ProvidentFund ? null : quantity;
        Amount = amount;
        Price = price;
        CashEffect = Kind switch
        {
            AssetTransactionKind.FundBuy or AssetTransactionKind.FundBuyPending or AssetTransactionKind.GoldBuy => -amount,
            AssetTransactionKind.FundSell or AssetTransactionKind.FundSellArrival or AssetTransactionKind.GoldSell
                or AssetTransactionKind.GoldSellArrival or AssetTransactionKind.ProvidentWithdrawal => amount,
            _ => 0m
        };
        Note = Normalize(note);
        SetHistoryMetadata(date, priceDate, sequence);
        Version++;
    }

    public void Reverse()
    {
        if (IsReversed)
        {
            throw new InvalidOperationException("该记录已撤销。");
        }

        IsReversed = true;
        Version++;
    }

    public static AssetTransaction CreateSnapshot(Guid ledgerId, Guid? positionId, AssetType assetType, AssetTransactionKind kind,
        decimal? quantity, decimal cost, decimal? price, DateOnly date, DateOnly? priceDate, string? note, Guid requestId, DateTimeOffset now)
    {
        var transaction = Create(ledgerId, requestId, assetType, kind, positionId, quantity, cost, 0m, price, "起始资产 / 持仓核对", note, date, now);
        transaction.Correct(quantity, cost, price, date, priceDate, 0, note);
        return transaction;
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public static AssetTransaction CreateProvidentDeposit(
        Guid ledgerId,
        decimal amount,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveAmount(amount, nameof(amount));

        return Create(
            ledgerId,
            requestId,
            AssetType.ProvidentFund,
            AssetTransactionKind.ProvidentDeposit,
            null,
            null,
            amount,
            0m,
            null,
            "公积金",
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateProvidentWithdrawal(
        Guid ledgerId,
        decimal amount,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveAmount(amount, nameof(amount));

        return Create(
            ledgerId,
            requestId,
            AssetType.ProvidentFund,
            AssetTransactionKind.ProvidentWithdrawal,
            null,
            null,
            amount,
            amount,
            null,
            "公积金",
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateFundOpening(
        Guid ledgerId,
        Guid fundPositionId,
        decimal shares,
        decimal cost,
        decimal netValue,
        string subject,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        if (shares == 0 && cost != 0)
        {
            throw new ArgumentException("份额为 0 时，持仓成本也应为 0。", nameof(cost));
        }

        if (cost < 0)
        {
            throw new ArgumentException("持仓成本不能为负数。", nameof(cost));
        }

        return Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundOpening,
            RequirePositionId(fundPositionId),
            shares,
            cost,
            0m,
            netValue,
            subject,
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateFundBuyPending(
        Guid ledgerId,
        Guid fundPositionId,
        decimal amount,
        string subject,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveAmount(amount, nameof(amount));

        return Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundBuyPending,
            RequirePositionId(fundPositionId),
            null,
            amount,
            -amount,
            null,
            subject,
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateFundBuyConfirm(
        Guid ledgerId,
        Guid fundPositionId,
        Guid pendingTransactionId,
        decimal shares,
        decimal amount,
        decimal netValue,
        decimal fee,
        string subject,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveQuantity(shares, nameof(shares));
        EnsurePositiveAmount(amount, nameof(amount));
        EnsureFee(fee, amount);

        if (pendingTransactionId == Guid.Empty)
        {
            throw new ArgumentException("买入待确认标识无效。", nameof(pendingTransactionId));
        }

        var transaction = Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundBuyConfirm,
            RequirePositionId(fundPositionId),
            shares,
            amount,
            0m,
            netValue,
            subject,
            note,
            businessDate,
            now,
            fee);
        transaction.LinkedTransactionId = pendingTransactionId;

        return transaction;
    }

    public static AssetTransaction CreateFundSellApply(
        Guid ledgerId,
        Guid fundPositionId,
        decimal shares,
        string subject,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveQuantity(shares, nameof(shares));

        return Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundSellApply,
            RequirePositionId(fundPositionId),
            shares,
            0m,
            0m,
            null,
            subject,
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateFundSellConfirm(
        Guid ledgerId,
        Guid fundPositionId,
        Guid applyTransactionId,
        decimal shares,
        decimal netValue,
        decimal netAmount,
        decimal fee,
        string subject,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveQuantity(shares, nameof(shares));
        EnsurePositiveAmount(netAmount, nameof(netAmount));
        EnsureFee(fee, netAmount);

        if (applyTransactionId == Guid.Empty)
        {
            throw new ArgumentException("赎回申请标识无效。", nameof(applyTransactionId));
        }

        var transaction = Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundSellConfirm,
            RequirePositionId(fundPositionId),
            shares,
            netAmount,
            0m,
            netValue,
            subject,
            note,
            businessDate,
            now,
            fee);
        transaction.LinkedTransactionId = applyTransactionId;

        return transaction;
    }

    public static AssetTransaction CreateFundSellArrival(
        Guid ledgerId,
        Guid fundPositionId,
        Guid confirmTransactionId,
        decimal amount,
        string subject,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveAmount(amount, nameof(amount));

        if (confirmTransactionId == Guid.Empty)
        {
            throw new ArgumentException("赎回确认标识无效。", nameof(confirmTransactionId));
        }

        var transaction = Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundSellArrival,
            RequirePositionId(fundPositionId),
            null,
            amount,
            amount,
            null,
            subject,
            note,
            businessDate,
            now);
        transaction.LinkedTransactionId = confirmTransactionId;

        return transaction;
    }

    public static AssetTransaction CreateFundQuote(
        Guid ledgerId,
        Guid fundPositionId,
        decimal netValue,
        string subject,
        DateOnly netValueDate,
        Guid requestId,
        DateTimeOffset now,
        string? source = null)
    {
        var transaction = Create(
            ledgerId,
            requestId,
            AssetType.Fund,
            AssetTransactionKind.FundQuote,
            RequirePositionId(fundPositionId),
            null,
            0m,
            0m,
            netValue,
            subject,
            null,
            netValueDate,
            now);
        transaction.QuoteSource = NormalizeQuoteSource(source);

        return transaction;
    }

    public static AssetTransaction CreateGoldBuy(
        Guid ledgerId,
        decimal grams,
        decimal amount,
        decimal quote,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveQuantity(grams, nameof(grams));
        EnsurePositiveAmount(amount, nameof(amount));

        return Create(
            ledgerId,
            requestId,
            AssetType.Gold,
            AssetTransactionKind.GoldBuy,
            null,
            grams,
            amount,
            -amount,
            quote,
            "积存金",
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateGoldSell(
        Guid ledgerId,
        decimal grams,
        decimal netAmount,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveQuantity(grams, nameof(grams));
        EnsurePositiveAmount(netAmount, nameof(netAmount));

        return Create(
            ledgerId,
            requestId,
            AssetType.Gold,
            AssetTransactionKind.GoldSell,
            null,
            grams,
            netAmount,
            netAmount,
            null,
            "积存金",
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateGoldSellPending(
        Guid ledgerId,
        decimal grams,
        decimal netAmount,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveQuantity(grams, nameof(grams));
        EnsurePositiveAmount(netAmount, nameof(netAmount));

        return Create(
            ledgerId,
            requestId,
            AssetType.Gold,
            AssetTransactionKind.GoldSellPending,
            null,
            grams,
            netAmount,
            0m,
            null,
            "积存金",
            note,
            businessDate,
            now);
    }

    public static AssetTransaction CreateGoldSellArrival(
        Guid ledgerId,
        Guid pendingTransactionId,
        decimal amount,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        EnsurePositiveAmount(amount, nameof(amount));

        if (pendingTransactionId == Guid.Empty)
        {
            throw new ArgumentException("积存金卖出待到账标识无效。", nameof(pendingTransactionId));
        }

        var transaction = Create(
            ledgerId,
            requestId,
            AssetType.Gold,
            AssetTransactionKind.GoldSellArrival,
            null,
            null,
            amount,
            amount,
            null,
            "积存金",
            note,
            businessDate,
            now);
        transaction.LinkedTransactionId = pendingTransactionId;

        return transaction;
    }

    public static AssetTransaction CreateGoldQuote(
        Guid ledgerId,
        decimal quote,
        DateOnly quoteDate,
        Guid requestId,
        DateTimeOffset now,
        string? source = null)
    {
        var transaction = Create(
            ledgerId,
            requestId,
            AssetType.Gold,
            AssetTransactionKind.GoldQuote,
            null,
            null,
            0m,
            0m,
            quote,
            "积存金",
            null,
            quoteDate,
            now);
        transaction.QuoteSource = NormalizeQuoteSource(source);

        return transaction;
    }

    public static AssetTransaction CreateGoldCorrection(
        Guid ledgerId,
        decimal grams,
        decimal cost,
        decimal quote,
        DateOnly businessDate,
        string? note,
        Guid requestId,
        DateTimeOffset now)
    {
        if (grams == 0 && cost != 0)
        {
            throw new ArgumentException("持有克数为 0 时，持仓成本也应为 0。", nameof(cost));
        }

        if (cost < 0)
        {
            throw new ArgumentException("持仓成本不能为负数。", nameof(cost));
        }

        return Create(
            ledgerId,
            requestId,
            AssetType.Gold,
            AssetTransactionKind.GoldCorrection,
            null,
            grams,
            cost,
            0m,
            quote,
            "积存金",
            note,
            businessDate,
            now);
    }

    private static AssetTransaction Create(
        Guid ledgerId,
        Guid requestId,
        AssetType assetType,
        AssetTransactionKind kind,
        Guid? fundPositionId,
        decimal? quantity,
        decimal amount,
        decimal cashEffect,
        decimal? price,
        string? subject,
        string? note,
        DateOnly businessDate,
        DateTimeOffset now,
        decimal fee = 0m)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("请求标识无效。", nameof(requestId));
        }

        if (businessDate == default)
        {
            throw new ArgumentException("业务日期无效。", nameof(businessDate));
        }

        EnsureAmount(amount, nameof(amount));

        if (cashEffect != 0m)
        {
            EnsureAmount(cashEffect, nameof(cashEffect));
        }

        if (quantity is not null)
        {
            if (quantity < 0)
            {
                throw new ArgumentException("数量不能为负数。", nameof(quantity));
            }

            if (decimal.Round(quantity.Value, 4) != quantity.Value)
            {
                throw new ArgumentException("数量最多保留四位小数。", nameof(quantity));
            }

            if (quantity > MaximumQuantity)
            {
                throw new ArgumentException("数量超出允许范围。", nameof(quantity));
            }
        }

        if (price is not null)
        {
            if (price <= 0)
            {
                throw new ArgumentException("价格必须大于 0。", nameof(price));
            }

            if (decimal.Round(price.Value, 4) != price.Value)
            {
                throw new ArgumentException("价格最多保留四位小数。", nameof(price));
            }

            if (price > MaximumPrice)
            {
                throw new ArgumentException("价格超出允许范围。", nameof(price));
            }
        }

        if (!string.IsNullOrWhiteSpace(subject) && subject.Trim().Length > MaximumSubjectLength)
        {
            throw new ArgumentException($"摘要不能超过 {MaximumSubjectLength} 个字符。", nameof(subject));
        }

        if (!string.IsNullOrWhiteSpace(note) && note.Trim().Length > MaximumNoteLength)
        {
            throw new ArgumentException($"备注不能超过 {MaximumNoteLength} 个字符。", nameof(note));
        }

        if (fee != 0m)
        {
            EnsureFee(fee, amount);
        }

        return new AssetTransaction
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            RequestId = requestId,
            AssetType = assetType,
            Kind = kind,
            FundPositionId = fundPositionId,
            Quantity = quantity,
            Amount = amount,
            CashEffect = cashEffect,
            Fee = fee,
            Price = price,
            Subject = Normalize(subject),
            Note = Normalize(note),
            BusinessDate = businessDate,
            CreatedAt = now
        };
    }

    private static Guid RequirePositionId(Guid fundPositionId)
    {
        if (fundPositionId == Guid.Empty)
        {
            throw new ArgumentException("基金持仓标识无效。", nameof(fundPositionId));
        }

        return fundPositionId;
    }

    private static void EnsureAmount(decimal amount, string paramName)
    {
        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("金额最多保留两位小数。", paramName);
        }

        if (decimal.Abs(amount) > MaximumAmount)
        {
            throw new ArgumentException("金额超出允许范围。", paramName);
        }
    }

    private static void EnsurePositiveAmount(decimal amount, string paramName)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("金额必须大于 0。", paramName);
        }

        EnsureAmount(amount, paramName);
    }

    private static void EnsureFee(decimal fee, decimal amount)
    {
        if (fee < 0 || fee > amount)
        {
            throw new ArgumentException("费用必须在 0 与买入金额之间。", nameof(fee));
        }

        if (decimal.Round(fee, 2) != fee)
        {
            throw new ArgumentException("费用最多保留两位小数。", nameof(fee));
        }
    }

    private static void EnsurePositiveQuantity(decimal quantity, string paramName)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("数量必须大于 0。", paramName);
        }

        if (decimal.Round(quantity, 4) != quantity)
        {
            throw new ArgumentException("数量最多保留四位小数。", paramName);
        }

        if (quantity > MaximumQuantity)
        {
            throw new ArgumentException("数量超出允许范围。", paramName);
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeQuoteSource(string? source)
    {
        var value = Normalize(source);

        if (value is not null && value.Length > MaximumQuoteSourceLength)
        {
            throw new ArgumentException($"行情来源不能超过 {MaximumQuoteSourceLength} 个字符。", nameof(source));
        }

        return value;
    }
}
