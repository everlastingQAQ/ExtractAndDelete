namespace ExtractAndDelete.Core;

/// <summary>
/// Coordinates validation, staged extraction, atomic publication and source cleanup.
/// </summary>
public sealed class ExtractionService
{
    private const string StagingPrefix = ".extractanddelete-";

    private readonly IArchiveExtractor _archiveExtractor;
    private readonly ICleanupService _cleanupService;

    public ExtractionService(
        IArchiveExtractor archiveExtractor,
        ICleanupService cleanupService)
    {
        _archiveExtractor = archiveExtractor
            ?? throw new ArgumentNullException(nameof(archiveExtractor));
        _cleanupService = cleanupService
            ?? throw new ArgumentNullException(nameof(cleanupService));
    }

    public static ExtractionService CreateDefault() =>
        new(new ArchiveExtractor(), new CleanupService());

    public async Task<ExtractAndDeleteResult> ExecuteAsync(
        ExtractAndDeleteRequest? request,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? archivePath = null;
        string? destinationPath = null;
        string? stagingPath = null;
        FileIdentity? originalIdentity = null;
        bool destinationPublished = false;

        try
        {
            progress?.Report(new ExtractionProgress(
                WorkflowStage.Validating,
                null,
                0,
                0,
                0,
                0,
                CanCancel: true));

            (archivePath, destinationPath) = ValidateRequest(request);

            originalIdentity = FileIdentityReader.TryRead(archivePath);
            if (originalIdentity is null)
            {
                return CreateResult(
                    WorkflowOutcome.ValidationFailed,
                    ErrorCode.ArchiveNotFound,
                    "无法读取源压缩包，文件可能已被移动或删除。",
                    archivePath,
                    destinationPath,
                    SourceDisposition.MissingOrChanged);
            }

            cancellationToken.ThrowIfCancellationRequested();

            string parentPath = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidOperationException("Destination parent path is unavailable.");
            stagingPath = CreateStagingPath(parentPath);

            ArchiveExtractionResult extractionResult =
                await _archiveExtractor.ExtractAsync(
                    archivePath,
                    stagingPath,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!extractionResult.Success)
            {
                bool stagingCleanupFailed = !TryDeleteStaging(stagingPath!);
                return CreateResult(
                    extractionResult.ErrorCode == ErrorCode.Cancelled
                        ? WorkflowOutcome.Cancelled
                        : WorkflowOutcome.ExtractionFailed,
                    stagingCleanupFailed
                        ? ErrorCode.StagingCleanupFailed
                        : extractionResult.ErrorCode,
                    stagingCleanupFailed
                        ? $"{extractionResult.UserMessage} 临时目录清理失败，请手动处理：{stagingPath}"
                        : extractionResult.UserMessage,
                    archivePath,
                    destinationPath,
                    SourceDisposition.Retained,
                    CombineDiagnostics(
                        extractionResult.DiagnosticMessage,
                        stagingCleanupFailed ? $"Staging path: {stagingPath}" : null),
                    stagingCleanupFailed ? stagingPath : null);
            }

            FileIdentity? identityBeforePublish = FileIdentityReader.TryRead(archivePath);
            if (identityBeforePublish is null || !originalIdentity.Equals(identityBeforePublish))
            {
                bool stagingCleanupFailed = !TryDeleteStaging(stagingPath);
                return CreateResult(
                    WorkflowOutcome.ExtractionFailed,
                    stagingCleanupFailed ? ErrorCode.StagingCleanupFailed : ErrorCode.SourceChanged,
                    stagingCleanupFailed
                        ? $"源压缩包在执行期间发生变化，且临时目录清理失败，请手动处理：{stagingPath}"
                        : "源压缩包在解压期间发生变化，已保留源文件且未发布目标目录。",
                    archivePath,
                    destinationPath,
                    SourceDisposition.MissingOrChanged,
                    stagingCleanupFailed ? $"Staging path: {stagingPath}" : null,
                    stagingCleanupFailed ? stagingPath : null);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                bool stagingCleanupFailed = !TryDeleteStaging(stagingPath);
                return CreateResult(
                    WorkflowOutcome.Cancelled,
                    stagingCleanupFailed ? ErrorCode.StagingCleanupFailed : ErrorCode.Cancelled,
                    stagingCleanupFailed
                        ? $"已取消解压，但临时目录清理失败，请手动处理：{stagingPath}"
                        : "用户已取消解压。",
                    archivePath,
                    destinationPath,
                    SourceDisposition.Retained,
                    stagingCleanupFailed ? $"Staging path: {stagingPath}" : null,
                    stagingCleanupFailed ? stagingPath : null);
            }

            progress?.Report(new ExtractionProgress(
                WorkflowStage.Publishing,
                null,
                0,
                0,
                0,
                0,
                CanCancel: false));

            try
            {
                // Both paths are generated/validated on the same volume, so this is atomic.
                Directory.Move(stagingPath, destinationPath);
                stagingPath = null;
                destinationPublished = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bool stagingCleanupFailed = !TryDeleteStaging(stagingPath!);
                return CreateResult(
                    WorkflowOutcome.PublishFailed,
                    stagingCleanupFailed ? ErrorCode.StagingCleanupFailed : ErrorCode.PublishFailure,
                    stagingCleanupFailed
                        ? $"发布解压目录失败，且临时目录清理失败，请手动处理：{stagingPath}"
                        : "无法发布解压目录，源压缩包已保留。",
                    archivePath,
                    destinationPath,
                    SourceDisposition.Retained,
                    CombineDiagnostics(
                        ex.ToString(),
                        stagingCleanupFailed ? $"Staging path: {stagingPath}" : null),
                    stagingCleanupFailed ? stagingPath : null);
            }

            // Cancellation is deliberately ignored after publication begins. The final
            // directory must not be rolled back and cleanup must still be attempted.
            FileIdentity? identityBeforeRecycle = FileIdentityReader.TryRead(archivePath);
            if (identityBeforeRecycle is null || !originalIdentity.Equals(identityBeforeRecycle))
            {
                progress?.Report(new ExtractionProgress(
                    WorkflowStage.Recycling,
                    null,
                    0,
                    0,
                    0,
                    0,
                    CanCancel: false));

                return CreateResult(
                    WorkflowOutcome.CleanupFailed,
                    ErrorCode.SourceChanged,
                    "源压缩包在发布后发生变化，未执行回收操作；源文件已保留。",
                    archivePath,
                    destinationPath,
                    SourceDisposition.MissingOrChanged,
                    identityBeforeRecycle is null ? "Source file is missing." : "Source identity changed.",
                    DestinationPublished: true);
            }

            progress?.Report(new ExtractionProgress(
                WorkflowStage.Recycling,
                null,
                0,
                0,
                0,
                0,
                CanCancel: false));

            CleanupResult cleanupResult =
                await _cleanupService.MoveToRecycleBinAsync(archivePath)
                    .ConfigureAwait(false);

            if (!cleanupResult.Success)
            {
                SourceDisposition disposition = File.Exists(archivePath)
                    ? SourceDisposition.Retained
                    : SourceDisposition.MissingOrChanged;
                return CreateResult(
                    WorkflowOutcome.CleanupFailed,
                    cleanupResult.ErrorCode == ErrorCode.None
                        ? ErrorCode.RecycleFailed
                        : cleanupResult.ErrorCode,
                    "文件已完整解压，但源压缩包无法移入回收站，源文件已保留。",
                    archivePath,
                    destinationPath,
                    disposition,
                    cleanupResult.DiagnosticMessage,
                    DestinationPublished: true);
            }

            progress?.Report(new ExtractionProgress(
                WorkflowStage.Completed,
                null,
                0,
                0,
                0,
                0,
                CanCancel: false));

            return CreateResult(
                WorkflowOutcome.Completed,
                ErrorCode.None,
                "解压并回收完成。",
                archivePath,
                destinationPath,
                SourceDisposition.Recycled,
                DestinationPublished: true);
        }
        catch (OperationCanceledException)
        {
            if (destinationPublished)
            {
                return CreateResult(
                    WorkflowOutcome.CleanupFailed,
                    ErrorCode.Unexpected,
                    "文件已完整解压，但源压缩包无法移入回收站，源文件已保留。",
                    archivePath ?? request?.ArchivePath ?? string.Empty,
                    destinationPath ?? request?.DestinationPath ?? string.Empty,
                    File.Exists(archivePath) ? SourceDisposition.Retained : SourceDisposition.MissingOrChanged,
                    "Cancellation was raised after publication began.",
                    DestinationPublished: true);
            }

            if (archivePath is null || destinationPath is null || stagingPath is null)
            {
                return CreateResult(
                    WorkflowOutcome.Cancelled,
                    ErrorCode.Cancelled,
                    "用户已取消操作。",
                    archivePath ?? request?.ArchivePath ?? string.Empty,
                    destinationPath ?? request?.DestinationPath ?? string.Empty,
                    SourceDisposition.Retained);
            }

            bool stagingCleanupFailed = !TryDeleteStaging(stagingPath);
            return CreateResult(
                WorkflowOutcome.Cancelled,
                stagingCleanupFailed ? ErrorCode.StagingCleanupFailed : ErrorCode.Cancelled,
                stagingCleanupFailed
                    ? $"已取消解压，但临时目录清理失败，请手动处理：{stagingPath}"
                    : "用户已取消解压。",
                archivePath,
                destinationPath,
                SourceDisposition.Retained,
                stagingCleanupFailed ? $"Staging path: {stagingPath}" : null,
                stagingCleanupFailed ? stagingPath : null);
        }
        catch (ValidationException ex)
        {
            return CreateResult(
                WorkflowOutcome.ValidationFailed,
                ex.ErrorCode,
                ex.UserMessage,
                archivePath ?? request?.ArchivePath ?? string.Empty,
                destinationPath ?? request?.DestinationPath ?? string.Empty,
                SourceDisposition.Retained,
                ex.ToString());
        }
        catch (Exception ex)
        {
            if (destinationPublished)
            {
                return CreateResult(
                    WorkflowOutcome.CleanupFailed,
                    ErrorCode.Unexpected,
                    "文件已完整解压，但源压缩包无法移入回收站，源文件已保留。",
                    archivePath ?? request?.ArchivePath ?? string.Empty,
                    destinationPath ?? request?.DestinationPath ?? string.Empty,
                    File.Exists(archivePath) ? SourceDisposition.Retained : SourceDisposition.MissingOrChanged,
                    ex.ToString(),
                    DestinationPublished: true);
            }

            bool stagingCleanupFailed = false;
            if (stagingPath is not null)
            {
                stagingCleanupFailed = !TryDeleteStaging(stagingPath);
            }

            return CreateResult(
                WorkflowOutcome.ExtractionFailed,
                stagingCleanupFailed ? ErrorCode.StagingCleanupFailed : ErrorCode.Unexpected,
                stagingCleanupFailed
                    ? $"操作失败，且临时目录清理失败，请手动处理：{stagingPath}"
                    : "操作过程中发生未预期错误。",
                archivePath ?? request?.ArchivePath ?? string.Empty,
                destinationPath ?? request?.DestinationPath ?? string.Empty,
                SourceDisposition.Retained,
                CombineDiagnostics(
                    ex.ToString(),
                    stagingCleanupFailed ? $"Staging path: {stagingPath}" : null),
                stagingCleanupFailed ? stagingPath : null);
        }
    }

