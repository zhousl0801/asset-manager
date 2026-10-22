namespace AssetKeeper.Application.Funds;

public sealed record FundMutationResultDto(
    FundPositionDto Position,
    decimal CashBalance,
    FundActivityDto Activity);
