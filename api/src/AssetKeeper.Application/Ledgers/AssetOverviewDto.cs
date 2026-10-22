namespace AssetKeeper.Application.Ledgers;

public sealed record AssetOverviewDto(bool IsConfigured, decimal? Amount, string? Note);
