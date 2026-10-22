using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Assets;

public sealed class FundPosition
{
    public const decimal MaximumShares = 1_000_000_000m;
    public const decimal MaximumAmount = 1_000_000_000m;
    public const decimal MaximumNetValue = 1_000_000_000m;
    public const int MaximumNameLength = 50;
    public const int MaximumCodeLength = 20;
    public const int MaximumShareClassLength = 10;
    public const int MaximumPlatformLength = 30;

    private FundPosition()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Code { get; private set; }

    public string? ShareClass { get; private set; }

    public string? Platform { get; private set; }

    public decimal Shares { get; private set; }

    public decimal Cost { get; private set; }

    public decimal NetValue { get; private set; }

    public DateOnly NetValueDate { get; private set; }

    public decimal RealizedProfit { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public decimal Value => decimal.Round(Shares * NetValue, 2);

    public decimal HoldingProfit => decimal.Round(Value - Cost, 2);

    public string DisplayName => string.IsNullOrWhiteSpace(ShareClass) ? Name : $"{Name} {ShareClass}";

    public void RestoreProjection(decimal shares, decimal cost, decimal realizedProfit, decimal netValue, DateOnly netValueDate, DateTimeOffset now)
    {
        Shares = shares;
        Cost = cost;
        RealizedProfit = realizedProfit;
        NetValue = netValue;
        NetValueDate = netValueDate;
        UpdatedAt = now;
        Version++;
    }

    public static FundPosition Create(
        Guid ledgerId,
        string name,
        string? code,
        string? shareClass,
        string? platform,
        decimal shares,
        decimal cost,
        decimal netValue,
        DateOnly netValueDate,
        DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        ValidateText(name, MaximumNameLength, required: true, nameof(name));
        ValidateText(code, MaximumCodeLength, required: false, nameof(code));
        ValidateText(shareClass, MaximumShareClassLength, required: false, nameof(shareClass));
        ValidateText(platform, MaximumPlatformLength, required: false, nameof(platform));
        ValidateShares(shares, allowZero: true);
        ValidateCost(cost);
        ValidateNetValue(netValue);
        ValidateDate(netValueDate, nameof(netValueDate));

        if (shares == 0 && cost != 0)
        {
            throw new ArgumentException("份额为 0 时，持仓成本也应为 0。", nameof(cost));
        }

        return new FundPosition
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Name = name.Trim(),
            Code = Normalize(code),
            ShareClass = Normalize(shareClass),
            Platform = Normalize(platform),
            Shares = shares,
            Cost = cost,
            NetValue = netValue,
            NetValueDate = netValueDate,
            UpdatedAt = now
        };
    }

    public void Buy(
        decimal shares,
        decimal amount,
        decimal netValue,
        DateOnly netValueDate,
        DateTimeOffset now)
    {
        ValidateShares(shares, allowZero: false);
        ValidateAmount(amount);
        ValidateNetValue(netValue);
        ValidateDate(netValueDate, nameof(netValueDate));

        if (Shares + shares > MaximumShares)
        {
            throw new InvalidOperationException("持有份额超出允许范围。");
        }

        if (Cost + amount > MaximumAmount)
        {
            throw new InvalidOperationException("持仓成本超出允许范围。");
        }

        Shares += shares;
        Cost += amount;
        if (netValueDate >= NetValueDate)
        {
            NetValue = netValue;
            NetValueDate = netValueDate;
        }
        UpdatedAt = now;
        Version++;
    }

    public void Sell(decimal shares, decimal netAmount, DateTimeOffset now)
    {
        ValidateShares(shares, allowZero: false);
        ValidateAmount(netAmount);

        if (shares > Shares)
        {
            throw new InvalidOperationException("卖出份额不能超过持有份额。");
        }

        var costReduction = shares == Shares
            ? Cost
            : decimal.Round(Cost * shares / Shares, 2);

        Shares -= shares;
        Cost -= costReduction;

        if (Shares == 0)
        {
            Cost = 0m;
        }

        RealizedProfit = decimal.Round(RealizedProfit + netAmount - costReduction, 2);
        UpdatedAt = now;
        Version++;
    }

    public void UpdateQuote(decimal netValue, DateOnly netValueDate, DateTimeOffset now)
    {
        ValidateNetValue(netValue);
        ValidateDate(netValueDate, nameof(netValueDate));

        if (netValueDate >= NetValueDate)
        {
            NetValue = netValue;
            NetValueDate = netValueDate;
        }
        UpdatedAt = now;
        Version++;
    }

    private static void ValidateText(string? value, int maximumLength, bool required, string paramName)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("名称不能为空。", paramName);
        }

        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maximumLength)
        {
            throw new ArgumentException($"文本不能超过 {maximumLength} 个字符。", paramName);
        }
    }

    private static void ValidateShares(decimal shares, bool allowZero)
    {
        if (shares < 0 || (!allowZero && shares == 0))
        {
            throw new ArgumentException(allowZero ? "份额不能为负数。" : "份额必须大于 0。", nameof(shares));
        }

        if (decimal.Round(shares, 4) != shares)
        {
            throw new ArgumentException("份额最多保留四位小数。", nameof(shares));
        }

        if (shares > MaximumShares)
        {
            throw new ArgumentException("份额超出允许范围。", nameof(shares));
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

    private static void ValidateNetValue(decimal netValue)
    {
        if (netValue <= 0)
        {
            throw new ArgumentException("净值必须大于 0。", nameof(netValue));
        }

        if (decimal.Round(netValue, 4) != netValue)
        {
            throw new ArgumentException("净值最多保留四位小数。", nameof(netValue));
        }

        if (netValue > MaximumNetValue)
        {
            throw new ArgumentException("净值超出允许范围。", nameof(netValue));
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
