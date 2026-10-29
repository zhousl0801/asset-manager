namespace AssetKeeper.Application.Funds;

public sealed record FundSearchResultDto(
    string Code,
    string Name,
    string BaseName,
    string? ShareClass,
    string FundType,
    bool IsQdii,
    int ConfirmationDelayDays);
