namespace ExtractAndDelete.Core;

/// <summary>
/// Coordinates validation, staged extraction, host-specific publication and
/// source cleanup. The archive engine is never allowed to write directly to
/// the user's target directory.
/// </summary>
public sealed class ExtractionService
{
    private const string StagingPrefix = ".extractanddelete-";

    private readonly IArchiveExtractor _archiveExtractor;
    private readonly IDestinationPublisher _destinationPublisher;
    private readonly ICleanupService _cleanupService;

    public ExtractionService(
        IArchiveExtractor archiveExtractor,
        IDestinationPublisher destinationPublisher,
        ICleanupService cleanupService)
    {
        _archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        _destinationPublisher = destinationPublisher ?? throw new ArgumentNullException(nameof(destinationPublisher));
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
    }

    // Compatibility constructor for V2 callers and the strict CLI tests.
    public ExtractionService(IArchiveExtractor archiveExtractor, ICleanupService cleanupService)
        : this(archiveExtractor, new AtomicDirectoryPublisher(), cleanupService)
    {
    }

    public static ExtractionService CreateDefault() =>
        new(
            new SevenZipArchiveExtractor(SevenZipToolProvider.CreateDefault()),
            new AtomicDirectoryPublisher(),
            new CleanupService());

    public static ExtractionService CreateGui(IntPtr ownerWindow) =>
        new(
            new SevenZipArchiveExtractor(SevenZipToolProvider.CreateDefault()),
            new WindowsShellDestinationPublisher(ownerWindow),
            new CleanupService());

