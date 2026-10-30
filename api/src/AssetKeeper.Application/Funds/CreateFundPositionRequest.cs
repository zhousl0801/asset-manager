using System.Text.Json.Serialization;

namespace AssetKeeper.Application.Funds;

public sealed record CreateFundPositionRequest(
    string Name,
    string? Code,
    string? ShareClass,
    string? Platform,
    decimal Shares,
    decimal Cost,
    [property: JsonIgnore]
    decimal NetValue,
    [property: JsonIgnore]
    DateOnly NetValueDate,
    string? Note,
    Guid RequestId,
    DateOnly? HoldingDate = null,
    int ConfirmationDelayDays = 2,
    decimal PriorRealizedProfit = 0m);
