using ExtractAndDelete.Gui.ViewModels;

namespace ExtractAndDelete.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void DefaultState_RequiresBothSelections()
    {
        MainViewModel viewModel = new();

        Assert.Null(viewModel.ArchivePath);
        Assert.Null(viewModel.ParentDirectory);
        Assert.Null(viewModel.DestinationPath);
        Assert.False(viewModel.CanExecute);
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.CanCancel);
        Assert.Equal(StatusTone.Normal, viewModel.StatusTone);
    }

    [Fact]
    public void SelectingArchive_ClearsParentAndComputesNothingUntilFolderSelected()
    {
        using TemporaryDirectory temp = new();
        string archivePath = Path.Combine(temp.Path, "中文 archive.zip");
        File.WriteAllText(archivePath, "placeholder");
        MainViewModel viewModel = new();

        viewModel.SetParentDirectory(temp.Path);
        viewModel.SetArchive(archivePath);

        Assert.Equal(Path.GetFullPath(archivePath), viewModel.ArchivePath);
        Assert.Null(viewModel.ParentDirectory);
        Assert.Null(viewModel.DestinationPath);
        Assert.False(viewModel.CanExecute);
    }

    [Fact]
    public void SelectingFolder_ComputesArchiveBasenameAndRejectsExistingDirectory()
    {
        using TemporaryDirectory temp = new();
        string archivePath = Path.Combine(temp.Path, "压缩包 & (1).ZIP");
        File.WriteAllText(archivePath, "placeholder");
        MainViewModel viewModel = new();
        viewModel.SetArchive(archivePath);

        viewModel.SetParentDirectory(temp.Path);

        string expectedDestination = Path.Combine(temp.Path, "压缩包 & (1)");
        Assert.Equal(expectedDestination, viewModel.DestinationPath);
        Assert.True(viewModel.CanExecute);

        Directory.CreateDirectory(expectedDestination);
        viewModel.SetParentDirectory(temp.Path);

        Assert.False(viewModel.CanExecute);
        Assert.Contains("最终目录已存在", viewModel.StatusMessage);
        Assert.Equal(StatusTone.Error, viewModel.StatusTone);
    }

    [Fact]
    public void ActivationParser_PreservesUnicodeSpacesAmpersandAndParentheses()
    {
        string path = @"C:\测试目录\archive & (1).zip";

        string? parsed = CommandLineActivation.TryGetArchivePath(
            $"--archive \"{path}\"");

        Assert.Equal(path, parsed);
    }

    [Fact]
    public void InvalidPickerPath_DoesNotThrowAndCannotExecute()
    {
        MainViewModel viewModel = new();

        viewModel.SetArchive("\0invalid.zip");
        viewModel.SetParentDirectory("\0invalid-parent");

        Assert.False(viewModel.CanExecute);
        Assert.Null(viewModel.ArchivePath);
        Assert.Null(viewModel.ParentDirectory);
        Assert.Equal(StatusTone.Error, viewModel.StatusTone);
    }

    [Fact]
    public void UnsupportedArchive_IsRejectedBeforeExecution()
    {
        using TemporaryDirectory temp = new();
        string archivePath = Path.Combine(temp.Path, "not-a-zip.txt");
        File.WriteAllText(archivePath, "placeholder");
        MainViewModel viewModel = new();

        viewModel.SetArchive(archivePath);
        viewModel.SetParentDirectory(temp.Path);

        Assert.False(viewModel.CanExecute);
        Assert.Equal(StatusTone.Error, viewModel.StatusTone);
        Assert.Contains("仅支持 ZIP、7Z、RAR 和 TAR", viewModel.StatusMessage);
    }
}
