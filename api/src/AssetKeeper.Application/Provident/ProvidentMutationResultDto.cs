namespace AssetKeeper.Application.Provident;

public sealed record ProvidentMutationResultDto(
    decimal Balance,
    ProvidentActivityDto Activity);
