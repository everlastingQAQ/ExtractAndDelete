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
    ExtractionIoFailure,
    PublishFailure,
    SourceChanged,
    RecycleUnavailable,
    RecycleFailed,
    StagingCleanupFailed,
    Cancelled,
    Unexpected
}

public enum SourceDisposition
{
    Recycled,
    Retained,
    MissingOrChanged
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
    bool CanCancel)
{
    public double Percentage => TotalBytes <= 0
        ? (TotalEntries == 0 ? 0 : (double)CompletedEntries / TotalEntries * 100)
        : (double)CompletedBytes / TotalBytes * 100;
}

public sealed record ArchiveExtractionResult(
    bool Success,
    ErrorCode ErrorCode,
    string UserMessage,
    string? DiagnosticMessage = null);

public sealed record CleanupResult(
    bool Success,
    ErrorCode ErrorCode,
    string UserMessage,
    string? DiagnosticMessage = null);

public sealed record ExtractAndDeleteResult(
    WorkflowOutcome Outcome,
    ErrorCode ErrorCode,
    string UserMessage,
    string ArchivePath,
    string DestinationPath,
    bool DestinationPublished,
    SourceDisposition SourceDisposition,
    string? DiagnosticMessage = null,
    string? StagingPath = null)
{
    public bool Success => Outcome == WorkflowOutcome.Completed;

    public string? ErrorMessage => Success ? null : UserMessage;
}
