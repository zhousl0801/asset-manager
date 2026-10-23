namespace AssetKeeper.Application.Backup;

public sealed class BackupDocument
{
    public const string CurrentFormat = "asset-keeper-backup";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;

    public int Version { get; set; } = CurrentVersion;

    public DateTimeOffset ExportedAt { get; set; }

    public BackupLedger Ledger { get; set; } = new();

    public BackupCashAccount CashAccount { get; set; } = new();

    public BackupProvidentFundAccount? ProvidentAccount { get; set; }

    public BackupGoldHolding? GoldHolding { get; set; }

    public List<BackupEntryCategory> Categories { get; set; } = [];

    public List<BackupLedgerEntry> Entries { get; set; } = [];

    public List<BackupCashAdjustment> CashAdjustments { get; set; } = [];

    public List<BackupFundGroup> FundGroups { get; set; } = [];

    public List<BackupFundPosition> FundPositions { get; set; } = [];

    public List<BackupAssetTransaction> AssetTransactions { get; set; } = [];

    public List<BackupPriceObservation> PriceObservations { get; set; } = [];

    public List<BackupPlan> Plans { get; set; } = [];

    public List<BackupPlanOccurrence> PlanOccurrences { get; set; } = [];

    public List<BackupMutationReceipt> MutationReceipts { get; set; } = [];
}

public sealed class BackupLedger
{
    public Guid Id { get; set; }
    public int SingletonSlot { get; set; } = 1;
    public string Currency { get; set; } = "CNY";
    public string TimeZone { get; set; } = "Asia/Shanghai";
    public DateOnly StartDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
    public long NextFactSequence { get; set; }
}

public sealed class BackupCashAccount
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public decimal Balance { get; set; }
    public decimal? OpeningBalance { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public bool HistoryConfirmed { get; set; } = true;
    public DateOnly AsOfDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupProvidentFundAccount
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public decimal Balance { get; set; }
    public DateOnly AsOfDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupGoldHolding
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public decimal Grams { get; set; }
    public decimal Cost { get; set; }
    public decimal Quote { get; set; }
    public DateOnly QuoteDate { get; set; }
    public decimal RealizedProfit { get; set; }
    public DateTimeOffset? LastQuoteAttemptAt { get; set; }
    public string? LastQuoteSource { get; set; }
    public string? LastQuoteError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupEntryCategory
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? SystemKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupLedgerEntry
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string? Note { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public long BusinessSequence { get; set; }
    public Guid? RelatedCashAdjustmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversalReason { get; set; }
    public int Version { get; set; }
}

public sealed class BackupCashAdjustment
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal NewBalance { get; set; }
    public decimal Difference { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long BusinessSequence { get; set; }
    public decimal RecordedDifference { get; set; }
    public bool IsReversed { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BackupFundGroup
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupFundPosition
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public Guid? FundGroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? ShareClass { get; set; }
    public string? Platform { get; set; }
    public decimal Shares { get; set; }
    public decimal Cost { get; set; }
    public decimal NetValue { get; set; }
    public DateOnly NetValueDate { get; set; }
    public decimal RealizedProfit { get; set; }
    public DateTimeOffset? LastQuoteAttemptAt { get; set; }
    public string? LastQuoteSource { get; set; }
    public string? LastQuoteError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupAssetTransaction
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public Guid RequestId { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid? FundPositionId { get; set; }
    public Guid? LinkedTransactionId { get; set; }
    public decimal? Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal CashEffect { get; set; }
    public decimal Fee { get; set; }
    public decimal? Price { get; set; }
    public string? QuoteSource { get; set; }
    public string? Subject { get; set; }
    public string? Note { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid? RelatedCashAdjustmentId { get; set; }
    public DateOnly? PriceDate { get; set; }
    public long BusinessSequence { get; set; }
    public bool NeedsDateConfirmation { get; set; }
    public bool IsReversed { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BackupPriceObservation
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public Guid? FundPositionId { get; set; }
    public Guid? SourceTransactionId { get; set; }
    public decimal Price { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Source { get; set; } = "manual";
    public DateTimeOffset ObtainedAt { get; set; }
    public long RevisionOrder { get; set; }
    public bool IsSuperseded { get; set; }
}

public sealed class BackupPlan
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Cycle { get; set; } = string.Empty;
    public int? DayOfMonth { get; set; }
    public int? DayOfWeek { get; set; }
    public TimeOnly Time { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly GenerateFrom { get; set; }
    public bool IsActive { get; set; }
    public Guid? FundPositionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupPlanOccurrence
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid LedgerId { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public Guid? FundPositionId { get; set; }
    public decimal Amount { get; set; }
    public int PlanVersion { get; set; }
    public DateOnly DueDate { get; set; }
    public TimeOnly DueTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? RelatedRecordId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class BackupMutationReceipt
{
    public Guid LedgerId { get; set; }
    public Guid RequestId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