    private static (string ArchivePath, string DestinationPath) ValidateRequest(
        ExtractAndDeleteRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException(
                ErrorCode.InvalidArchivePath,
                "未提供源压缩包路径。");
        }

        string archivePath = GetFullPathOrThrow(
            request.ArchivePath,
            ErrorCode.InvalidArchivePath,
            "源压缩包路径无效。");
        string destinationPath = GetFullPathOrThrow(
            request.DestinationPath,
            ErrorCode.InvalidDestination,
            "最终目标目录路径无效。");

        if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                ErrorCode.UnsupportedFormat,
                "当前版本仅支持 ZIP 压缩包。");
        }

        if (!File.Exists(archivePath))
        {
            throw new ValidationException(
                ErrorCode.ArchiveNotFound,
                "源压缩包不存在。");
        }

        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new ValidationException(
                ErrorCode.DestinationAlreadyExists,
                "最终目录已存在，请重新选择目标父目录。");
        }

        string? parentPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(parentPath) || !Directory.Exists(parentPath))
        {
            throw new ValidationException(
                ErrorCode.InvalidDestination,
                "目标父目录不存在或无效。");
        }

        return (archivePath, destinationPath);
    }

    private static string GetFullPathOrThrow(
        string? path,
        ErrorCode errorCode,
        string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ValidationException(errorCode, message);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ValidationException(errorCode, message, ex);
        }
    }

    private static string CreateStagingPath(string parentPath)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = Path.Combine(
                parentPath,
                $"{StagingPrefix}{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to allocate a unique staging path.");
    }

    private static bool TryDeleteStaging(string stagingPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(stagingPath);
            string name = Path.GetFileName(fullPath);
            if (!name.StartsWith(StagingPrefix, StringComparison.Ordinal)
                || !name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                return false;
            }

            if (Directory.Exists(fullPath))
            {
                if (!TryDeleteStagingDirectory(fullPath))
                {
                    return false;
                }
            }
            else if (File.Exists(fullPath))
            {
                // A staging path is always a directory. Do not delete an unexpected
                // replacement file merely because its name matches our prefix.
                return false;
            }

            return !Directory.Exists(fullPath) && !File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteStagingDirectory(string path)
    {
        FileAttributes rootAttributes;
        try
        {
            rootAttributes = File.GetAttributes(path);
        }
        catch
        {
            return false;
        }

        if ((rootAttributes & FileAttributes.ReparsePoint) != 0
            || (rootAttributes & FileAttributes.Directory) == 0)
        {
            return false;
        }

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(path, "*", options);
        }
        catch
        {
            return false;
        }

        foreach (string entry in entries)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Never recurse through a junction or symlink during cleanup.
                return false;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!TryDeleteStagingDirectory(entry))
                {
                    return false;
                }
            }
            else
            {
                try
                {
                    File.Delete(entry);
                    if (File.Exists(entry))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        try
        {
            Directory.Delete(path, recursive: false);
            return !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static ExtractAndDeleteResult CreateResult(
        WorkflowOutcome outcome,
        ErrorCode errorCode,
        string userMessage,
        string archivePath,
        string destinationPath,
        SourceDisposition sourceDisposition,
        string? diagnosticMessage = null,
        string? stagingPath = null,
        bool DestinationPublished = false)
    {
        return new ExtractAndDeleteResult(
            outcome,
            errorCode,
            userMessage,
            archivePath,
            destinationPath,
            DestinationPublished,
            sourceDisposition,
            diagnosticMessage,
            stagingPath);
    }

    private static string? CombineDiagnostics(params string?[] messages)
    {
        string[] nonEmpty = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Cast<string>()
            .ToArray();
        return nonEmpty.Length == 0 ? null : string.Join(Environment.NewLine, nonEmpty);
    }

    private sealed class ValidationException : Exception
    {
        public ValidationException(ErrorCode errorCode, string userMessage, Exception? inner = null)
            : base(userMessage, inner)
        {
            ErrorCode = errorCode;
            UserMessage = userMessage;
        }

        public ErrorCode ErrorCode { get; }

        public string UserMessage { get; }
    }
}
