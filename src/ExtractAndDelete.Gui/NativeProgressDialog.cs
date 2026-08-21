using System.Runtime.InteropServices;

namespace ExtractAndDelete.Gui;

internal interface IExtractionProgressUi : IDisposable
{
    bool IsCancellationRequested { get; }

    void Start(nint owner);

    void Report(string? currentEntry, long completedBytes, long totalBytes,
        int completedEntries, int totalEntries);

    void Stop();
}

internal sealed class NativeProgressDialog : IExtractionProgressUi
{
    private const uint ProgressDialogNormal = 0x0000;
    private const uint ProgressDialogModal = 0x0001;
    private const uint ProgressDialogAutoTime = 0x0002;
    private const uint ProgressDialogNoMinimize = 0x0008;

    private IProgressDialog? _dialog;
    private bool _started;
    private bool _disposed;

    public bool IsCancellationRequested
    {
        get
        {
            try
            {
                return _dialog is not null && _dialog.HasUserCancelled() != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Start(nint owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        Type progressDialogType = Type.GetTypeFromCLSID(
            new Guid("F8383852-FCD3-11D1-A6B9-006097DF5BD4"),
            throwOnError: true)!;
        _dialog = (IProgressDialog)Activator.CreateInstance(progressDialogType)!;
        TryCall(() => _dialog.SetTitle("正在提取压缩文件夹"));
        TryCall(() => _dialog.SetCancelMsg("正在取消并清理临时文件，请稍候。", nint.Zero));
        TryCall(() => _dialog.StartProgressDialog(
            owner,
            null,
            ProgressDialogNormal | ProgressDialogModal | ProgressDialogAutoTime | ProgressDialogNoMinimize,
            nint.Zero));
        _started = true;
    }

    public void Report(string? currentEntry, long completedBytes, long totalBytes,
        int completedEntries, int totalEntries)
    {
        if (!_started || _dialog is null)
        {
            return;
        }

        string line = string.IsNullOrWhiteSpace(currentEntry)
            ? "正在提取文件……"
            : currentEntry;
        TryCall(() => _dialog.SetLine(1, line, false, nint.Zero));

        if (totalBytes > 0)
        {
            TryCall(() => _dialog.SetProgress64(
                (ulong)Math.Max(0, completedBytes),
                (ulong)Math.Max(1, totalBytes)));
        }
        else
        {
            TryCall(() => _dialog.SetProgress(
                (uint)Math.Clamp(completedEntries, 0, int.MaxValue),
                (uint)Math.Max(1, totalEntries)));
        }
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        if (_dialog is not null)
        {
            TryCall(_dialog.StopProgressDialog);
        }
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        if (_dialog is not null)
        {
            Marshal.FinalReleaseComObject(_dialog);
            _dialog = null;
        }

        _disposed = true;
    }

    private static void TryCall(Func<int> call)
    {
        try
        {
            _ = call();
        }
        catch (COMException)
        {
            // Progress UI is advisory. Core cancellation and cleanup remain authoritative.
        }
        catch (InvalidComObjectException)
        {
        }
    }

    [ComImport]
    [Guid("EBBC7C04-315E-11D2-B62F-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProgressDialog
    {
        int StartProgressDialog(nint hwndParent, [MarshalAs(UnmanagedType.IUnknown)] object? punkEnableModless,
            uint dwFlags, nint pvReserved);

        int StopProgressDialog();

        int SetAnimation(nint hInstAnimation, ushort idAnimation);

        int HasUserCancelled();

        int SetLine(uint dwLineNum, [MarshalAs(UnmanagedType.LPWStr)] string pwzString,
            [MarshalAs(UnmanagedType.Bool)] bool fCompactPath, nint pvReserved);

        int SetProgress(uint dwCompleted, uint dwTotal);

        int SetProgress64(ulong ullCompleted, ulong ullTotal);

        int Timer(uint dwTimerAction, nint pvReserved);

        int SetCancelMsg([MarshalAs(UnmanagedType.LPWStr)] string pwzCancelMsg, nint pvReserved);

        int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pwzTitle);
    }
}
