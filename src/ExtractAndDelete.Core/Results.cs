namespace ExtractAndDelete.Core;

public enum WorkflowStage
{
    Validating,
    Scanning,
    Extracting,
    Publishing,
    Recycling,
    Completed
}

public enum WorkflowOutcome
{
    Completed,
    CompletedWithSkippedItems,
    Cancelled,
    ValidationFailed,
    ExtractionFailed,
    PublishFailed,
    CleanupFailed
}

public enum ErrorCode
{
    None,
    InvalidArchivePath,
    ArchiveNotFound,
    UnsupportedFormat,
    InvalidDestination,
    DestinationAlreadyExists,
    ArchiveUnreadable,
    UnsafeArchiveEntry,
    DuplicateArchiveEntry,
    ArchiveEntryConflict,
    InsufficientDiskSpace,
    ArchiveSizeOverflow,
    ExtractionIoFailure,
    PublishFailure,
    SourceChanged,
    RecycleUnavailable,
    RecycleFailed,
    StagingCleanupFailed,
    Cancelled,
    Unexpected,
    ArchiveEncrypted,
    MultiVolumeArchiveNotSupported,
    UnsupportedArchiveEntryType,
    ArchiveTooComplex,
    ArchiveEngineUnavailable,
    ArchiveEngineIntegrityFailure,
    ArchiveEngineProtocolFailure,
    ArchiveEngineProcessFailure,
    ArchiveEngineTerminationFailure,
    ArchiveVerificationFailure,
    InsufficientMemory,
    DestinationItemsSkipped,
    DestinationCreationFailed,
    DestinationPublishAborted,
    SourceDestinationConflict
}

public enum DestinationPublishOutcome
{
    Completed,
    CompletedWithSkippedItems,
    Cancelled,
    Failed
}

public enum DestinationState
{
    Unchanged,
    PartiallyModified,
    CompletedWithSkippedItems,
    Completed
}

public enum SourceDisposition
{
    Recycled,
    Retained,
    MissingOrChanged
}

public enum StagingCleanupState
{
    NotCreated,
    ReadyForPublish,
    Cleaned,
    RetainedForSafety,
    CleanupFailed
}

public sealed record ExtractAndDeleteRequest(
    string ArchivePath,
    string DestinationPath);

public sealed record ExtractionProgress(
    WorkflowStage Stage,
    string? CurrentEntry,
    int CompletedEntries,
    int TotalEntries,
    long CompletedBytes,
    long TotalBytes,
    bool CanCancel,
    bool IsIndeterminate = false)
{
    public double Percentage => TotalBytes <= 0
        ? (TotalEntries == 0 ? 0 : (double)CompletedEntries / TotalEntries * 100)
        : (double)CompletedBytes / TotalBytes * 100;
}

public sealed record ArchiveExtractionResult(
    bool Success,
    ErrorCode ErrorCode,
    string UserMessage,
    string? DiagnosticMessage = null,
    StagingCleanupState StagingCleanupState = StagingCleanupState.NotCreated,
    string? StagingPath = null,
    int? EngineExitCode = null)
{
    public string? ErrorMessage => Success ? null : UserMessage;
}

public sealed record CleanupResult(
    bool Success,
    ErrorCode ErrorCode,
    string UserMessage,
    string? DiagnosticMessage = null)
{
    public string? ErrorMessage => Success ? null : UserMessage;
}

public sealed record DestinationPublishResult(
    DestinationPublishOutcome Outcome,
    ErrorCode ErrorCode,
    string UserMessage,
    DestinationState DestinationState,
    int SucceededItemCount,
    int SkippedItemCount,
    int FailedItemCount,
    string? DiagnosticMessage = null)
{
    public bool IsComplete => Outcome == DestinationPublishOutcome.Completed
        && DestinationState == DestinationState.Completed
        && SkippedItemCount == 0
        && FailedItemCount == 0;
}

public sealed record ExtractAndDeleteResult(
    WorkflowOutcome Outcome,
    ErrorCode ErrorCode,
    string UserMessage,
    string ArchivePath,
    string DestinationPath,
    DestinationState DestinationState,
    SourceDisposition SourceDisposition,
    string? DiagnosticMessage = null,
    string? StagingPath = null)
{
    public bool DestinationPublished => DestinationState == DestinationState.Completed;

    public bool Success => Outcome == WorkflowOutcome.Completed
        && DestinationPublished
        && SourceDisposition == SourceDisposition.Recycled;

    public string? ErrorMessage => Success ? null : UserMessage;
}
