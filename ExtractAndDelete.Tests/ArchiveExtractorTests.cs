using ExtractAndDelete.Core;
using System.IO.Compression;

namespace ExtractAndDelete.Tests;

public sealed class ArchiveExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ValidZip_ExtractsToStaging()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(
            temp.Path,
            "source.ZIP",
            ("folder/hello.txt", "Hello"));
        string stagingPath = Path.Combine(temp.Path, "staging");

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            stagingPath,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ErrorCode.None, result.ErrorCode);
        Assert.Equal("Hello", File.ReadAllText(Path.Combine(stagingPath, "folder", "hello.txt")));
        Assert.True(File.Exists(zipPath));
    }

    [Fact]
    public async Task ExtractAsync_EmptyZip_Succeeds()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateArchive(temp.Path, "empty.zip", _ => { });
        string stagingPath = Path.Combine(temp.Path, "staging");

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            stagingPath,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(stagingPath));
    }

    [Fact]
    public async Task ExtractAsync_EmptyDirectory_PreservesDirectory()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateArchive(temp.Path, "empty-dir.zip", archive =>
        {
            archive.CreateEntry("empty/");
        });
        string stagingPath = Path.Combine(temp.Path, "staging");

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            stagingPath,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.Combine(stagingPath, "empty")));
    }

    [Fact]
    public async Task ExtractAsync_InvalidZip_ReturnsArchiveUnreadable()
    {
        using TemporaryDirectory temp = new();
        string zipPath = Path.Combine(temp.Path, "invalid.zip");
        File.WriteAllText(zipPath, "not a zip");

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            Path.Combine(temp.Path, "staging"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.ArchiveUnreadable, result.ErrorCode);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("folder/../../escape.txt")]
    public async Task ExtractAsync_UnsafePath_IsRejected(string entryName)
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateArchive(temp.Path, "unsafe.zip", archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using StreamWriter writer = new(entry.Open());
            writer.Write("unsafe");
        });

        string stagingPath = Path.Combine(temp.Path, "staging");
        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            stagingPath,
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.UnsafeArchiveEntry, result.ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "escape.txt")));
    }

    [Fact]
    public async Task ExtractAsync_CaseInsensitiveDuplicatePath_IsRejected()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateArchive(temp.Path, "duplicate.zip", archive =>
        {
            foreach (string name in new[] { "Readme.txt", "readme.TXT" })
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using StreamWriter writer = new(entry.Open());
                writer.Write(name);
            }
        });

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            Path.Combine(temp.Path, "staging"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.DuplicateArchiveEntry, result.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_FileDirectoryConflict_IsRejected()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateArchive(temp.Path, "conflict.zip", archive =>
        {
            ZipArchiveEntry file = archive.CreateEntry("folder");
            using (StreamWriter writer = new(file.Open()))
            {
                writer.Write("file");
            }

            ZipArchiveEntry child = archive.CreateEntry("folder/child.txt");
            using StreamWriter childWriter = new(child.Open());
            childWriter.Write("child");
        });

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            Path.Combine(temp.Path, "staging"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.ArchiveEntryConflict, result.ErrorCode);
    }

    [Fact]
    public async Task ExtractAsync_CancellationDuringCopy_ReturnsCancelled()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateArchive(temp.Path, "large.zip", archive =>
        {
            ZipArchiveEntry entry = archive.CreateEntry("large.bin", CompressionLevel.NoCompression);
            using Stream output = entry.Open();
            byte[] buffer = new byte[1024 * 1024];
            for (int i = 0; i < 8; i++)
            {
                output.Write(buffer);
            }
        });

        using CancellationTokenSource cancellation = new();
        Progress<ExtractionProgress> progress = new(value =>
        {
            if (value.Stage == WorkflowStage.Extracting && value.CompletedBytes > 0)
            {
                cancellation.Cancel();
            }
        });

        ArchiveExtractionResult result = await new ArchiveExtractor().ExtractAsync(
            zipPath,
            Path.Combine(temp.Path, "staging"),
            progress,
            cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.True(File.Exists(zipPath));
    }
}
