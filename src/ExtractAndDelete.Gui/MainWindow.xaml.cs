using ExtractAndDelete.Core;
using ExtractAndDelete.Gui.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ExtractAndDelete.Gui;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += MainWindow_Closed;
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
        picker.FileTypeFilter.Add(".zip");
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
        await ViewModel.ExecuteAsync();
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

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ViewModel.IsRunning && ViewModel.CanCancel)
        {
            args.Handled = true;
            ViewModel.SetStatus("正在取消解压并清理临时目录，请稍候。", StatusTone.Warning);
            ViewModel.RequestCancellation();
        }
        else if (ViewModel.IsRunning)
        {
            args.Handled = true;
            ViewModel.SetStatus("正在完成安全操作，请稍候。", StatusTone.Warning);
        }
    }
}
