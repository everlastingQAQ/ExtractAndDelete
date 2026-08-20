using ExtractAndDelete.Core;

namespace ExtractAndDelete.Tests;

public sealed class SevenZipArchiveExtractorTests
{
    [Fact]
    public async Task ExtractsZipWithThePinnedEngineAndVerifiesStaging()
    {
        using TemporaryDirectory temp = new();
        string archivePath = TestArchives.CreateZip(
            temp.Path,
            "中文 archive.zip",
            ("folder/hello.txt", "Hello from 7-Zip"),
            ("空文件", string.Empty));
        string stagingPath = Path.Combine(temp.Path, ".extractanddelete-test.tmp");

        ArchiveExtractionResult result = await new SevenZipArchiveExtractor(
            new SevenZipToolProvider(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "7-Zip")))
            .ExtractAsync(archivePath, stagingPath, progress: null, CancellationToken.None);

        Assert.True(result.Success, result.DiagnosticMessage);
        Assert.Equal(ErrorCode.None, result.ErrorCode);
        Assert.Equal(StagingCleanupState.ReadyForPublish, result.StagingCleanupState);
        Assert.Equal("Hello from 7-Zip", File.ReadAllText(Path.Combine(stagingPath, "folder", "hello.txt")));
        Assert.True(File.Exists(Path.Combine(stagingPath, "空文件")));
    }

    [Fact]
    public async Task RejectsUnsafeEntryBeforePublishing()
    {
        using TemporaryDirectory temp = new();
        string archivePath = TestArchives.CreateArchive(temp.Path, "unsafe.zip", archive =>
        {
            archive.CreateEntry("../outside.txt");
        });
        string stagingPath = Path.Combine(temp.Path, ".extractanddelete-test.tmp");

        ArchiveExtractionResult result = await new SevenZipArchiveExtractor(
            new SevenZipToolProvider(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "7-Zip")))
            .ExtractAsync(archivePath, stagingPath, progress: null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.UnsafeArchiveEntry, result.ErrorCode);
        Assert.True(File.Exists(archivePath));
    }
}
