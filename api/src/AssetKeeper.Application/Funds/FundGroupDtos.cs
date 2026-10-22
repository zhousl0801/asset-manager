namespace AssetKeeper.Application.Funds;

public sealed record FundGroupDto(
    Guid Id,
    string Name,
    int SortOrder,
    int FundCount,
    int PositionCount,
    decimal ConfirmedValue);

public sealed record CreateFundGroupRequest(string Name);

public sealed record RenameFundGroupRequest(string Name);

public sealed record AssignFundGroupRequest(Guid? GroupId);

public sealed record FundGroupAssignmentDto(
    Guid? GroupId,
    IReadOnlyList<Guid> PositionIds);

public enum FundGroupOperationStatus
{
    Success,
    Created,
    NotFound,
    Invalid,
    Conflict
}

public sealed record FundGroupOperationResult(
    FundGroupOperationStatus Status,
    FundGroupDto? Group,
    FundGroupAssignmentDto? Assignment,
    IReadOnlyList<string> Errors)
{
    public static FundGroupOperationResult Success(FundGroupDto group) =>
        new(FundGroupOperationStatus.Success, group, null, []);

    public static FundGroupOperationResult Created(FundGroupDto group) =>
        new(FundGroupOperationStatus.Created, group, null, []);

    public static FundGroupOperationResult Success(FundGroupAssignmentDto assignment) =>
        new(FundGroupOperationStatus.Success, null, assignment, []);

    public static FundGroupOperationResult Deleted() =>
        new(FundGroupOperationStatus.Success, null, null, []);

    public static FundGroupOperationResult NotFound(string message) =>
        new(FundGroupOperationStatus.NotFound, null, null, [message]);

    public static FundGroupOperationResult Invalid(IReadOnlyList<string> errors) =>
        new(FundGroupOperationStatus.Invalid, null, null, errors);

    public static FundGroupOperationResult Conflict(string message) =>
        new(FundGroupOperationStatus.Conflict, null, null, [message]);
}
