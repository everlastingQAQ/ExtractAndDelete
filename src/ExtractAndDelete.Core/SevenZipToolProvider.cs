using System.Security.Cryptography;

namespace ExtractAndDelete.Core;

public sealed record SevenZipToolPaths(string ExecutablePath, string LibraryPath);

public sealed class SevenZipEngineException : Exception
{
    public SevenZipEngineException(
        ErrorCode errorCode,
        string userMessage,
        string diagnosticMessage)
        : base(userMessage)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
        DiagnosticMessage = diagnosticMessage;
    }

    public ErrorCode ErrorCode { get; }

    public string UserMessage { get; }

    public string DiagnosticMessage { get; }
}

public sealed class SevenZipToolProvider
{
    public const string Version = "26.02";
    public const string ExpectedExecutableSha256 =
        "83967f1b02b43c4efeda302795722c809e0e81b8307de73558d10484d5676a7d";
    public const string ExpectedLibrarySha256 =
        "69fd4df057985c40e510e2fac182881c7f85e90aa13ec703f763a8fdb2ce61f8";

    private readonly string _rootDirectory;

    public SevenZipToolProvider(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A tool directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public static SevenZipToolProvider CreateDefault() =>
        new(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "7-Zip"));

    public SevenZipToolPaths ResolveAndVerify()
    {
        string executablePath = Path.Combine(_rootDirectory, "7z.exe");
        string libraryPath = Path.Combine(_rootDirectory, "7z.dll");
        if (!File.Exists(executablePath) || !File.Exists(libraryPath))
        {
            throw new SevenZipEngineException(
                ErrorCode.ArchiveEngineUnavailable,
                "内置 7-Zip 引擎不可用，无法解压压缩包。",
                $"Missing 7-Zip 26.02 files under '{_rootDirectory}'.");
        }

        string executableHash = ComputeSha256(executablePath);
        string libraryHash = ComputeSha256(libraryPath);
        if (!string.Equals(executableHash, ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(libraryHash, ExpectedLibrarySha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new SevenZipEngineException(
                ErrorCode.ArchiveEngineIntegrityFailure,
                "内置 7-Zip 引擎完整性校验失败，已停止操作。",
                $"7z.exe SHA-256: {executableHash}; 7z.dll SHA-256: {libraryHash}.");
        }

        return new SevenZipToolPaths(executablePath, libraryPath);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
