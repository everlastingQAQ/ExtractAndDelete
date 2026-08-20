using System.Buffers;
using System.IO.Compression;

namespace ExtractAndDelete.Core;

public interface IArchiveExtractor
{
    Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string stagingPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class ArchiveExtractor : IArchiveExtractor
{
    private const int BufferSize = 128 * 1024;

    public async Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string stagingPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using FileStream source = new(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using ZipArchive archive = new(source, ZipArchiveMode.Read, leaveOpen: false);
            progress?.Report(new ExtractionProgress(
                WorkflowStage.Scanning,
                null,
                0,
                archive.Entries.Count,
                0,
                0,
                CanCancel: true));

            IReadOnlyList<ValidatedEntry> entries = ValidateEntries(
                archive,
                stagingPath,
                progress,
                cancellationToken,
                out long totalBytes);
            cancellationToken.ThrowIfCancellationRequested();

            string? root = Path.GetPathRoot(stagingPath);
            long availableBytes = string.IsNullOrEmpty(root)
                ? 0
                : new DriveInfo(root).AvailableFreeSpace;
            if (availableBytes < totalBytes)
            {
                return Failure(
                    ErrorCode.InsufficientDiskSpace,
                    "目标磁盘可用空间不足。",
                    $"Available bytes: {availableBytes}; required bytes: {totalBytes}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(stagingPath);
            EnsureNoReparsePoints(stagingPath);
            int completedEntries = 0;
            long completedBytes = 0;

            foreach (ValidatedEntry validatedEntry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = validatedEntry.DestinationPath;

                if (validatedEntry.IsDirectory)
                {
                    Directory.CreateDirectory(destination);
                    EnsureNoReparsePoints(destination);
                }
                else
                {
                    string? parent = Path.GetDirectoryName(destination);
                    if (string.IsNullOrEmpty(parent))
                    {
                        return Failure(
                            ErrorCode.UnsafeArchiveEntry,
                            "压缩包包含无效的文件路径。",
                            validatedEntry.Entry.FullName);
                    }

                    Directory.CreateDirectory(parent);
                    EnsureNoReparsePoints(parent);
                    if (File.Exists(destination)
                        && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new ArchiveValidationException(
                            ErrorCode.UnsafeArchiveEntry,
                            "目标路径包含不允许的 reparse-point。",
                            validatedEntry.Entry.FullName);
                    }
                    await CopyEntryAsync(
                        validatedEntry.Entry,
                        destination,
                        completedEntries,
                        entries.Count,
                        completedBytes,
                        totalBytes,
                        progress,
                        cancellationToken);

                    TryRestoreLastWriteTime(destination, validatedEntry.Entry.LastWriteTime);
                    completedBytes = checked(completedBytes + validatedEntry.Entry.Length);
                }

                completedEntries++;
                progress?.Report(new ExtractionProgress(
                    WorkflowStage.Extracting,
                    validatedEntry.Entry.FullName,
                    completedEntries,
                    entries.Count,
                    completedBytes,
                    totalBytes,
                    CanCancel: true));
            }

            return new ArchiveExtractionResult(true, ErrorCode.None, "解压完成。");
        }
        catch (OperationCanceledException)
        {
            return Failure(ErrorCode.Cancelled, "用户已取消解压。", null);
        }
        catch (ArchiveValidationException ex)
        {
            return Failure(ex.ErrorCode, ex.UserMessage, ex.ToString());
        }
        catch (InvalidDataException ex)
        {
            return Failure(
                ErrorCode.ArchiveUnreadable,
                "无法读取 ZIP，文件可能已损坏或受密码保护。",
                ex.ToString());
        }
        catch (NotSupportedException ex)
        {
            return Failure(
                ErrorCode.ArchiveUnreadable,
                "无法读取 ZIP，文件可能已损坏或受密码保护。",
                ex.ToString());
        }
        catch (OverflowException ex)
        {
            return Failure(
                ErrorCode.ArchiveSizeOverflow,
                "压缩包声明的解压大小超出可处理范围。",
                ex.ToString());
        }
        catch (IOException ex)
        {
            return Failure(
                ErrorCode.ExtractionIoFailure,
                "解压过程中发生文件 I/O 错误。",
                ex.ToString());
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(
                ErrorCode.ExtractionIoFailure,
                "没有足够权限读取压缩包或写入目标目录。",
                ex.ToString());
        }
        catch (Exception ex)
        {
            return Failure(ErrorCode.Unexpected, "解压过程中发生未预期错误。", ex.ToString());
        }
    }

    private static async Task CopyEntryAsync(
        ZipArchiveEntry entry,
        string destination,
        int completedEntries,
        int totalEntries,
        long completedBytes,
        long totalBytes,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using Stream input = entry.Open();
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long entryBytes = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                entryBytes = checked(entryBytes + read);
                if (entryBytes > entry.Length)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' produced more data than declared.");
                }

                progress?.Report(new ExtractionProgress(
                    WorkflowStage.Extracting,
                    entry.FullName,
                    completedEntries,
                    totalEntries,
                    completedBytes + entryBytes,
                    totalBytes,
                    CanCancel: true));
            }

            if (entryBytes != entry.Length)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' ended before its declared length.");
            }

            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IReadOnlyList<ValidatedEntry> ValidateEntries(
        ZipArchive archive,
        string stagingPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken,
        out long totalBytes)
    {
        string fullStagingPath = Path.GetFullPath(stagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string stagingPrefix = fullStagingPath + Path.DirectorySeparatorChar;
        var seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ValidatedEntry>(archive.Entries.Count);
        totalBytes = 0;
        int scannedEntries = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSymbolicLink(entry))
            {
                throw new ArchiveValidationException(
                    ErrorCode.UnsafeArchiveEntry,
                    "压缩包包含不允许的符号链接或 reparse 条目。",
                    entry.FullName);
            }

            string normalizedRelative = NormalizeEntryPath(entry.FullName);
            string destinationPath = Path.GetFullPath(
                Path.Combine(fullStagingPath, normalizedRelative));

            if (!destinationPath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArchiveValidationException(
                    ErrorCode.UnsafeArchiveEntry,
                    "压缩包包含越界路径。",
                    entry.FullName);
            }

            bool isDirectory = entry.FullName
                .Replace('\\', '/')
                .EndsWith("/", StringComparison.Ordinal);

            if (seen.TryGetValue(normalizedRelative, out bool previousIsDirectory))
            {
                throw new ArchiveValidationException(
                    previousIsDirectory == isDirectory
                        ? ErrorCode.DuplicateArchiveEntry
                        : ErrorCode.ArchiveEntryConflict,
                    previousIsDirectory == isDirectory
                        ? "压缩包包含重复路径。"
                        : "压缩包包含文件与目录同名冲突。",
                    entry.FullName);
            }

            seen.Add(normalizedRelative, isDirectory);

            if (isDirectory)
            {
                directories.Add(normalizedRelative);
            }
            else
            {
                files.Add(normalizedRelative);
                totalBytes = checked(totalBytes + entry.Length);
            }

            result.Add(new ValidatedEntry(entry, destinationPath, isDirectory));
            scannedEntries++;
            progress?.Report(new ExtractionProgress(
                WorkflowStage.Scanning,
                entry.FullName,
                scannedEntries,
                archive.Entries.Count,
                0,
                0,
                CanCancel: true));
        }

