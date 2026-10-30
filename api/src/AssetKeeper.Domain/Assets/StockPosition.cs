using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Assets;

public sealed class StockPosition
{
    public const decimal MaximumShares = 1_000_000_000m;
    public const decimal MaximumAmount = 1_000_000_000m;
    public const decimal MaximumPrice = 1_000_000_000m;
    public const int MaximumNameLength = 50;
    public const int MaximumCodeLength = 20;
    public const int MaximumCurrencyLength = 3;

    private StockPosition()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public StockMarket Market { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Currency { get; private set; } = "CNY";

    public decimal Shares { get; private set; }

    public decimal Cost { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly PriceDate { get; private set; }

    public decimal? PreviousClose { get; private set; }

    public DateOnly? PreviousCloseDate { get; private set; }

    public decimal RealizedProfit { get; private set; }

    public DateTimeOffset? LastQuoteAttemptAt { get; private set; }

    public string? LastQuoteSource { get; private set; }

    public string? LastQuoteError { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public decimal Value => decimal.Round(Shares * Price, 2);

    public decimal HoldingProfit => decimal.Round(Value - Cost, 2);

    // 保留 4 位，交给展示层按品种决定显示几位（ETF / LOF 报价到 0.001）。
    public decimal? AverageCost => Shares > 0m ? decimal.Round(Cost / Shares, 4) : null;

    public string DisplayName => $"{Name} ({Code})";

    public void RestoreProjection(decimal shares, decimal cost, decimal realizedProfit, decimal price, DateOnly priceDate, DateTimeOffset now)
    {
        Shares = shares;
        Cost = cost;
        RealizedProfit = realizedProfit;
        Price = price;
        PriceDate = priceDate;
        UpdatedAt = now;
        Version++;
    }

    public void RecordQuote(
        decimal price,
        DateOnly priceDate,
        decimal? previousClose,
        DateOnly? previousCloseDate,
        DateTimeOffset now)
    {
        ValidatePrice(price, nameof(price));
        ValidateDate(priceDate, nameof(priceDate));

        if (priceDate >= PriceDate)
        {
            Price = price;
            PriceDate = priceDate;
        }

        if (previousClose is > 0m && previousCloseDate is not null)
        {
            ValidatePrice(previousClose.Value, nameof(previousClose));
            ValidateDate(previousCloseDate.Value, nameof(previousCloseDate));

            if (previousCloseDate >= priceDate)
            {
                // 昨收必须早于行情日，否则不参与今日盈亏。
            }
            else if (PreviousCloseDate is null || previousCloseDate >= PreviousCloseDate)
            {
                PreviousClose = previousClose;
                PreviousCloseDate = previousCloseDate;
            }
        }

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

    public void UpdateProfile(string name, DateTimeOffset now)
    {
        ValidateText(name, MaximumNameLength, required: true, nameof(name));

        if (Name == name.Trim())
        {
            return;
        }

        Name = name.Trim();
        UpdatedAt = now;
        Version++;
    }

    public static StockPosition Create(
        Guid ledgerId,
        StockMarket market,
        string code,
        string name,
        string currency,
        decimal shares,
        decimal cost,
        decimal price,
        DateOnly priceDate,
        DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        ValidateText(code, MaximumCodeLength, required: true, nameof(code));
        ValidateText(name, MaximumNameLength, required: true, nameof(name));
        ValidateText(currency, MaximumCurrencyLength, required: true, nameof(currency));

        if (!Enum.IsDefined(market))
        {
            throw new ArgumentException("股票市场无效。", nameof(market));
        }

        ValidateShares(shares, allowZero: true);
        ValidateCost(cost);
        ValidatePrice(price, nameof(price));
        ValidateDate(priceDate, nameof(priceDate));

        if (shares == 0m && cost != 0m)
        {
            throw new ArgumentException("持有数量为 0 时，持仓成本也应为 0。", nameof(cost));
        }

        return new StockPosition
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Market = market,
            Code = NormalizeCode(code),
            Name = name.Trim(),
            Currency = currency.Trim().ToUpperInvariant(),
            Shares = shares,
            Cost = cost,
            Price = price,
            PriceDate = priceDate,
            UpdatedAt = now
        };
    }

    private static void ValidateText(string? value, int maximumLength, bool required, string paramName)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("名称或代码不能为空。", paramName);
        }

        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maximumLength)
        {
            throw new ArgumentException($"文本不能超过 {maximumLength} 个字符。", paramName);
        }
    }

    private static void ValidateShares(decimal shares, bool allowZero)
    {
        if (shares < 0m || (!allowZero && shares == 0m))
        {
            throw new ArgumentException(allowZero ? "持有数量不能为负数。" : "持有数量必须大于 0。", nameof(shares));
        }

        if (decimal.Round(shares, 4) != shares)
        {
            throw new ArgumentException("持有数量最多保留四位小数。", nameof(shares));
        }

        if (shares > MaximumShares)
        {
            throw new ArgumentException("持有数量超出允许范围。", nameof(shares));
        }
    }

    private static void ValidateCost(decimal cost)
    {
        if (cost < 0m)
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

    private static void ValidatePrice(decimal price, string paramName)
    {
        if (price <= 0m)
        {
            throw new ArgumentException("价格必须大于 0。", paramName);
        }

        if (decimal.Round(price, 4) != price)
        {
            throw new ArgumentException("价格最多保留四位小数。", paramName);
        }

        if (price > MaximumPrice)
        {
            throw new ArgumentException("价格超出允许范围。", paramName);
        }
    }

    private static void ValidateDate(DateOnly date, string paramName)
    {
        if (date == default)
        {
            throw new ArgumentException("日期无效。", paramName);
        }
    }

    private static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
