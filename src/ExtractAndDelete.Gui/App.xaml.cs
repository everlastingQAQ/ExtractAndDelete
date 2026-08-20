using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using ExtractAndDelete.Gui.ViewModels;

namespace ExtractAndDelete.Gui;

public partial class App : Application
{
    private static readonly AppInstance MainInstance =
        AppInstance.FindOrRegisterForKey("ExtractAndDelete.Main");

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        if (MainInstance.IsCurrent)
        {
            MainInstance.Activated += MainInstance_Activated;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!MainInstance.IsCurrent)
        {
            AppActivationArguments? activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation is not null)
            {
                MainInstance.RedirectActivationToAsync(activation)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            Process.GetCurrentProcess().Kill();
            return;
        }

        _window ??= new MainWindow();
        _window.Activate();
        _window.HandleActivation(
            CommandLineActivation.TryGetArchivePath(args.Arguments)
            ?? CommandLineActivation.TryGetArchivePath());
    }

    private void MainInstance_Activated(object? sender, AppActivationArguments args)
    {
        DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The application dispatcher is unavailable.");
        dispatcherQueue.TryEnqueue(() =>
        {
            _window ??= new MainWindow();
            _window.Activate();
            string? arguments = args.Data is Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launch
                ? launch.Arguments
                : null;
            _window.HandleActivation(
                CommandLineActivation.TryGetArchivePath(arguments)
                ?? CommandLineActivation.TryGetArchivePath());
        });
    }
}
