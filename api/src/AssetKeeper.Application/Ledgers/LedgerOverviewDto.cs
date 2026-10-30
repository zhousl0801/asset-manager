namespace AssetKeeper.Application.Ledgers;

public sealed record LedgerOverviewDto(
    Guid Id,
    string Currency,
    string TimeZone,
    DateOnly StartDate,
    DateOnly AsOfDate,
    decimal TotalAssets,
    AssetOverviewDto Cash,
    AssetOverviewDto Fund,
    AssetOverviewDto ProvidentFund,
    AssetOverviewDto Gold,
    AssetOverviewDto Stock,
    MonthSummaryDto Month,
    bool IsComplete = false,
    bool FundNotHeld = false,
    bool ProvidentFundNotHeld = false,
    bool GoldNotHeld = false,
    bool StockNotHeld = false,
    bool CoverageComplete = false);
