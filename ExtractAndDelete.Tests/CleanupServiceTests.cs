using ExtractAndDelete.Core;

namespace ExtractAndDelete.Tests;

public sealed class CleanupServiceTests
{
    [Fact]
    public async Task MoveToRecycleBinAsync_MissingFile_ReturnsUnavailable()
    {
        string filePath = Path.Combine(
            Path.GetTempPath(),
            $"ExtractAndDelete-missing-{Guid.NewGuid():N}.zip");

        CleanupResult result = await new CleanupService().MoveToRecycleBinAsync(filePath);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.RecycleUnavailable, result.ErrorCode);
        Assert.True(File.Exists(filePath) is false);
    }

    [Fact]
    public async Task MoveToRecycleBinAsync_InvalidPath_ReturnsUnavailable()
    {
        CleanupResult result = await new CleanupService().MoveToRecycleBinAsync(string.Empty);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.RecycleUnavailable, result.ErrorCode);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task MoveToRecycleBinAsync_ExistingUniqueFile_LeavesNoOriginalPath()
    {
        using TemporaryDirectory temp = new();
        string filePath = Path.Combine(temp.Path, $"integration-{Guid.NewGuid():N}.txt");
        File.WriteAllText(filePath, "integration test only");

        CleanupResult result = await new CleanupService().MoveToRecycleBinAsync(filePath);

        Assert.True(result.Success, result.DiagnosticMessage);
        Assert.False(File.Exists(filePath));
    }
}
