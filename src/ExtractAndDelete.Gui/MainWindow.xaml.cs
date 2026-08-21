using ExtractAndDelete.Core;
using ExtractAndDelete.Gui.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Diagnostics;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ExtractAndDelete.Gui;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private Task<ExtractAndDeleteResult?>? _workflowTask;
    private bool _allowClose;
    private bool _closePromptShowing;

    public MainWindow()
    {
        ViewModel = new MainViewModel();
        InitializeComponent();
        ViewModel.SetServiceFactory(() =>
            ExtractionService.CreateGui(WindowNative.GetWindowHandle(this)));
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppWindow.Closing += MainWindow_Closing;

        try
        {
            AppWindow.Resize(new SizeInt32(760, 500));
        }
        catch
        {
            // Window sizing is a visual hint only; a platform shell may reject
            // it when the app is restored from a saved placement.
        }

        UpdateVisualState();
    }

    public void HandleActivation(string? archivePath)
    {
        _ = HandleActivationAsync(archivePath);
    }

    public async Task HandleActivationAsync(string? archivePath)
    {
        Activate();
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.ArchivePath))
            {
                return;
            }

            StorageFile? file = await PickArchiveAsync();
            if (file is null)
            {
                _allowClose = true;
                Close();
                return;
            }

            ViewModel.SetArchiveFromPicker(file.Path);
            return;
        }

        if (ViewModel.IsRunning)
        {
            ViewModel.SetStatus("当前已有解压任务正在进行。", StatusTone.Warning);
            Activate();
            return;
        }

        ViewModel.SetArchiveFromActivation(archivePath);
        Activate();
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.SetTargetPath(folder.Path);
        }
    }

    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workflowTask is not null)
        {
            return;
        }

        _workflowTask = ViewModel.ExecuteAsync();
        try
        {
            ExtractAndDeleteResult? result = await _workflowTask;
            if (result is null)
            {
                return;
            }

            bool opened = true;
            if (result.DestinationPublished && ViewModel.ShowExtractedFiles)
            {
                opened = TryOpenDestination(result.DestinationPath);
            }

            if (result.Success && opened)
            {
                _allowClose = true;
                Close();
            }
        }
        finally
        {
            _workflowTask = null;
            UpdateVisualState();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRunning)
        {
            ViewModel.RequestCancellation();
            return;
        }

        _allowClose = true;
        Close();
    }

    private async Task<StorageFile?> PickArchiveAsync()
    {
        FileOpenPicker picker = new();
        foreach (string extension in SupportedArchiveFormats.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFileAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Bindings.Update();
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        if (ProgressPanel is not null)
        {
            ProgressPanel.Visibility = ViewModel.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        }

        if (StatusTextBlock is not null)
        {
            StatusTextBlock.Visibility = ViewModel.IsRunning || ViewModel.StatusTone != StatusTone.Normal
                ? Visibility.Visible
                : Visibility.Collapsed;
            StatusTextBlock.Foreground = ViewModel.StatusTone switch
            {
                StatusTone.Success => new SolidColorBrush(Colors.ForestGreen),
                StatusTone.Warning => new SolidColorBrush(Colors.DarkGoldenrod),
                StatusTone.Error => new SolidColorBrush(Colors.Firebrick),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
    }

    private bool TryOpenDestination(string destinationPath)
    {
        try
        {
            if (!Directory.Exists(destinationPath))
            {
                ViewModel.SetStatus("已完成，但找不到目标文件夹，无法打开。", StatusTone.Warning);
                return false;
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = "explorer.exe",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(destinationPath);
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus("已完成，但无法打开目标文件夹。", StatusTone.Warning);
            Debug.WriteLine(ex);
            return false;
        }
    }

    private async void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !ViewModel.IsRunning)
        {
            return;
        }

        args.Cancel = true;
        if (!ViewModel.CanCancel)
        {
            ViewModel.SetStatus("正在完成安全操作，请稍候。", StatusTone.Warning);
            return;
        }

        if (_closePromptShowing)
        {
            return;
        }

        _closePromptShowing = true;
        try
        {
            ContentDialog dialog = new()
            {
                Title = "确认退出",
                Content = "提取正在进行，是否取消提取并退出？",
                PrimaryButtonText = "取消提取并退出",
                CloseButtonText = "继续提取",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            ViewModel.RequestCancellation();
            if (_workflowTask is not null)
            {
                await _workflowTask;
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            _closePromptShowing = false;
        }
    }
}
