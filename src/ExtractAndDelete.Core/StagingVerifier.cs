namespace ExtractAndDelete.Core;

public static class StagingVerifier
{
    public static void Verify(
        string stagingPath,
        ValidatedArchiveManifest manifest)
    {
        string root = Path.GetFullPath(stagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new StagingVerificationException(
                "7-Zip 未创建预期的临时目录。",
                root);
        }

        EnsureDirectory(root);
        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileIds = new HashSet<(uint Volume, ulong Index)>();

        VerifyDirectory(
            root,
            root,
            manifest,
            visitedFiles,
            visitedDirectories,
            fileIds);

        if (!manifest.Files.SetEquals(visitedFiles)
            || !manifest.Directories.SetEquals(visitedDirectories))
        {
            throw new StagingVerificationException(
                "解压后的临时目录与压缩包清单不一致。",
                $"Expected files: {manifest.Files.Count}; actual files: {visitedFiles.Count}; "
                + $"expected directories: {manifest.Directories.Count}; actual directories: {visitedDirectories.Count}.");
        }
    }

    private static void VerifyDirectory(
        string root,
        string directory,
        ValidatedArchiveManifest manifest,
        ISet<string> visitedFiles,
        ISet<string> visitedDirectories,
        ISet<(uint Volume, ulong Index)> fileIds)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };

        foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", options))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StagingVerificationException(
                    "解压结果包含不允许的 reparse-point。",
                    path);
            }

            string relative = Path.GetRelativePath(root, path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!manifest.Directories.Contains(relative))
                {
                    throw new StagingVerificationException(
                        "解压结果包含未声明的目录。",
                        relative);
                }

                visitedDirectories.Add(relative);
                NormalizeDirectoryAttributes(path);
                VerifyDirectory(
                    root,
                    path,
                    manifest,
                    visitedFiles,
                    visitedDirectories,
                    fileIds);
                continue;
            }

            ValidatedArchiveEntry? expected = manifest.Entries.FirstOrDefault(
                value => !value.IsDirectory
                    && string.Equals(value.EntryPath, relative, StringComparison.OrdinalIgnoreCase));
            if (expected is null)
            {
                throw new StagingVerificationException(
                    "解压结果包含未声明的文件。",
                    $"{relative}; expected files: {string.Join(", ", manifest.Files)}");
            }

            if (new FileInfo(path).Length != expected.Size)
            {
                throw new StagingVerificationException(
                    "解压结果中的文件大小与压缩包清单不一致。",
                    relative);
            }

            FileIdentity? identity = FileIdentityReader.TryRead(path);
            if (identity is null || !fileIds.Add((identity.VolumeSerialNumber, identity.FileIndex)))
            {
                throw new StagingVerificationException(
                    "解压结果包含无法验证或重复的文件身份。",
                    relative);
            }

            visitedFiles.Add(relative);
            NormalizeFileAttributes(path);
        }
    }

    private static void EnsureDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0
            || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new StagingVerificationException(
                "临时目录不是安全的普通目录。",
                path);
        }
    }

    private static void NormalizeFileAttributes(string path) =>
        File.SetAttributes(path, FileAttributes.Normal);

    private static void NormalizeDirectoryAttributes(string path) =>
        File.SetAttributes(path, FileAttributes.Directory);
}

public sealed class StagingVerificationException : Exception
{
    public StagingVerificationException(string message, string diagnostic)
        : base($"{message} Path: {diagnostic}")
    {
    }
}
