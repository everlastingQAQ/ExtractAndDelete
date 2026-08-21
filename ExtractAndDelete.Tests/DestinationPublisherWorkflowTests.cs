using ExtractAndDelete.Core;
using ExtractAndDelete.Gui.ViewModels;

namespace ExtractAndDelete.Tests;

public sealed class DestinationPublisherWorkflowTests
{
    [Fact]
    public async Task GuiPublisherPolicy_AllowsExistingDirectory_AndRecyclesAfterComplete()
    {
        using TemporaryDirectory temp = new();
        string archive = TestArchives.CreateZip(temp.Path, "source.zip", ("new.txt", "new"));
        string destination = Path.Combine(temp.Path, "existing");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "old.txt"), "old");
        FakePublisher publisher = new(DestinationPublishOutcome.Completed, DestinationState.Completed);
        FakeCleanup cleanup = new(success: true);

        ExtractAndDeleteResult result = await new ExtractionService(
            new SuccessfulExtractor(), publisher, cleanup).ExecuteAsync(
            new(archive, destination), null, CancellationToken.None);

        Assert.Equal(WorkflowOutcome.Completed, result.Outcome);
        Assert.True(result.DestinationPublished);
        Assert.Equal(1, cleanup.CallCount);
        Assert.True(File.Exists(Path.Combine(destination, "old.txt")));
    }

    [Fact]
    public async Task SkippedItems_KeepSourceAndNeverCallCleanup()
    {
        using TemporaryDirectory temp = new();
        string archive = TestArchives.CreateZip(temp.Path, "source.zip", ("new.txt", "new"));
        string destination = Path.Combine(temp.Path, "output");
        FakePublisher publisher = new(DestinationPublishOutcome.CompletedWithSkippedItems, DestinationState.CompletedWithSkippedItems, skipped: 1);
        FakeCleanup cleanup = new(success: true);

        ExtractAndDeleteResult result = await new ExtractionService(
            new SuccessfulExtractor(), publisher, cleanup).ExecuteAsync(
            new(archive, destination), null, CancellationToken.None);

        Assert.Equal(WorkflowOutcome.CompletedWithSkippedItems, result.Outcome);
        Assert.Equal(ErrorCode.DestinationItemsSkipped, result.ErrorCode);
        Assert.Equal(SourceDisposition.Retained, result.SourceDisposition);
        Assert.Equal(0, cleanup.CallCount);
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task PublishingCancellation_LeavesSourceAndSkipsCleanup()
    {
        using TemporaryDirectory temp = new();
        string archive = TestArchives.CreateZip(temp.Path, "source.zip", ("new.txt", "new"));
        string destination = Path.Combine(temp.Path, "output");
        FakePublisher publisher = new(DestinationPublishOutcome.Cancelled, DestinationState.PartiallyModified);
        FakeCleanup cleanup = new(success: true);

        ExtractAndDeleteResult result = await new ExtractionService(
            new SuccessfulExtractor(), publisher, cleanup).ExecuteAsync(
            new(archive, destination), null, CancellationToken.None);

        Assert.Equal(WorkflowOutcome.Cancelled, result.Outcome);
        Assert.Equal(DestinationState.PartiallyModified, result.DestinationState);
        Assert.Equal(0, cleanup.CallCount);
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public void DefaultTarget_UsesOnlyLastExtension()
    {
        using TemporaryDirectory temp = new();
        MainViewModel viewModel = new();
        string archive = Path.Combine(temp.Path, "package.data.7Z");
        File.WriteAllText(archive, "placeholder");

        viewModel.SetArchiveFromActivation(archive);

        Assert.Equal(Path.Combine(temp.Path, "package.data"), viewModel.TargetPath);
        Assert.True(viewModel.ShowExtractedFiles);
    }

    private sealed class SuccessfulExtractor : IArchiveExtractor
    {
        public Task<ArchiveExtractionResult> ExtractAsync(
            string archivePath,
            string stagingPath,
            IProgress<ExtractionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(stagingPath);
            File.WriteAllText(Path.Combine(stagingPath, "new.txt"), "new");
            return Task.FromResult(new ArchiveExtractionResult(true, ErrorCode.None, "ok"));
        }
    }

    private sealed class FakePublisher(
        DestinationPublishOutcome outcome,
        DestinationState state,
        int skipped = 0) : IDestinationPublisher, IDestinationPublisherPolicy
    {
        public bool AllowsExistingDirectory => true;
        public bool SupportsCancellation => true;

        public Task<DestinationPublishResult> PublishAsync(
            string stagingPath,
            string destinationPath,
            IProgress<ExtractionProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (outcome == DestinationPublishOutcome.Completed)
            {
                Directory.CreateDirectory(destinationPath);
            }

            return Task.FromResult(new DestinationPublishResult(
                outcome,
                skipped > 0 ? ErrorCode.DestinationItemsSkipped : ErrorCode.None,
                "fake",
                state,
                1,
                skipped,
                0));
        }
    }

    private sealed class FakeCleanup(bool success) : ICleanupService
    {
        public int CallCount { get; private set; }

        public Task<CleanupResult> MoveToRecycleBinAsync(string filePath)
        {
            CallCount++;
            if (success)
            {
                File.Delete(filePath);
                return Task.FromResult(new CleanupResult(true, ErrorCode.None, "ok"));
            }

            return Task.FromResult(new CleanupResult(false, ErrorCode.RecycleFailed, "failed"));
        }
    }
}
