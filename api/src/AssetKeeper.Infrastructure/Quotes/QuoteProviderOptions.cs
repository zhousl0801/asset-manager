namespace AssetKeeper.Infrastructure.Quotes;

public sealed class QuoteProviderOptions
{
    public string FundBaseUrl { get; set; } = "https://api.fund.eastmoney.com/";

    public string GoldBaseUrl { get; set; } = "https://mybank.icbc.com.cn/";

    public int TimeoutSeconds { get; set; } = 8;
}