    public async Task<ExtractAndDeleteResult> ExecuteAsync(
        ExtractAndDeleteRequest? request,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? archivePath = null;
        string? destinationPath = null;
        string? stagingPath = null;
        FileIdentity? originalIdentity = null;
        DestinationState destinationState = DestinationState.Unchanged;
        bool publishingStarted = false;

        try
        {
            progress?.Report(new ExtractionProgress(WorkflowStage.Validating, null, 0, 0, 0, 0, true));
            (archivePath, destinationPath) = ValidateRequest(request);
            originalIdentity = FileIdentityReader.TryRead(archivePath);
            if (originalIdentity is null)
            {
                return Result(WorkflowOutcome.ValidationFailed, ErrorCode.ArchiveNotFound,
                    "无法读取源压缩包，文件可能已被移动或删除。", archivePath, destinationPath,
                    DestinationState.Unchanged, SourceDisposition.MissingOrChanged);
            }

            cancellationToken.ThrowIfCancellationRequested();
            stagingPath = CreateStagingPath(destinationPath);
            ArchiveExtractionResult extractionResult = await _archiveExtractor.ExtractAsync(
                archivePath, stagingPath, progress, cancellationToken).ConfigureAwait(false);
            if (!extractionResult.Success)
            {
                return ExtractionFailure(extractionResult, archivePath, destinationPath, stagingPath);
            }

            FileIdentity? identityBeforePublish = FileIdentityReader.TryRead(archivePath);
            if (identityBeforePublish is null || !originalIdentity.Equals(identityBeforePublish))
            {
                return CleanupAndResult(
                    WorkflowOutcome.ExtractionFailed,
                    ErrorCode.SourceChanged,
                    "源压缩包在解压期间发生变化，已保留源文件且未发布目标目录。",
                    archivePath,
                    destinationPath,
                    DestinationState.Unchanged,
                    SourceDisposition.MissingOrChanged,
                    stagingPath,
                    identityBeforePublish is null ? "Source file is missing." : "Source identity changed.");
            }

            if (WouldPublishOverSource(archivePath, destinationPath, stagingPath))
            {
                return CleanupAndResult(
                    WorkflowOutcome.PublishFailed,
                    ErrorCode.SourceDestinationConflict,
                    "目标目录中的内容会覆盖正在处理的源压缩包，操作已停止；源文件已保留。",
                    archivePath,
                    destinationPath,
                    DestinationState.Unchanged,
                    SourceDisposition.Retained,
                    stagingPath);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return CleanupAndResult(
                    WorkflowOutcome.Cancelled,
                    ErrorCode.Cancelled,
                    "用户已取消解压。",
                    archivePath,
                    destinationPath,
                    DestinationState.Unchanged,
                    SourceDisposition.Retained,
                    stagingPath);
            }

            bool publishingCanCancel = (_destinationPublisher as IDestinationPublisherPolicy)?.SupportsCancellation == true;
            progress?.Report(new ExtractionProgress(
                WorkflowStage.Publishing, null, 0, 0, 0, 0, publishingCanCancel, true));

            publishingStarted = true;
            DestinationPublishResult publishResult = await _destinationPublisher.PublishAsync(
                stagingPath,
                destinationPath,
                progress,
                publishingCanCancel ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
            destinationState = publishResult.DestinationState;

            bool stagingCleaned = TryDeleteStaging(stagingPath);
            string? retainedStagingPath = stagingCleaned ? null : stagingPath;

            if (publishResult.Outcome == DestinationPublishOutcome.Cancelled)
            {
                return Result(
                    WorkflowOutcome.Cancelled,
                    publishResult.ErrorCode == ErrorCode.None ? ErrorCode.DestinationPublishAborted : publishResult.ErrorCode,
                    CombineMessage(publishResult.UserMessage, retainedStagingPath is null ? null : $"临时目录清理失败，请手动处理：{retainedStagingPath}"),
                    archivePath,
                    destinationPath,
                    destinationState,
                    SourceDisposition.Retained,
                    CombineDiagnostics(publishResult.DiagnosticMessage, retainedStagingPath),
                    retainedStagingPath);
            }

            if (publishResult.Outcome == DestinationPublishOutcome.Failed)
            {
                return Result(
                    WorkflowOutcome.PublishFailed,
                    !stagingCleaned ? ErrorCode.StagingCleanupFailed : publishResult.ErrorCode,
                    CombineMessage(publishResult.UserMessage, retainedStagingPath is null ? null : $"临时目录清理失败，请手动处理：{retainedStagingPath}"),
                    archivePath,
                    destinationPath,
                    destinationState,
                    SourceDisposition.Retained,
                    CombineDiagnostics(publishResult.DiagnosticMessage, retainedStagingPath),
                    retainedStagingPath);
            }

            if (publishResult.Outcome == DestinationPublishOutcome.CompletedWithSkippedItems)
            {
                return Result(
                    WorkflowOutcome.CompletedWithSkippedItems,
                    ErrorCode.DestinationItemsSkipped,
                    CombineMessage(publishResult.UserMessage, retainedStagingPath is null ? null : $"临时目录清理失败，请手动处理：{retainedStagingPath}"),
                    archivePath,
                    destinationPath,
                    destinationState,
                    SourceDisposition.Retained,
                    CombineDiagnostics(publishResult.DiagnosticMessage, retainedStagingPath),
                    retainedStagingPath);
            }

            if (!stagingCleaned)
            {
                return Result(
                    WorkflowOutcome.PublishFailed,
                    ErrorCode.StagingCleanupFailed,
                    $"目标内容已完整发布，但临时目录清理失败，请手动处理：{stagingPath}；源压缩包已保留。",
                    archivePath,
                    destinationPath,
                    destinationState,
                    SourceDisposition.Retained,
                    retainedStagingPath,
                    retainedStagingPath);
            }

            FileIdentity? identityBeforeRecycle = FileIdentityReader.TryRead(archivePath);
            if (identityBeforeRecycle is null || !originalIdentity.Equals(identityBeforeRecycle))
            {
                progress?.Report(new ExtractionProgress(WorkflowStage.Recycling, null, 0, 0, 0, 0, false));
                return Result(
                    WorkflowOutcome.CleanupFailed,
                    ErrorCode.SourceChanged,
                    "目标内容已完整发布，但源压缩包在发布后发生变化，未执行回收；源文件已保留。",
                    archivePath,
                    destinationPath,
                    destinationState,
                    SourceDisposition.MissingOrChanged,
                    identityBeforeRecycle is null ? "Source file is missing." : "Source identity changed.");
            }

            progress?.Report(new ExtractionProgress(WorkflowStage.Recycling, null, 0, 0, 0, 0, false));
            CleanupResult cleanupResult;
            try
            {
                cleanupResult = await _cleanupService.MoveToRecycleBinAsync(archivePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Result(
                    WorkflowOutcome.CleanupFailed,
                    ErrorCode.Unexpected,
                    "文件已完整发布，但源压缩包无法移入回收站，源文件已保留。",
                    archivePath,
                    destinationPath,
                    destinationState,
                    File.Exists(archivePath) ? SourceDisposition.Retained : SourceDisposition.MissingOrChanged,
                    ex.ToString());
            }
            if (!cleanupResult.Success)
            {
                SourceDisposition disposition = File.Exists(archivePath)
                    ? SourceDisposition.Retained
                    : SourceDisposition.MissingOrChanged;
                return Result(
                    WorkflowOutcome.CleanupFailed,
                    cleanupResult.ErrorCode == ErrorCode.None ? ErrorCode.RecycleFailed : cleanupResult.ErrorCode,
                    "文件已完整发布，但源压缩包无法移入回收站，源文件已保留。",
                    archivePath,
                    destinationPath,
                    destinationState,
                    disposition,
                    cleanupResult.DiagnosticMessage);
            }

            progress?.Report(new ExtractionProgress(WorkflowStage.Completed, null, 0, 0, 0, 0, false));
            return Result(WorkflowOutcome.Completed, ErrorCode.None, "解压并回收完成。", archivePath, destinationPath,
                DestinationState.Completed, SourceDisposition.Recycled);
        }
        catch (OperationCanceledException)
        {
            if (archivePath is null || destinationPath is null)
            {
                return Result(WorkflowOutcome.Cancelled, ErrorCode.Cancelled, "用户已取消操作.",
                    archivePath ?? request?.ArchivePath ?? string.Empty,
                    destinationPath ?? request?.DestinationPath ?? string.Empty,
                    DestinationState.Unchanged, SourceDisposition.Retained);
            }

            return CleanupAndResult(
                WorkflowOutcome.Cancelled,
                publishingStarted ? ErrorCode.DestinationPublishAborted : ErrorCode.Cancelled,
                publishingStarted ? "Windows 文件操作已取消，目标目录可能包含部分内容；源压缩包已保留。" : "用户已取消解压。",
                archivePath,
                destinationPath,
                publishingStarted ? DestinationState.PartiallyModified : DestinationState.Unchanged,
                SourceDisposition.Retained,
                stagingPath);
        }
        catch (ValidationException ex)
        {
            return Result(WorkflowOutcome.ValidationFailed, ex.ErrorCode, ex.UserMessage,
                archivePath ?? request?.ArchivePath ?? string.Empty,
                destinationPath ?? request?.DestinationPath ?? string.Empty,
                DestinationState.Unchanged, SourceDisposition.Retained, ex.ToString());
        }
        catch (Exception ex)
        {
            if (archivePath is not null && destinationPath is not null)
            {
                return CleanupAndResult(
                    publishingStarted ? WorkflowOutcome.PublishFailed : WorkflowOutcome.ExtractionFailed,
                    publishingStarted ? ErrorCode.PublishFailure : ErrorCode.Unexpected,
                    publishingStarted
                        ? "Windows 文件操作发生未预期错误，目标目录可能包含部分内容；源压缩包已保留。"
                        : "操作过程中发生未预期错误，源压缩包已保留。",
                    archivePath,
                    destinationPath,
                    publishingStarted ? DestinationState.PartiallyModified : destinationState,
                    SourceDisposition.Retained,
                    stagingPath,
                    ex.ToString());
            }

            return Result(WorkflowOutcome.ExtractionFailed, ErrorCode.Unexpected,
                "操作过程中发生未预期错误。", archivePath ?? request?.ArchivePath ?? string.Empty,
                destinationPath ?? request?.DestinationPath ?? string.Empty,
                destinationState, SourceDisposition.Retained, ex.ToString());
        }
    }

    private DestinationPublishResult? ValidateDestinationPolicy(string destinationPath)
    {
        if (!Directory.Exists(destinationPath)
            || _destinationPublisher is not AtomicDirectoryPublisher)
        {
            return null;
        }

        return new DestinationPublishResult(
            DestinationPublishOutcome.Failed,
            ErrorCode.DestinationAlreadyExists,
            "目标目录已存在，命令行模式不会合并或覆盖现有内容。",
            DestinationState.Unchanged,
            0,
            0,
            1);
    }

    private (string ArchivePath, string DestinationPath) ValidateRequest(ExtractAndDeleteRequest? request)
    {
        if (request is null)
        {
            throw new ValidationException(ErrorCode.InvalidArchivePath, "未提供源压缩包路径。");
        }

        string archivePath = GetFullPathOrThrow(request.ArchivePath, ErrorCode.InvalidArchivePath, "源压缩包路径无效。");
        string destinationPath = GetFullPathOrThrow(request.DestinationPath, ErrorCode.InvalidDestination, "准确目标目录路径无效。");
        if (!SupportedArchiveFormats.TryResolve(archivePath, out _))
        {
            throw new ValidationException(ErrorCode.UnsupportedFormat, "当前版本仅支持 ZIP、7Z、RAR 和 TAR 压缩包。");
        }

        if (!File.Exists(archivePath))
        {
            throw new ValidationException(ErrorCode.ArchiveNotFound, "源压缩包不存在。");
        }

        if (File.Exists(destinationPath))
        {
            throw new ValidationException(ErrorCode.InvalidDestination, "目标路径是文件，不是文件夹。");
        }

        DestinationPublishResult? policyResult = ValidateDestinationPolicy(destinationPath);
        if (policyResult is not null)
        {
            throw new ValidationException(policyResult.ErrorCode, policyResult.UserMessage);
        }

        if (FindExistingDirectory(destinationPath) is null)
        {
            throw new ValidationException(ErrorCode.InvalidDestination, "目标路径没有可用的本地或 UNC 父目录。");
        }

        return (archivePath, destinationPath);
    }

    private static string GetFullPathOrThrow(string? path, ErrorCode errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ValidationException(errorCode, message);
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Path.IsPathFullyQualified(fullPath))
            {
                throw new ValidationException(errorCode, message);
            }
            return fullPath;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ValidationException(errorCode, message, ex);
        }
    }