        foreach (string file in files)
        {
            if (directories.Contains(file) || HasFileAncestor(file, files))
            {
                throw new ArchiveValidationException(
                    ErrorCode.ArchiveEntryConflict,
                    "压缩包包含文件与目录路径冲突。",
                    file);
            }
        }

        foreach (string directory in directories)
        {
            if (files.Contains(directory) || HasFileAncestor(directory, files))
            {
                throw new ArchiveValidationException(
                    ErrorCode.ArchiveEntryConflict,
                    "压缩包包含文件与目录路径冲突。",
                    directory);
            }
        }

        return result;
    }

    private static bool HasFileAncestor(string path, HashSet<string> files)
    {
        string? parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            if (files.Contains(parent))
            {
                return true;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return false;
    }

    private static string NormalizeEntryPath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains('\0'))
        {
            throw new ArchiveValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包包含空或无效路径。",
                entryName);
        }

        string slashPath = entryName.Replace('\\', '/');
        if (slashPath.StartsWith("/", StringComparison.Ordinal)
            || slashPath.Contains(":/", StringComparison.Ordinal)
            || slashPath.Contains(':', StringComparison.Ordinal))
        {
            throw new ArchiveValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包包含绝对路径。",
                entryName);
        }

        string[] parts = slashPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new ArchiveValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包包含越界路径。",
                entryName);
        }

        char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
        foreach (string part in parts)
        {
            if (part.EndsWith(".", StringComparison.Ordinal)
                || part.EndsWith(" ", StringComparison.Ordinal)
                || part.IndexOfAny(invalidFileNameChars) >= 0
                || IsReservedDeviceName(part))
            {
                throw new ArchiveValidationException(
                    ErrorCode.UnsafeArchiveEntry,
                    "压缩包包含 Windows 不支持的文件名。",
                    entryName);
            }
        }

        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private static bool IsReservedDeviceName(string part)
    {
        string name = Path.GetFileNameWithoutExtension(part);
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4
                && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                && name[3] is >= '1' and <= '9')
            || (name.Length == 4
                && name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
                && name[3] is >= '1' and <= '9');
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        const int ReparsePointAttribute = 0x0400;
        return (unixMode & 0xF000) == 0xA000
            || (entry.ExternalAttributes & ReparsePointAttribute) != 0;
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArchiveValidationException(
                        ErrorCode.UnsafeArchiveEntry,
                        "目标路径包含不允许的 reparse-point。",
                        current);
                }
            }
            catch (FileNotFoundException)
            {
                // The parent may not exist yet; its first existing ancestor is checked.
            }
            catch (DirectoryNotFoundException)
            {
                // The parent may not exist yet; its first existing ancestor is checked.
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static void TryRestoreLastWriteTime(string path, DateTimeOffset timestamp)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Some ZIP writers emit timestamps outside the Windows range.
        }
    }

    private static ArchiveExtractionResult Failure(
        ErrorCode errorCode,
        string userMessage,
        string? diagnosticMessage)
    {
        return new ArchiveExtractionResult(false, errorCode, userMessage, diagnosticMessage);
    }

    private sealed record ValidatedEntry(
        ZipArchiveEntry Entry,
        string DestinationPath,
        bool IsDirectory);

    private sealed class ArchiveValidationException : Exception
    {
        public ArchiveValidationException(ErrorCode errorCode, string message, string? entryName)
            : base($"{message} Entry: {entryName}")
        {
            ErrorCode = errorCode;
            UserMessage = message;
        }

        public ErrorCode ErrorCode { get; }

        public string UserMessage { get; }
    }
}
