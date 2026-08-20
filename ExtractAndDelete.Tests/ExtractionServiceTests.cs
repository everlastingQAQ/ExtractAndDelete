using ExtractAndDelete.Core;

namespace ExtractAndDelete.Tests;

public sealed class ExtractionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_InvalidZip_KeepsSourceAndDoesNotCallCleanup()
    {
        using TemporaryDirectory temp = new();
        string zipPath = Path.Combine(temp.Path, "invalid.zip");
        string destinationPath = Path.Combine(temp.Path, "output");
        File.WriteAllText(zipPath, "not a zip");
        FakeCleanupService cleanup = new(success: true);

        ExtractAndDeleteResult result = await new ExtractionService(
            new ArchiveExtractor(),
            cleanup).ExecuteAsync(
                new ExtractAndDeleteRequest(zipPath, destinationPath),
                progress: null,
                CancellationToken.None);

        Assert.Equal(WorkflowOutcome.ExtractionFailed, result.Outcome);
        Assert.Equal(ErrorCode.ArchiveUnreadable, result.ErrorCode);
        Assert.True(File.Exists(zipPath));
        Assert.False(Directory.Exists(destinationPath));
        Assert.Equal(0, cleanup.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ValidArchive_PublishesAndRecycles()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        FakeCleanupService cleanup = new(success: true);

        ExtractAndDeleteResult result = await CreateFakeService(cleanup).ExecuteAsync(
            new ExtractAndDeleteRequest(zipPath, destinationPath),
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(WorkflowOutcome.Completed, result.Outcome);
        Assert.Equal(ErrorCode.None, result.ErrorCode);
        Assert.True(result.DestinationPublished);
        Assert.Equal(SourceDisposition.Recycled, result.SourceDisposition);
        Assert.True(File.Exists(Path.Combine(destinationPath, "hello.txt")));
        Assert.False(File.Exists(zipPath));
        Assert.Equal(1, cleanup.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CleanupFailure_KeepsPublishedOutputAndSource()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        FakeCleanupService cleanup = new(success: false);

        ExtractAndDeleteResult result = await CreateFakeService(cleanup).ExecuteAsync(
            new ExtractAndDeleteRequest(zipPath, destinationPath),
            progress: null,
            CancellationToken.None);

        Assert.Equal(WorkflowOutcome.CleanupFailed, result.Outcome);
        Assert.Equal(ErrorCode.RecycleFailed, result.ErrorCode);
        Assert.True(result.DestinationPublished);
        Assert.True(Directory.Exists(destinationPath));
        Assert.True(File.Exists(zipPath));
    }

    [Fact]
    public async Task ExecuteAsync_ExistingDestination_IsRejectedWithoutModification()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "new"));
        string destinationPath = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(destinationPath);
        string existingFile = Path.Combine(destinationPath, "existing.txt");
        File.WriteAllText(existingFile, "old");
        FakeArchiveExtractor extractor = new(success: true);
        FakeCleanupService cleanup = new(success: true);

        ExtractAndDeleteResult result = await new ExtractionService(extractor, cleanup).ExecuteAsync(
            new ExtractAndDeleteRequest(zipPath, destinationPath),
            progress: null,
            CancellationToken.None);

        Assert.Equal(WorkflowOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(ErrorCode.DestinationAlreadyExists, result.ErrorCode);
        Assert.Equal("old", File.ReadAllText(existingFile));
        Assert.Equal(0, extractor.CallCount);
        Assert.Equal(0, cleanup.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeExtraction_DoesNotCreateOutput()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        FakeArchiveExtractor extractor = new(success: true);

        ExtractAndDeleteResult result = await CreateFakeService(new FakeCleanupService(true), extractor)
            .ExecuteAsync(
                new ExtractAndDeleteRequest(zipPath, destinationPath),
                progress: null,
                cancellation.Token);

        Assert.Equal(WorkflowOutcome.Cancelled, result.Outcome);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.False(Directory.Exists(destinationPath));
        Assert.True(File.Exists(zipPath));
        Assert.Equal(0, extractor.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringExtraction_CleansStaging()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        FakeArchiveExtractor extractor = new(success: false, errorCode: ErrorCode.Cancelled);

        ExtractAndDeleteResult result = await CreateFakeService(new FakeCleanupService(true), extractor)
            .ExecuteAsync(
                new ExtractAndDeleteRequest(zipPath, destinationPath),
                progress: null,
                CancellationToken.None);

        Assert.Equal(WorkflowOutcome.Cancelled, result.Outcome);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.False(Directory.Exists(destinationPath));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".extractanddelete-*.tmp"));
        Assert.True(File.Exists(zipPath));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAfterPublishing_IsIgnored()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        using CancellationTokenSource cancellation = new();
        FakeCleanupService cleanup = new(success: true);

        InlineProgress progress = new(value =>
        {
            if (value.Stage == WorkflowStage.Publishing)
            {
                cancellation.Cancel();
            }
        });

        ExtractAndDeleteResult result = await CreateFakeService(cleanup).ExecuteAsync(
            new ExtractAndDeleteRequest(zipPath, destinationPath),
            progress,
            cancellation.Token);

        Assert.True(result.Success);
        Assert.True(Directory.Exists(destinationPath));
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public async Task ExecuteAsync_SourceChangesBeforePublishing_DoesNotPublishOrCleanup()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        FakeArchiveExtractor extractor = new(success: true, mutateArchive: true);
        FakeCleanupService cleanup = new(success: true);

        ExtractAndDeleteResult result = await CreateFakeService(cleanup, extractor).ExecuteAsync(
            new ExtractAndDeleteRequest(zipPath, destinationPath),
            progress: null,
            CancellationToken.None);

        Assert.Equal(WorkflowOutcome.ExtractionFailed, result.Outcome);
        Assert.Equal(ErrorCode.SourceChanged, result.ErrorCode);
        Assert.False(result.DestinationPublished);
        Assert.False(Directory.Exists(destinationPath));
        Assert.True(File.Exists(zipPath));
        Assert.Equal(0, cleanup.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DestinationCreatedBeforePublish_ReturnsPublishFailure()
    {
        using TemporaryDirectory temp = new();
        string zipPath = TestArchives.CreateZip(temp.Path, "source.zip", ("hello.txt", "Hello"));
        string destinationPath = Path.Combine(temp.Path, "output");
        FakeCleanupService cleanup = new(success: true);
        InlineProgress progress = new(value =>
        {
            if (value.Stage == WorkflowStage.Publishing && !Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
                File.WriteAllText(Path.Combine(destinationPath, "untouched.txt"), "untouched");
            }
        });

        ExtractAndDeleteResult result = await CreateFakeService(cleanup).ExecuteAsync(
            new ExtractAndDeleteRequest(zipPath, destinationPath),
            progress,
            CancellationToken.None);

        Assert.Equal(WorkflowOutcome.PublishFailed, result.Outcome);
        Assert.Equal(ErrorCode.PublishFailure, result.ErrorCode);
        Assert.True(File.Exists(Path.Combine(destinationPath, "untouched.txt")));
        Assert.True(File.Exists(zipPath));
        Assert.Equal(0, cleanup.CallCount);
    }

    private static ExtractionService CreateFakeService(
        FakeCleanupService cleanup,
        FakeArchiveExtractor? extractor = null) =>
        new(extractor ?? new FakeArchiveExtractor(success: true), cleanup);

    private sealed class InlineProgress(Action<ExtractionProgress> handler) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) => handler(value);
    }

    private sealed class FakeArchiveExtractor(
        bool success,
        ErrorCode errorCode = ErrorCode.None,
        bool mutateArchive = false) : IArchiveExtractor
    {
        public int CallCount { get; private set; }

        public Task<ArchiveExtractionResult> ExtractAsync(
            string archivePath,
            string stagingPath,
            IProgress<ExtractionProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Directory.CreateDirectory(stagingPath);
            if (success)
            {
                File.WriteAllText(Path.Combine(stagingPath, "hello.txt"), "Hello");
                if (mutateArchive)
                {
                    File.AppendAllText(archivePath, "changed");
                }
            }

            return Task.FromResult(new ArchiveExtractionResult(
                success,
                success ? ErrorCode.None : errorCode,
                success ? "解压完成。" : "用户已取消解压。"));
        }
    }

    private sealed class FakeCleanupService(bool success) : ICleanupService
    {
        public int CallCount { get; private set; }

        public Task<CleanupResult> MoveToRecycleBinAsync(string filePath)
        {
            CallCount++;
            if (success)
            {
                File.Delete(filePath);
                return Task.FromResult(new CleanupResult(true, ErrorCode.None, "已回收。"));
            }

            return Task.FromResult(new CleanupResult(
                false,
                ErrorCode.RecycleFailed,
                "无法回收。"));
        }
    }
}