    private static string? FindExistingDirectory(string path)
    {
        string current = path;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    return (attributes & FileAttributes.ReparsePoint) == 0 ? current : null;
                }
                catch
                {
                    return null;
                }
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            current = parent;
        }

        return null;
    }

    private static string CreateStagingPath(string destinationPath)
    {
        string? parent = Path.GetDirectoryName(destinationPath);
        string? root = FindExistingDirectory(parent ?? destinationPath);
        if (root is null)
        {
            root = Path.Combine(Path.GetTempPath(), "ExtractAndDelete", "staging");
            Directory.CreateDirectory(root);
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            string candidate = Path.Combine(root, $"{StagingPrefix}{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to allocate a unique staging path.");
    }

    private static bool WouldPublishOverSource(string archivePath, string destinationPath, string stagingPath)
    {
        string archiveFull = Path.GetFullPath(archivePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string entry in Directory.EnumerateFileSystemEntries(stagingPath, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch
            {
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                continue;
            }

            string relative = Path.GetRelativePath(stagingPath, entry);
            string output = Path.GetFullPath(Path.Combine(destinationPath, relative));
            if (string.Equals(
                output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                archiveFull,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ExtractAndDeleteResult ExtractionFailure(
        ArchiveExtractionResult extractionResult,
        string archivePath,
        string destinationPath,
        string stagingPath)
    {
        bool unsafeTermination = extractionResult.StagingCleanupState == StagingCleanupState.RetainedForSafety;
        bool cleanupFailed = !unsafeTermination && !TryDeleteStaging(stagingPath);
        string? retained = unsafeTermination || cleanupFailed ? extractionResult.StagingPath ?? stagingPath : null;
        return Result(
            extractionResult.ErrorCode == ErrorCode.Cancelled ? WorkflowOutcome.Cancelled : WorkflowOutcome.ExtractionFailed,
            cleanupFailed ? ErrorCode.StagingCleanupFailed : extractionResult.ErrorCode,
            unsafeTermination
                ? $"{extractionResult.UserMessage} 7-Zip 进程未能确认停止，请手动处理临时目录：{retained}"
                : cleanupFailed
                ? $"{extractionResult.UserMessage} 临时目录清理失败，请手动处理：{retained}"
                : extractionResult.UserMessage,
            archivePath,
            destinationPath,
            DestinationState.Unchanged,
            SourceDisposition.Retained,
            CombineDiagnostics(extractionResult.DiagnosticMessage, retained),
            retained);
    }

    private static ExtractAndDeleteResult CleanupAndResult(
        WorkflowOutcome outcome,
        ErrorCode errorCode,
        string message,
        string archivePath,
        string destinationPath,
        DestinationState destinationState,
        SourceDisposition disposition,
        string? stagingPath,
        string? diagnostic = null)
    {
        bool cleaned = stagingPath is null || TryDeleteStaging(stagingPath);
        string? retained = cleaned ? null : stagingPath;
        return Result(
            outcome,
            cleaned ? errorCode : ErrorCode.StagingCleanupFailed,
            cleaned ? message : $"{message} 临时目录清理失败，请手动处理：{stagingPath}",
            archivePath,
            destinationPath,
            destinationState,
            disposition,
            CombineDiagnostics(diagnostic, retained),
            retained);
    }

    private static bool TryDeleteStaging(string stagingPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(stagingPath);
            string name = Path.GetFileName(fullPath);
            if (!name.StartsWith(StagingPrefix, StringComparison.Ordinal) || !name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                return false;
            }

            if (!Directory.Exists(fullPath))
            {
                return !File.Exists(fullPath);
            }

            FileAttributes rootAttributes = File.GetAttributes(fullPath);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0 || (rootAttributes & FileAttributes.Directory) == 0)
            {
                return false;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(fullPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.None,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false
            }))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!TryDeleteDirectory(entry)) return false;
                }
                else
                {
                    File.Delete(entry);
                }
            }

            Directory.Delete(fullPath, false);
            return !Directory.Exists(fullPath) && !File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
        {
            return false;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        }))
        {
            FileAttributes entryAttributes = File.GetAttributes(entry);
            if ((entryAttributes & FileAttributes.ReparsePoint) != 0) return false;
            if ((entryAttributes & FileAttributes.Directory) != 0)
            {
                if (!TryDeleteDirectory(entry)) return false;
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(path, false);
        return !Directory.Exists(path);
    }

    private static ExtractAndDeleteResult Result(
        WorkflowOutcome outcome,
        ErrorCode errorCode,
        string message,
        string archivePath,
        string destinationPath,
        DestinationState destinationState,
        SourceDisposition sourceDisposition,
        string? diagnostic = null,
        string? stagingPath = null) =>
        new(outcome, errorCode, message, archivePath, destinationPath, destinationState, sourceDisposition, diagnostic, stagingPath);

    private static string CombineMessage(string first, string? second) =>
        string.IsNullOrWhiteSpace(second) ? first : $"{first} {second}";

    private static string? CombineDiagnostics(params string?[] messages)
    {
        string[] nonEmpty = messages.Where(static message => !string.IsNullOrWhiteSpace(message)).Cast<string>().ToArray();
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
