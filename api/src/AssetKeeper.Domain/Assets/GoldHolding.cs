using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Assets;

public sealed class GoldHolding
{
    public const decimal MaximumGrams = 1_000_000m;
    public const decimal MaximumAmount = 1_000_000_000m;
    public const decimal MaximumQuote = 1_000_000_000m;

    private GoldHolding()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public decimal Grams { get; private set; }

    public decimal Cost { get; private set; }

    public decimal Quote { get; private set; }

    public DateOnly QuoteDate { get; private set; }

    public decimal RealizedProfit { get; private set; }

    public DateTimeOffset? LastQuoteAttemptAt { get; private set; }

    public string? LastQuoteSource { get; private set; }

    public string? LastQuoteError { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public decimal Value => decimal.Round(Grams * Quote, 2);

    public decimal HoldingProfit => decimal.Round(Value - Cost, 2);

    public void RestoreProjection(decimal grams, decimal cost, decimal realizedProfit, decimal quote, DateOnly quoteDate, DateTimeOffset now)
    {
        Grams = grams;
        Cost = cost;
        RealizedProfit = realizedProfit;
        Quote = quote;
        QuoteDate = quoteDate;
        UpdatedAt = now;
        Version++;
    }

    public void RecordQuoteSyncAttempt(DateTimeOffset now, string? source, string? error)
    {
        LastQuoteAttemptAt = now;
        LastQuoteSource = Normalize(source);
        LastQuoteError = Normalize(error);
        UpdatedAt = now;
        Version++;
    }

    public static GoldHolding Create(
        Guid ledgerId,
        decimal grams,
        decimal cost,
        decimal quote,
        DateOnly quoteDate,
        DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        ValidateGrams(grams, allowZero: true);
        ValidateCost(cost);
        ValidateQuote(quote);
        ValidateDate(quoteDate, nameof(quoteDate));

        if (grams == 0 && cost != 0)
        {
            throw new ArgumentException("持有克数为 0 时，持仓成本也应为 0。", nameof(cost));
        }

        return new GoldHolding
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Grams = grams,
            Cost = cost,
            Quote = quote,
            QuoteDate = quoteDate,
            UpdatedAt = now
        };
    }

    public void Buy(decimal grams, decimal amount, decimal quote, DateOnly quoteDate, DateTimeOffset now)
    {
        ValidateGrams(grams, allowZero: false);
        ValidateAmount(amount);
        ValidateQuote(quote);
        ValidateDate(quoteDate, nameof(quoteDate));

        if (Grams + grams > MaximumGrams)
        {
            throw new InvalidOperationException("持有克数超出允许范围。");
        }

        if (Cost + amount > MaximumAmount)
        {
            throw new InvalidOperationException("持仓成本超出允许范围。");
        }

        Grams += grams;
        Cost += amount;
        if (quoteDate >= QuoteDate)
        {
            Quote = quote;
            QuoteDate = quoteDate;
        }
        UpdatedAt = now;
        Version++;
    }

    public void Sell(decimal grams, decimal netAmount, DateTimeOffset now)
    {
        ValidateGrams(grams, allowZero: false);
        ValidateAmount(netAmount);

        if (grams > Grams)
        {
            throw new InvalidOperationException("卖出克数不能超过持有克数。");
        }

        var costReduction = grams == Grams
            ? Cost
            : decimal.Round(Cost * grams / Grams, 2);

        Grams -= grams;
        Cost -= costReduction;

        if (Grams == 0)
        {
            Cost = 0m;
        }

        RealizedProfit = decimal.Round(RealizedProfit + netAmount - costReduction, 2);
        UpdatedAt = now;
        Version++;
    }

    public void UpdateQuote(decimal quote, DateOnly quoteDate, DateTimeOffset now)
    {
        ValidateQuote(quote);
        ValidateDate(quoteDate, nameof(quoteDate));

        if (quoteDate >= QuoteDate)
        {
            Quote = quote;
            QuoteDate = quoteDate;
        }
        UpdatedAt = now;
        Version++;
    }

    public void Reconcile(
        decimal grams,
        decimal cost,
        decimal quote,
        DateOnly quoteDate,
        DateTimeOffset now)
    {
        ValidateGrams(grams, allowZero: true);
        ValidateCost(cost);
        ValidateQuote(quote);
        ValidateDate(quoteDate, nameof(quoteDate));

        if (grams == 0 && cost != 0)
        {
            throw new ArgumentException("持有克数为 0 时，持仓成本也应为 0。", nameof(cost));
        }

        Grams = grams;
        Cost = cost;
        if (quoteDate >= QuoteDate)
        {
            Quote = quote;
            QuoteDate = quoteDate;
        }
        UpdatedAt = now;
        Version++;
    }

    private static void ValidateGrams(decimal grams, bool allowZero)
    {
        if (grams < 0 || (!allowZero && grams == 0))
        {
            throw new ArgumentException(allowZero ? "持有克数不能为负数。" : "克数必须大于 0。", nameof(grams));
        }

        if (decimal.Round(grams, 4) != grams)
        {
            throw new ArgumentException("克数最多保留四位小数。", nameof(grams));
        }

        if (grams > MaximumGrams)
        {
            throw new ArgumentException("克数超出允许范围。", nameof(grams));
        }
    }

    private static void ValidateCost(decimal cost)
    {
        if (cost < 0)
        {
            throw new ArgumentException("持仓成本不能为负数。", nameof(cost));
        }

        if (decimal.Round(cost, 2) != cost)
        {
            throw new ArgumentException("持仓成本最多保留两位小数。", nameof(cost));
        }

        if (cost > MaximumAmount)
        {
            throw new ArgumentException("持仓成本超出允许范围。", nameof(cost));
        }
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("金额必须大于 0。", nameof(amount));
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("金额最多保留两位小数。", nameof(amount));
        }

        if (amount > MaximumAmount)
        {
            throw new ArgumentException("金额超出允许范围。", nameof(amount));
        }
    }

    private static void ValidateQuote(decimal quote)
    {
        if (quote <= 0)
        {
            throw new ArgumentException("报价必须大于 0。", nameof(quote));
        }

        if (decimal.Round(quote, 4) != quote)
        {
            throw new ArgumentException("报价最多保留四位小数。", nameof(quote));
        }

        if (quote > MaximumQuote)
        {
            throw new ArgumentException("报价超出允许范围。", nameof(quote));
        }
    }

    private static void ValidateDate(DateOnly date, string paramName)
    {
        if (date == default)
        {
            throw new ArgumentException("日期无效。", paramName);
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
