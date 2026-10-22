namespace AssetKeeper.Application.Gold;

public sealed record GoldMutationResultDto(
    decimal Grams,
    decimal Cost,
    decimal Quote,
    DateOnly? QuoteDate,
    decimal Value,
    decimal CashBalance,
    GoldActivityDto Activity);
