using ExtractAndDelete.Core;

namespace ExtractAndDelete.Tests;

public sealed class WindowsShellPublisherIntegrationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task PublishesIntoNewUnicodeDirectoryThroughWindowsShell()
    {
        using TemporaryDirectory temp = new();
        string staging = Path.Combine(temp.Path, ".extractanddelete-stage.tmp");
        string destination = Path.Combine(temp.Path, "目标 & (1)");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(Path.Combine(staging, "子目录"));
        File.WriteAllText(Path.Combine(staging, "中文.txt"), "shell publisher");
        File.WriteAllText(Path.Combine(staging, "子目录", "nested.txt"), "nested");

        DestinationPublishResult result = await new WindowsShellDestinationPublisher(IntPtr.Zero)
            .PublishAsync(staging, destination, progress: null, CancellationToken.None);

        Assert.Equal(DestinationPublishOutcome.Completed, result.Outcome);
        Assert.Equal(DestinationState.Completed, result.DestinationState);
        Assert.True(File.Exists(Path.Combine(destination, "中文.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(destination, "子目录", "nested.txt")));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task FullGuiWorkflowPublishesAndRecyclesUniqueZip()
    {
        using TemporaryDirectory temp = new();
        string archive = TestArchives.CreateZip(temp.Path, "full-workflow.zip", ("hello.txt", "workflow"));
        string destination = Path.Combine(temp.Path, "完整目标");

        ExtractAndDeleteResult result = await ExtractionService.CreateGui(IntPtr.Zero).ExecuteAsync(
            new ExtractAndDeleteRequest(archive, destination),
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success, result.DiagnosticMessage);
        Assert.Equal(DestinationState.Completed, result.DestinationState);
        Assert.Equal(SourceDisposition.Recycled, result.SourceDisposition);
        Assert.Equal("workflow", File.ReadAllText(Path.Combine(destination, "hello.txt")));
        Assert.False(File.Exists(archive));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task FullGuiWorkflowMergesIntoExistingDirectoryWithoutConflict()
    {
        using TemporaryDirectory temp = new();
        string archive = TestArchives.CreateZip(temp.Path, "merge-workflow.zip", ("new.txt", "new"));
        string destination = Path.Combine(temp.Path, "已有目标");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "old.txt"), "old");

        ExtractAndDeleteResult result = await ExtractionService.CreateGui(IntPtr.Zero).ExecuteAsync(
            new ExtractAndDeleteRequest(archive, destination), null, CancellationToken.None);

        Assert.True(result.Success, result.DiagnosticMessage);
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "old.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "new.txt")));
        Assert.False(File.Exists(archive));
    }
}
