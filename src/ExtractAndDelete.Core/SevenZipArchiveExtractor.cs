using System.Globalization;
using System.Text.RegularExpressions;

namespace ExtractAndDelete.Core;

public sealed class SevenZipArchiveExtractor : IArchiveExtractor
{
    private const int BufferSize = 128 * 1024;
    private static readonly Regex PercentagePattern =
        new(@"^\s*(\d{1,3})%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SevenZipToolProvider _toolProvider;
    private readonly SevenZipProcessRunner _processRunner;

    public SevenZipArchiveExtractor(
        SevenZipToolProvider toolProvider,
        SevenZipProcessRunner? processRunner = null)
    {
        _toolProvider = toolProvider
            ?? throw new ArgumentNullException(nameof(toolProvider));
        _processRunner = processRunner ?? new SevenZipProcessRunner();
    }

    public async Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string stagingPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArchiveFormatDescriptor format;
        string? fullStagingPath = null;
        try
        {
            if (!SupportedArchiveFormats.TryResolve(archivePath, out format))
            {
                return Failure(
                    ErrorCode.UnsupportedFormat,
                    "当前版本仅支持 ZIP、7Z、RAR 和 TAR 压缩包。",
                    archivePath,
                    StagingCleanupState.NotCreated,
                    null);
            }

            fullStagingPath = Path.GetFullPath(stagingPath);
            PrepareEmptyStaging(fullStagingPath);
            SevenZipToolPaths tools = _toolProvider.ResolveAndVerify();
            cancellationToken.ThrowIfCancellationRequested();

            await using FileStream sourceLock = new(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            SevenZipListingParser parser = new();
            progress?.Report(new ExtractionProgress(
                WorkflowStage.Scanning,
                null,
                0,
                0,
                0,
                0,
                CanCancel: true,
                IsIndeterminate: true));

            SevenZipProcessResult listingProcess = await _processRunner.RunAsync(
                tools,
                BuildListArguments(format, archivePath),
                cancellationToken,
                line =>
                {
                    parser.AppendLine(line);
                    if (parser.EntryCount > 0)
                    {
                        progress?.Report(new ExtractionProgress(
                            WorkflowStage.Scanning,
                            null,
                            parser.EntryCount,
                            0,
                            0,
                            0,
                            CanCancel: true,
                            IsIndeterminate: true));
                    }
                });

            if (!listingProcess.TerminationConfirmed)
            {
                return Failure(
                    ErrorCode.ArchiveEngineTerminationFailure,
                    "无法确认 7-Zip 已停止，已保留临时目录以确保安全。",
                    CombineDiagnostics(listingProcess),
                    StagingCleanupState.RetainedForSafety,
                    fullStagingPath,
                    listingProcess.ExitCode);
            }

            if (listingProcess.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    ErrorCode.Cancelled,
                    "用户已取消解压。",
                    CombineDiagnostics(listingProcess),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    listingProcess.ExitCode);
            }

            SevenZipArchiveListing listing;
            try
            {
                listing = parser.Complete();
            }
            catch (SevenZipProtocolException ex)
            {
                return Failure(
                    ErrorCode.ArchiveEngineProtocolFailure,
                    "无法解析压缩包扫描结果。",
                    CombineDiagnostics(listingProcess, ex.ToString()),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    listingProcess.ExitCode);
            }

            if (listingProcess.ExitCode != 0)
            {
                return Failure(
                    MapProcessFailure(listingProcess, cancellationToken),
                    MapProcessMessage(listingProcess),
                    CombineDiagnostics(listingProcess),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    listingProcess.ExitCode);
            }

            ValidatedArchiveManifest manifest;
            try
            {
                manifest = ArchiveEntryValidator.Validate(format, listing, fullStagingPath);
            }
            catch (ArchiveManifestValidationException ex)
            {
                return Failure(
                    ex.ErrorCode,
                    ex.UserMessage,
                    ex.ToString(),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    listingProcess.ExitCode);
            }

            long availableBytes = GetAvailableBytes(fullStagingPath);
            if (availableBytes < manifest.TotalBytes)
            {
                return Failure(
                    ErrorCode.InsufficientDiskSpace,
                    "目标磁盘可用空间不足。",
                    $"Available bytes: {availableBytes}; required bytes: {manifest.TotalBytes}.",
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    listingProcess.ExitCode);
            }

            cancellationToken.ThrowIfCancellationRequested();
            int lastPercentage = 0;
            string? currentEntry = null;
            progress?.Report(new ExtractionProgress(
                WorkflowStage.Extracting,
                null,
                0,
                manifest.Entries.Count,
                0,
                manifest.TotalBytes,
                CanCancel: true));

            SevenZipProcessResult extractionProcess = await _processRunner.RunAsync(
                tools,
                BuildExtractArguments(format, archivePath, fullStagingPath),
                cancellationToken,
                line =>
                {
                    currentEntry = ParseEntryName(line) ?? currentEntry;
                    ReportProgress(
                        progress,
                        currentEntry,
                        manifest,
                        lastPercentage,
                        ref lastPercentage);
                },
                line =>
                {
                    Match match = PercentagePattern.Match(line);
                    if (match.Success
                        && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percentage))
                    {
                        lastPercentage = Math.Clamp(Math.Max(lastPercentage, percentage), 0, 100);
                        ReportProgress(
                            progress,
                            currentEntry,
                            manifest,
                            lastPercentage,
                            ref lastPercentage);
                    }
                });

            if (!extractionProcess.TerminationConfirmed)
            {
                return Failure(
                    ErrorCode.ArchiveEngineTerminationFailure,
                    "无法确认 7-Zip 已停止，已保留临时目录以确保安全。",
                    CombineDiagnostics(extractionProcess),
                    StagingCleanupState.RetainedForSafety,
                    fullStagingPath,
                    extractionProcess.ExitCode);
            }

            if (extractionProcess.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    ErrorCode.Cancelled,
                    "用户已取消解压。",
                    CombineDiagnostics(extractionProcess),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    extractionProcess.ExitCode);
            }

