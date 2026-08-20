using System.IO.Compression;

namespace ExtractAndDelete.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ExtractAndDelete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Tests must not mask their assertion with best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Tests must not mask their assertion with best-effort temp cleanup.
        }
    }
}

internal static class TestArchives
{
    public static string CreateZip(
        string root,
        string fileName = "source.zip",
        params (string Name, string Content)[] files)
    {
        string zipPath = System.IO.Path.Combine(root, fileName);
        using FileStream stream = File.Create(zipPath);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        foreach ((string name, string content) in files)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }

    public static string CreateArchive(
        string root,
        string fileName,
        Action<ZipArchive> populate)
    {
        string zipPath = System.IO.Path.Combine(root, fileName);
        using FileStream stream = File.Create(zipPath);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        populate(archive);
        return zipPath;
    }
}
