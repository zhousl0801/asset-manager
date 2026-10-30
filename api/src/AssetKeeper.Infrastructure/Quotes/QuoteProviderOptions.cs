namespace AssetKeeper.Infrastructure.Quotes;

public sealed class QuoteProviderOptions
{
    public string FundBaseUrl { get; set; } = "https://api.fund.eastmoney.com/";

    // 移动端净值接口在另一个集群，字段名与主站一致（FSRQ / DWJZ），作为基金净值的降级源。
    public string FundFallbackBaseUrl { get; set; } = "https://fundmobapi.eastmoney.com/";

    public string GoldBaseUrl { get; set; } = "https://mybank.icbc.com.cn/";

    public string StockBaseUrl { get; set; } = "https://push2.eastmoney.com/";

    // 主站偶发整机空响应（连上后不回包），备用站点字段完全一致，价格可能略有延迟。
    public string StockFallbackBaseUrl { get; set; } = "https://push2delay.eastmoney.com/";

    public int TimeoutSeconds { get; set; } = 8;
}