            if (extractionProcess.ExitCode != 0)
            {
                return Failure(
                    MapProcessFailure(extractionProcess, cancellationToken),
                    MapProcessMessage(extractionProcess),
                    CombineDiagnostics(extractionProcess),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    extractionProcess.ExitCode);
            }

            try
            {
                StagingVerifier.Verify(fullStagingPath, manifest);
            }
            catch (StagingVerificationException ex)
            {
                return Failure(
                    ErrorCode.ArchiveVerificationFailure,
                    "解压结果验证失败，未发布最终目录。",
                    ex.ToString(),
                    StagingCleanupState.ReadyForPublish,
                    fullStagingPath,
                    extractionProcess.ExitCode);
            }

            progress?.Report(new ExtractionProgress(
                WorkflowStage.Extracting,
                null,
                manifest.Entries.Count,
                manifest.Entries.Count,
                manifest.TotalBytes,
                manifest.TotalBytes,
                CanCancel: true));

            return new ArchiveExtractionResult(
                true,
                ErrorCode.None,
                "解压完成。",
                CombineDiagnostics(listingProcess)
                + Environment.NewLine
                + CombineDiagnostics(extractionProcess),
                StagingCleanupState.ReadyForPublish,
                fullStagingPath,
                extractionProcess.ExitCode);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                ErrorCode.Cancelled,
                "用户已取消解压。",
                null,
                StagingCleanupState.ReadyForPublish,
                fullStagingPath);
        }
        catch (SevenZipEngineException ex)
        {
            return Failure(
                ex.ErrorCode,
                ex.UserMessage,
                ex.DiagnosticMessage,
                StagingCleanupState.NotCreated,
                fullStagingPath);
        }
        catch (ArchiveManifestValidationException ex)
        {
            return Failure(
                ex.ErrorCode,
                ex.UserMessage,
                ex.ToString(),
                StagingCleanupState.ReadyForPublish,
                fullStagingPath);
        }
        catch (IOException ex)
        {
            return Failure(
                ErrorCode.ExtractionIoFailure,
                "解压过程中发生文件 I/O 错误。",
                ex.ToString(),
                StagingCleanupState.ReadyForPublish,
                fullStagingPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(
                ErrorCode.ExtractionIoFailure,
                "没有足够权限读取压缩包或写入临时目录。",
                ex.ToString(),
                StagingCleanupState.ReadyForPublish,
                fullStagingPath);
        }
        catch (Exception ex)
        {
            return Failure(
                ErrorCode.Unexpected,
                "解压过程中发生未预期错误。",
                ex.ToString(),
                StagingCleanupState.ReadyForPublish,
                fullStagingPath);
        }
    }

    private static void PrepareEmptyStaging(string path)
    {
        if (File.Exists(path))
        {
            throw new IOException("The staging path is occupied by a file.");
        }

        Directory.CreateDirectory(path);
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || (attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException("The staging path is not a normal directory.");
        }

        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new IOException("The staging directory must be empty.");
        }
    }

    private static long GetAvailableBytes(string path)
    {
        string? root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
    }

    private static IReadOnlyList<string> BuildListArguments(
        ArchiveFormatDescriptor format,
        string archivePath) =>
        new[]
        {
            "l",
            "-slt",
            "-sccUTF-8",
            "-bso1",
            "-bse2",
            "-bsp0",
            $"-t{format.SevenZipType}",
            "--",
            archivePath
        };

    private static IReadOnlyList<string> BuildExtractArguments(
        ArchiveFormatDescriptor format,
        string archivePath,
        string stagingPath) =>
        new[]
        {
            "x",
            $"-t{format.SevenZipType}",
            "-sccUTF-8",
            "-y",
            "-aoa",
            "-sns-",
            "-snld0",
            "-bb1",
            "-bso1",
            "-bse2",
            "-bsp2",
            $"-o{stagingPath}",
            "--",
            archivePath
        };

    private static string? ParseEntryName(string line)
    {
        if (line.StartsWith("Extracting ", StringComparison.OrdinalIgnoreCase))
        {
            return line[11..].Trim();
        }

        if (line.StartsWith("- ", StringComparison.Ordinal))
        {
            return line[2..].Trim();
        }

        return null;
    }

    private static void ReportProgress(
        IProgress<ExtractionProgress>? progress,
        string? currentEntry,
        ValidatedArchiveManifest manifest,
        int percentage,
        ref int lastPercentage)
    {
        lastPercentage = Math.Clamp(Math.Max(lastPercentage, percentage), 0, 100);
        long completedBytes = manifest.TotalBytes == 0
            ? 0
            : (long)Math.Clamp(
                Math.Round(manifest.TotalBytes * (lastPercentage / 100d)),
                0,
                manifest.TotalBytes);
        progress?.Report(new ExtractionProgress(
            WorkflowStage.Extracting,
            currentEntry,
            0,
            manifest.Entries.Count,
            completedBytes,
            manifest.TotalBytes,
            CanCancel: true));
    }

    private static ErrorCode MapProcessFailure(
        SevenZipProcessResult result,
        CancellationToken cancellationToken)
    {
        if (result.ExitCode == 8)
        {
            return ErrorCode.InsufficientMemory;
        }

        if (result.ExitCode == 255 && cancellationToken.IsCancellationRequested)
        {
            return ErrorCode.Cancelled;
        }

        string diagnostics = CombineDiagnostics(result);
        if (diagnostics.Contains("password", StringComparison.OrdinalIgnoreCase)
            || diagnostics.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCode.ArchiveEncrypted;
        }

        if (diagnostics.Contains("volume", StringComparison.OrdinalIgnoreCase)
            && diagnostics.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCode.MultiVolumeArchiveNotSupported;
        }

        return ErrorCode.ArchiveUnreadable;
    }

    private static string MapProcessMessage(SevenZipProcessResult result) =>
        MapProcessFailure(result, CancellationToken.None) switch
        {
            ErrorCode.ArchiveEncrypted => "此压缩包已加密，当前版本不支持输入密码。",
            ErrorCode.MultiVolumeArchiveNotSupported => "当前版本不支持分卷压缩包。",
            ErrorCode.InsufficientMemory => "解压引擎内存不足，未发布最终目录。",
            _ => "无法读取或解压压缩包，文件可能已损坏。"
        };

    private static string CombineDiagnostics(
        SevenZipProcessResult result,
        string? extra = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.StandardOutputTail))
        {
            parts.Add($"7-Zip stdout tail:\n{result.StandardOutputTail}");
        }

        if (!string.IsNullOrWhiteSpace(result.StandardErrorTail))
        {
            parts.Add($"7-Zip stderr tail:\n{result.StandardErrorTail}");
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            parts.Add(extra);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static ArchiveExtractionResult Failure(
        ErrorCode errorCode,
        string userMessage,
        string? diagnosticMessage,
        StagingCleanupState stagingCleanupState,
        string? stagingPath,
        int? engineExitCode = null) =>
        new(
            false,
            errorCode,
            userMessage,
            diagnosticMessage,
            stagingCleanupState,
            stagingPath,
            engineExitCode);
}
