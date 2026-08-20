using ExtractAndDelete.Gui.ViewModels;
using ExtractAndDelete.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System.ComponentModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ExtractAndDelete.Gui;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private Task? _workflowTask;
    private bool _allowClose;
    private bool _closePromptShowing;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppWindow.Closing += MainWindow_Closing;
    }

    public void HandleActivation(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
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

    private async void ChooseArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new();
        foreach (string extension in SupportedArchiveFormats.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.SetArchive(file.Path);
        }
    }

    private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.SetParentDirectory(folder.Path);
        }
    }

    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        _workflowTask = ViewModel.ExecuteAsync();
        try
        {
            await _workflowTask;
        }
        finally
        {
            _workflowTask = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RequestCancellation();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // x:Bind OneWay bindings are refreshed through this notification source.
        Bindings.Update();
        StatusTextBlock.Foreground = ViewModel.StatusTone switch
        {
            StatusTone.Success => new SolidColorBrush(Colors.ForestGreen),
            StatusTone.Warning => new SolidColorBrush(Colors.DarkGoldenrod),
            StatusTone.Error => new SolidColorBrush(Colors.Firebrick),
            _ => new SolidColorBrush(Colors.Gray)
        };
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
                Content = "解压正在进行，是否取消解压并退出？",
                PrimaryButtonText = "取消解压并退出",
                CloseButtonText = "继续解压",
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
