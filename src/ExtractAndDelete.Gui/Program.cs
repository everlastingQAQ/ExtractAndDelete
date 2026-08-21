using ExtractAndDelete.Gui.ViewModels;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Windows.Forms;
using Windows.ApplicationModel.Activation;

namespace ExtractAndDelete.Gui;

internal static class Program
{
    private const string MainInstanceKey = "ExtractAndDelete.Main";

    [STAThread]
    private static void Main()
    {
        // These calls must run before any HWND, picker, or control is created.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppActivationArguments? activation = null;
        try
        {
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unable to read initial activation arguments: {ex}");
        }

        AppInstance mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            try
            {
                if (activation is not null)
                {
                    mainInstance.RedirectActivationToAsync(activation)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to redirect activation: {ex}");
            }

            return;
        }

        using ExtractionWizardForm form = new();
        mainInstance.Activated += (_, args) =>
        {
            string? archivePath = TryGetArchivePath(args);
            try
            {
                if (form.IsDisposed)
                {
                    return;
                }

                form.BeginInvoke(() => form.HandleActivation(archivePath));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                Debug.WriteLine($"Unable to deliver redirected activation: {ex}");
            }
        };

        string? initialArchivePath = TryGetArchivePath(activation)
            ?? CommandLineActivation.TryGetArchivePath();
        form.Shown += (_, _) => form.HandleActivation(initialArchivePath);
        Application.Run(form);
    }

    private static string? TryGetArchivePath(AppActivationArguments? activation)
    {
        if (activation?.Data is LaunchActivatedEventArgs launch)
        {
            return CommandLineActivation.TryGetArchivePath(launch.Arguments);
        }

        return null;
    }
}
