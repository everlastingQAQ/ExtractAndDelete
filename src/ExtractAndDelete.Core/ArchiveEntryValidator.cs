using System.Globalization;

namespace ExtractAndDelete.Core;

public sealed record ValidatedArchiveEntry(
    string EntryPath,
    string DestinationPath,
    bool IsDirectory,
    long Size,
    SevenZipArchiveEntry SourceEntry);

public sealed record ValidatedArchiveManifest(
    ArchiveFormatDescriptor Format,
    IReadOnlyList<ValidatedArchiveEntry> Entries,
    IReadOnlySet<string> Files,
    IReadOnlySet<string> Directories,
    long TotalBytes);

public static class ArchiveEntryValidator
{
    private const int MaximumEntries = 1_000_000;
    private const int MaximumSegmentLength = 255;
    private const int MaximumRelativePathLength = 32_000;

    public static ValidatedArchiveManifest Validate(
        ArchiveFormatDescriptor format,
        SevenZipArchiveListing listing,
        string stagingPath)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);

        if (!IsExpectedArchiveType(format, listing.ArchiveType))
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.ArchiveUnreadable,
                "压缩包格式与文件扩展名不一致。",
                listing.ArchiveType);
        }

        if (listing.Volumes > 1 || listing.Entries.Any(value => value.VolumeIndex.GetValueOrDefault() > 0))
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.MultiVolumeArchiveNotSupported,
                "当前版本不支持分卷压缩包。",
                listing.ArchiveType);
        }

        if (listing.Entries.Count > MaximumEntries)
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.ArchiveTooComplex,
                "压缩包条目数量超过当前版本的安全上限。",
                listing.Entries.Count.ToString(CultureInfo.InvariantCulture));
        }

        string fullStagingPath = Path.GetFullPath(stagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string stagingPrefix = fullStagingPath + Path.DirectorySeparatorChar;
        var seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validated = new List<ValidatedArchiveEntry>(listing.Entries.Count);
        long totalBytes = 0;

        foreach (SevenZipArchiveEntry entry in listing.Entries)
        {
            if (entry.IsEncrypted)
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.ArchiveEncrypted,
                    "此压缩包已加密，当前版本不支持输入密码。",
                    entry.EntryPath);
            }

            if (entry.IsAntiItem || entry.IsAlternateStream || entry.IsReparsePoint
                || entry.SymbolicLink is not null || entry.HardLink is not null)
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.UnsupportedArchiveEntryType,
                    "压缩包包含当前版本不允许的链接、设备或其他特殊条目。",
                    entry.EntryPath);
            }

            string normalizedRelativePath = NormalizeEntryPath(entry.EntryPath);
            if (seen.TryGetValue(normalizedRelativePath, out bool previousIsDirectory))
            {
                throw new ArchiveManifestValidationException(
                    previousIsDirectory == entry.IsDirectory
                        ? ErrorCode.DuplicateArchiveEntry
                        : ErrorCode.ArchiveEntryConflict,
                    previousIsDirectory == entry.IsDirectory
                        ? "压缩包包含重复路径。"
                        : "压缩包包含文件与目录同名冲突。",
                    entry.EntryPath);
            }

            string destinationPath;
            try
            {
                destinationPath = Path.GetFullPath(
                    Path.Combine(fullStagingPath, normalizedRelativePath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.UnsafeArchiveEntry,
                    "压缩包包含无法在 Windows 上创建的路径。",
                    entry.EntryPath,
                    ex);
            }

            if (!destinationPath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.UnsafeArchiveEntry,
                    "压缩包包含越界路径。",
                    entry.EntryPath);
            }

            seen.Add(normalizedRelativePath, entry.IsDirectory);
            if (entry.IsDirectory)
            {
                directories.Add(normalizedRelativePath);
            }
            else
            {
                files.Add(normalizedRelativePath);
                try
                {
                    totalBytes = checked(totalBytes + entry.Size);
                }
                catch (OverflowException ex)
                {
                    throw new ArchiveManifestValidationException(
                        ErrorCode.ArchiveSizeOverflow,
                        "压缩包声明的解压大小超出可处理范围。",
                        entry.EntryPath,
                        ex);
                }
            }

            validated.Add(new ValidatedArchiveEntry(
                normalizedRelativePath,
                destinationPath,
                entry.IsDirectory,
                entry.Size,
                entry));
        }

        foreach (string file in files)
        {
            if (HasFileAncestor(file, files))
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.ArchiveEntryConflict,
                    "压缩包包含文件与目录路径冲突。",
                    file);
            }
        }

        foreach (string directory in directories)
        {
            if (HasFileAncestor(directory, files))
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.ArchiveEntryConflict,
                    "压缩包包含文件与目录路径冲突。",
                    directory);
            }
        }

        var allDirectories = new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase);
        foreach (string path in seen.Keys)
        {
            string? parent = GetParent(path);
            while (!string.IsNullOrEmpty(parent))
            {
                allDirectories.Add(parent);
                parent = GetParent(parent);
            }
        }

        return new ValidatedArchiveManifest(
            format,
            validated,
            files,
            allDirectories,
            totalBytes);
    }

    public static string NormalizeEntryPath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains('\0'))
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包包含空或无效路径。",
                entryName);
        }

        string slashPath = entryName.Replace('\\', '/');
        if (slashPath.StartsWith("/", StringComparison.Ordinal)
            || slashPath.StartsWith("//", StringComparison.Ordinal)
            || slashPath.Contains(":", StringComparison.Ordinal))
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包包含绝对路径或盘符路径。",
                entryName);
        }

        string[] parts = slashPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包包含越界路径。",
                entryName);
        }

        char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
        foreach (string part in parts)
        {
            if (part.Length > MaximumSegmentLength
                || part.EndsWith(".", StringComparison.Ordinal)
                || part.EndsWith(" ", StringComparison.Ordinal)
                || part.IndexOfAny(invalidFileNameChars) >= 0
                || IsReservedDeviceName(part))
            {
                throw new ArchiveManifestValidationException(
                    ErrorCode.UnsafeArchiveEntry,
                    "压缩包包含 Windows 不支持的文件名。",
                    entryName);
            }
        }

        string normalized = string.Join(Path.DirectorySeparatorChar, parts);
        if (normalized.Length > MaximumRelativePathLength)
        {
            throw new ArchiveManifestValidationException(
                ErrorCode.UnsafeArchiveEntry,
                "压缩包路径过长，无法安全创建。",
                entryName);
        }

        return normalized;
    }

    private static bool IsExpectedArchiveType(
        ArchiveFormatDescriptor format,
        string archiveType)
    {
        string normalized = archiveType.Trim();
        return format.Format switch
        {
            ArchiveFormat.Zip => normalized.Equals("zip", StringComparison.OrdinalIgnoreCase),
            ArchiveFormat.SevenZip => normalized.Equals("7z", StringComparison.OrdinalIgnoreCase),
            ArchiveFormat.Rar => normalized.StartsWith("rar", StringComparison.OrdinalIgnoreCase),
            ArchiveFormat.Tar => normalized.Equals("tar", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool HasFileAncestor(
        string path,
        IReadOnlySet<string> files)
    {
        string? parent = GetParent(path);
        while (!string.IsNullOrEmpty(parent))
        {
            if (files.Contains(parent))
            {
                return true;
            }

            parent = GetParent(parent);
        }

        return false;
    }

    private static string? GetParent(string path)
    {
        int separator = path.LastIndexOf(Path.DirectorySeparatorChar);
        return separator <= 0 ? null : path[..separator];
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
}

public sealed class ArchiveManifestValidationException : Exception
{
    public ArchiveManifestValidationException(
        ErrorCode errorCode,
        string userMessage,
        string? entryName,
        Exception? innerException = null)
        : base($"{userMessage} Entry: {entryName}", innerException)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
    }

    public ErrorCode ErrorCode { get; }

    public string UserMessage { get; }
}
