using System.Runtime.InteropServices;

namespace ExtractAndDelete.Core;

public interface IDestinationPublisher
{
    Task<DestinationPublishResult> PublishAsync(
        string stagingPath,
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken);
}

// Optional policy information used by ExtractionService without making the
// public publisher contract depend on a particular UI or host.
public interface IDestinationPublisherPolicy
{
    bool AllowsExistingDirectory { get; }
    bool SupportsCancellation { get; }
}

public sealed class AtomicDirectoryPublisher : IDestinationPublisher, IDestinationPublisherPolicy
{
    bool IDestinationPublisherPolicy.AllowsExistingDirectory => false;
    bool IDestinationPublisherPolicy.SupportsCancellation => false;

    public Task<DestinationPublishResult> PublishAsync(
        string stagingPath,
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            {
                return Task.FromResult(Failure(
                    ErrorCode.PublishFailure,
                    "目标目录已存在，命令行模式不会合并或覆盖现有内容。"));
            }

            progress?.Report(new ExtractionProgress(
                WorkflowStage.Publishing,
                null,
                0,
                0,
                0,
                0,
                CanCancel: false,
                IsIndeterminate: true));

            Directory.Move(stagingPath, destinationPath);
            return Task.FromResult(new DestinationPublishResult(
                DestinationPublishOutcome.Completed,
                ErrorCode.None,
                "目标目录已原子发布。",
                DestinationState.Completed,
                1,
                0,
                0));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new DestinationPublishResult(
                DestinationPublishOutcome.Cancelled,
                ErrorCode.Cancelled,
                "用户已取消发布。",
                DestinationState.Unchanged,
                0,
                0,
                0));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Failure(
                ErrorCode.PublishFailure,
                "无法发布解压目录，源压缩包已保留。",
                ex.ToString()));
        }
    }

    private static DestinationPublishResult Failure(
        ErrorCode errorCode,
        string userMessage,
        string? diagnosticMessage = null) =>
        new(
            DestinationPublishOutcome.Failed,
            errorCode,
            userMessage,
            DestinationState.Unchanged,
            0,
            0,
            1,
            diagnosticMessage);
}

/// <summary>
/// Publishes staged archive contents through the supported Windows Shell
/// IFileOperation API. The implementation deliberately leaves confirmation,
/// conflict, error and elevation UI enabled so that the user sees the same
/// decisions as a normal Windows file operation.
/// </summary>
public sealed class WindowsShellDestinationPublisher : IDestinationPublisher, IDestinationPublisherPolicy
{
    private const uint FOFX_SHOWELEVATIONPROMPT = 0x00040000;
    private const uint FOFX_NOCOPYSECURITYATTRIBS = 0x00000800;
    private const uint FOFX_EARLYFAILURE = 0x00100000;

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_ABORT = unchecked((int)0x80004004);
    private const int HRESULT_FROM_WIN32_ERROR_CANCELLED = unchecked((int)0x800704C7);
    private const int COPYENGINE_S_MERGE = 0x00040101;
    private const int COPYENGINE_S_KEEP_BOTH = 0x00040102;
    private const int COPYENGINE_S_COLLISIONRESOLVED = 0x00040103;
    private const int COPYENGINE_S_ALREADY_DONE = 0x00040104;
    private const int COPYENGINE_S_USER_IGNORED = 0x00040105;
    private const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80040104);

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint CLSCTX_LOCAL_SERVER = 0x4;

    private static readonly Guid FileOperationClassId =
        new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid FileOperationInterfaceId =
        new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    private static readonly Guid ShellItemInterfaceId =
        new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    private readonly IntPtr _ownerWindow;

    public WindowsShellDestinationPublisher(IntPtr ownerWindow)
    {
        _ownerWindow = ownerWindow;
    }

    bool IDestinationPublisherPolicy.AllowsExistingDirectory => true;
    bool IDestinationPublisherPolicy.SupportsCancellation => true;

    public Task<DestinationPublishResult> PublishAsync(
        string stagingPath,
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stagingPath)
            || string.IsNullOrWhiteSpace(destinationPath))
        {
            return Task.FromResult(Failure(
                ErrorCode.InvalidDestination,
                "目标路径无效。"));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Cancelled("用户已取消发布。"));
        }

        return Task.Run(() => RunOnDedicatedSta(
            stagingPath,
            destinationPath,
            progress,
            cancellationToken));
    }

    private DestinationPublishResult RunOnDedicatedSta(
        string stagingPath,
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        DestinationPublishResult? result = null;
        Exception? threadException = null;
        Thread thread = new(() =>
        {
            try
            {
                result = PublishOnSta(
                    stagingPath,
                    destinationPath,
                    progress,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        })
        {
            IsBackground = true,
            Name = "ExtractAndDelete-WindowsPublish"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            return Failure(
                ErrorCode.PublishFailure,
                "Windows 文件操作无法发布解压内容，源压缩包已保留。",
                threadException.ToString());
        }

        return result ?? Failure(
            ErrorCode.PublishFailure,
            "Windows 文件操作没有返回结果，源压缩包已保留。");
    }

    private DestinationPublishResult PublishOnSta(
        string stagingPath,
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        int coInitializeHr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        bool shouldUninitialize = coInitializeHr >= 0;
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled("用户已取消发布。");
            }

            if (!Directory.Exists(stagingPath))
            {
                return Failure(ErrorCode.PublishFailure, "解压临时目录不存在，源压缩包已保留。");
            }

            DestinationPublishResult? createResult = EnsureDestinationDirectory(
                destinationPath,
                progress,
                cancellationToken);
            if (createResult is not null)
            {
                return createResult;
            }

            IShellItem? destinationItem = null;
            IFileOperation? operation = null;
            FileOperationProgressSink? sink = null;
            uint adviseCookie = 0;
            bool advised = false;
            try
            {
                Guid shellItemIid = ShellItemInterfaceId;
                int hr = SHCreateItemFromParsingName(
                    destinationPath,
                    IntPtr.Zero,
                    ref shellItemIid,
                    out destinationItem);
                if (hr < 0 || destinationItem is null)
                {
                    return Failure(
                        ErrorCode.DestinationCreationFailed,
                        "无法打开目标目录，源压缩包已保留。",
                        $"SHCreateItemFromParsingName HRESULT: 0x{hr:X8}");
                }

                operation = CreateFileOperation(out hr);
                if (hr < 0 || operation is null)
                {
                    return Failure(
                        ErrorCode.PublishFailure,
                        "无法启动 Windows 文件操作，源压缩包已保留。",
                        $"IFileOperation activation HRESULT: 0x{hr:X8}");
                }

                hr = operation.SetOwnerWindow(_ownerWindow);
                if (hr < 0)
                {
                    return Failure(ErrorCode.PublishFailure, "无法绑定 Windows 文件操作窗口，源压缩包已保留。", $"SetOwnerWindow HRESULT: 0x{hr:X8}");
                }

                uint flags = FOFX_SHOWELEVATIONPROMPT
                    | FOFX_NOCOPYSECURITYATTRIBS
                    | FOFX_EARLYFAILURE;
                hr = operation.SetOperationFlags(flags);
                if (hr < 0)
                {
                    return Failure(ErrorCode.PublishFailure, "无法配置 Windows 文件操作，源压缩包已保留。", $"SetOperationFlags HRESULT: 0x{hr:X8}");
                }

                sink = new FileOperationProgressSink(progress, cancellationToken);
                hr = operation.Advise(sink, out adviseCookie);
                if (hr < 0)
                {
                    return Failure(ErrorCode.PublishFailure, "无法监听 Windows 文件操作结果，源压缩包已保留。", $"Advise HRESULT: 0x{hr:X8}");
                }
                advised = true;

                string[] entries = Directory.GetFileSystemEntries(stagingPath);
                sink.SetTotal(entries.Length);
                foreach (string entryPath in entries)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Cancelled("用户已取消发布。");
                    }

                    IShellItem? sourceItem = null;
                    try
                    {
                        hr = SHCreateItemFromParsingName(
                            entryPath,
                            IntPtr.Zero,
                            ref shellItemIid,
                            out sourceItem);
                        if (hr < 0 || sourceItem is null)
                        {
                            sink.RecordFailure(hr, entryPath);
                            continue;
                        }

                        hr = operation.CopyItem(sourceItem, destinationItem, null, IntPtr.Zero);
                        if (hr < 0)
                        {
                            sink.RecordFailure(hr, entryPath);
                        }
                    }
                    finally
                    {
                        ReleaseCom(sourceItem);
                    }
                }

                hr = operation.PerformOperations();
                int performHr = hr;
                int abortedHr = operation.GetAnyOperationsAborted(out int aborted);
                DestinationPublishResult classified = sink.BuildResult(
                    performHr,
                    abortedHr,
                    aborted != 0,
                    cancellationToken.IsCancellationRequested);
                return classified;
            }
            finally
            {
                if (advised && operation is not null)
                {
                    _ = operation.Unadvise(adviseCookie);
                }

                ReleaseCom(sink);
                ReleaseCom(operation);
                ReleaseCom(destinationItem);
            }
        }
        catch (OperationCanceledException)
        {
            return Cancelled("用户已取消发布。");
        }
        catch (COMException ex)
        {
            return Failure(
                ErrorCode.PublishFailure,
                "Windows 文件操作失败，源压缩包已保留。",
                ex.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(
                ErrorCode.PublishFailure,
                "无法发布解压内容，源压缩包已保留。",
                ex.ToString());
        }
        finally
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }

    private DestinationPublishResult? EnsureDestinationDirectory(
        string destinationPath,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            return Failure(ErrorCode.InvalidDestination, "目标路径是文件，不是文件夹。源压缩包已保留。");
        }

        if (Directory.Exists(destinationPath))
        {
            return null;
        }

        string? existingParent = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(existingParent))
        {
            return Failure(ErrorCode.InvalidDestination, "目标路径无效，源压缩包已保留。");
        }

        List<string> missingParts = new();
        string current = destinationPath;
        while (!Directory.Exists(current))
        {
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(ErrorCode.DestinationCreationFailed, "无法找到目标路径的可用父目录，源压缩包已保留。");
            }

            missingParts.Add(Path.GetFileName(current));
            current = parent;
        }

        missingParts.Reverse();
        foreach (string part in missingParts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled("用户已取消创建目标目录。");
            }

            DestinationPublishResult result = CreateDirectoryWithShell(
                current,
                part,
                progress,
                cancellationToken);
            if (result.Outcome != DestinationPublishOutcome.Completed)
            {
                return result;
            }

            current = Path.Combine(current, part);
        }

        return null;
    }

    private DestinationPublishResult CreateDirectoryWithShell(
        string parentPath,
        string name,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        IShellItem? parentItem = null;
        IFileOperation? operation = null;
        FileOperationProgressSink? sink = null;
        uint cookie = 0;
        bool advised = false;
        try
        {
            Guid shellItemIid = ShellItemInterfaceId;
            int hr = SHCreateItemFromParsingName(parentPath, IntPtr.Zero, ref shellItemIid, out parentItem);
            if (hr < 0 || parentItem is null)
            {
                return Failure(ErrorCode.DestinationCreationFailed, "无法打开目标父目录，源压缩包已保留。", $"SHCreateItemFromParsingName HRESULT: 0x{hr:X8}");
            }

            operation = CreateFileOperation(out hr);
            if (hr < 0 || operation is null)
            {
                return Failure(ErrorCode.DestinationCreationFailed, "无法创建目标目录，源压缩包已保留。", $"IFileOperation activation HRESULT: 0x{hr:X8}");
            }

            _ = operation.SetOwnerWindow(_ownerWindow);
            _ = operation.SetOperationFlags(FOFX_SHOWELEVATIONPROMPT | FOFX_EARLYFAILURE);
            sink = new FileOperationProgressSink(progress, cancellationToken);
            hr = operation.Advise(sink, out cookie);
            if (hr < 0)
            {
                return Failure(ErrorCode.DestinationCreationFailed, "无法监听目标目录创建，源压缩包已保留。", $"Advise HRESULT: 0x{hr:X8}");
            }
            advised = true;
            hr = operation.NewItem(parentItem, FILE_ATTRIBUTE_DIRECTORY, name, null, IntPtr.Zero);
            if (hr < 0)
            {
                return Failure(ErrorCode.DestinationCreationFailed, "无法创建目标目录，源压缩包已保留。", $"NewItem HRESULT: 0x{hr:X8}");
            }

            int performHr = operation.PerformOperations();
            int abortedHr = operation.GetAnyOperationsAborted(out int aborted);
            if (cancellationToken.IsCancellationRequested || aborted != 0)
            {
                return Cancelled("用户已取消创建目标目录。");
            }
            if (performHr < 0 || abortedHr < 0)
            {
                return Failure(ErrorCode.DestinationCreationFailed, "Windows 无法创建目标目录，源压缩包已保留。", $"Perform HRESULT: 0x{performHr:X8}; aborted HRESULT: 0x{abortedHr:X8}");
            }

            return new DestinationPublishResult(
                DestinationPublishOutcome.Completed,
                ErrorCode.None,
                "目标目录已创建。",
                DestinationState.Completed,
                1,
                0,
                0);
        }
        finally
        {
            if (advised && operation is not null)
            {
                _ = operation.Unadvise(cookie);
            }
            ReleaseCom(sink);
            ReleaseCom(operation);
            ReleaseCom(parentItem);
        }
    }

    private static IFileOperation? CreateFileOperation(out int hr)
    {
        Guid classId = FileOperationClassId;
        Guid interfaceId = FileOperationInterfaceId;
        hr = CoCreateInstance(
            ref classId,
            IntPtr.Zero,
            CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER,
            ref interfaceId,
            out IFileOperation operation);
        return hr < 0 ? null : operation;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static DestinationPublishResult Cancelled(string message) =>
        new(
            DestinationPublishOutcome.Cancelled,
            ErrorCode.DestinationPublishAborted,
            message,
            DestinationState.PartiallyModified,
            0,
            0,
            0);

    private static DestinationPublishResult Failure(
        ErrorCode code,
        string message,
        string? diagnostic = null) =>
        new(
            DestinationPublishOutcome.Failed,
            code,
            message,
            DestinationState.Unchanged,
            0,
            0,
            1,
            diagnostic);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IFileOperation ppv);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private const uint COINIT_APARTMENTTHREADED = 0x2;

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise([MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(IntPtr popd);
        [PreserveSig] int SetProperties(IntPtr pproparray);
        [PreserveSig] int SetOwnerWindow(IntPtr hwndOwner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr punkItems);
        [PreserveSig] int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        [PreserveSig] int RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        [PreserveSig] int MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
        [PreserveSig] int CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
        [PreserveSig] int DeleteItems(IntPtr punkItems);
        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted(out int pfAnyOperationsAborted);
    }

    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hrResult);
        [PreserveSig] int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem? psiNewItem);
        [PreserveSig] int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IShellItem? psiNewItem);
        [PreserveSig] int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IShellItem? psiNewItem);
        [PreserveSig] int PreDeleteItem(uint dwFlags, IShellItem psiItem);
        [PreserveSig] int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewItem);
        [PreserveSig] int PreNewItem(uint dwFlags, IShellItem psiParentFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszName);
        [PreserveSig] int PostNewItem(uint dwFlags, IShellItem psiParentFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem);
        [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true)]
    private sealed class FileOperationProgressSink : IFileOperationProgressSink
    {
        private readonly IProgress<ExtractionProgress>? _progress;
        private readonly CancellationToken _cancellationToken;
        private int _total;
        private int _succeeded;
        private int _skipped;
        private int _failed;
        private bool _modified;
        private readonly List<string> _diagnostics = new();

        public FileOperationProgressSink(
            IProgress<ExtractionProgress>? progress,
            CancellationToken cancellationToken)
        {
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        public void SetTotal(int total) => _total = total;

        public void RecordFailure(int hr, string path)
        {
            _failed++;
            _diagnostics.Add($"{path}: HRESULT 0x{hr:X8}");
        }

        public int StartOperations() => IsCancelled ? E_ABORT : S_OK;

        public int FinishOperations(int hrResult)
        {
            if (hrResult < 0 && hrResult != E_ABORT && hrResult != COPYENGINE_E_USER_CANCELLED)
            {
                _failed++;
                _diagnostics.Add($"FinishOperations HRESULT 0x{hrResult:X8}");
            }
            return S_OK;
        }

        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) => CancellationResult();
        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName, int hrRename, IShellItem? psiNewItem) => S_OK;
        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, string? pszNewName) => CancellationResult();
        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, string? pszNewName, int hrMove, IShellItem? psiNewItem) => S_OK;
        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, string? pszNewName) => CancellationResult();

        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiParentFolder, string? pszNewName, int hrCopy, IShellItem? psiNewItem)
        {
            if (IsCancelled)
            {
                return E_ABORT;
            }

            _modified = true;
            switch (hrCopy)
            {
                case S_OK:
                case S_FALSE:
                case COPYENGINE_S_MERGE:
                case COPYENGINE_S_KEEP_BOTH:
                case COPYENGINE_S_COLLISIONRESOLVED:
                case COPYENGINE_S_ALREADY_DONE:
                    _succeeded++;
                    break;
                case COPYENGINE_S_USER_IGNORED:
                    _skipped++;
                    break;
                default:
                    _failed++;
                    _diagnostics.Add($"CopyItem HRESULT 0x{hrCopy:X8}");
                    break;
            }

            ReportProgress();
            return S_OK;
        }

        public int PreDeleteItem(uint dwFlags, IShellItem psiItem) => CancellationResult();
        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewItem) => S_OK;
        public int PreNewItem(uint dwFlags, IShellItem psiParentFolder, string pszName) => CancellationResult();
        public int PostNewItem(uint dwFlags, IShellItem psiParentFolder, string pszName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem) => S_OK;

        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar)
        {
            if (IsCancelled)
            {
                return E_ABORT;
            }

            _progress?.Report(new ExtractionProgress(
                WorkflowStage.Publishing,
                null,
                (int)Math.Min(iWorkSoFar, int.MaxValue),
                (int)Math.Min(iWorkTotal, int.MaxValue),
                0,
                0,
                CanCancel: true,
                IsIndeterminate: iWorkTotal == 0));
            return S_OK;
        }

        public int ResetTimer() => S_OK;
        public int PauseTimer() => S_OK;
        public int ResumeTimer() => S_OK;

        public DestinationPublishResult BuildResult(
            int performHr,
            int abortedHr,
            bool aborted,
            bool cancellationRequested)
        {
            bool cancelled = cancellationRequested
                || aborted
                || performHr == E_ABORT
                || performHr == COPYENGINE_E_USER_CANCELLED;
            if (cancelled)
            {
                return new DestinationPublishResult(
                    DestinationPublishOutcome.Cancelled,
                    ErrorCode.DestinationPublishAborted,
                    "Windows 文件操作已取消，目标目录可能包含部分内容；源压缩包已保留。",
                    _modified ? DestinationState.PartiallyModified : DestinationState.Unchanged,
                    _succeeded,
                    _skipped,
                    _failed,
                    Diagnostics(performHr, abortedHr));
            }

            if (_failed > 0 || performHr < 0 || abortedHr < 0)
            {
                return new DestinationPublishResult(
                    DestinationPublishOutcome.Failed,
                    ErrorCode.PublishFailure,
                    "Windows 文件操作失败，目标目录可能包含部分内容；源压缩包已保留。",
                    _modified ? DestinationState.PartiallyModified : DestinationState.Unchanged,
                    _succeeded,
                    _skipped,
                    _failed,
                    Diagnostics(performHr, abortedHr));
            }

            if (_skipped > 0)
            {
                return new DestinationPublishResult(
                    DestinationPublishOutcome.CompletedWithSkippedItems,
                    ErrorCode.DestinationItemsSkipped,
                    "部分文件已跳过，目标目录保留已完成内容；源压缩包已保留。",
                    DestinationState.CompletedWithSkippedItems,
                    _succeeded,
                    _skipped,
                    0,
                    Diagnostics(performHr, abortedHr));
            }

            return new DestinationPublishResult(
                DestinationPublishOutcome.Completed,
                ErrorCode.None,
                "目标内容已完整发布。",
                DestinationState.Completed,
                _succeeded,
                0,
                0,
                Diagnostics(performHr, abortedHr));
        }

        private bool IsCancelled => _cancellationToken.IsCancellationRequested;

        private int CancellationResult() => IsCancelled ? E_ABORT : S_OK;

        private void ReportProgress() =>
            _progress?.Report(new ExtractionProgress(
                WorkflowStage.Publishing,
                null,
                _succeeded + _skipped + _failed,
                _total,
                0,
                0,
                CanCancel: true,
                IsIndeterminate: _total == 0));

        private string? Diagnostics(int performHr, int abortedHr)
        {
            List<string> values = new(_diagnostics);
            if (performHr != S_OK)
            {
                values.Add($"PerformOperations HRESULT 0x{performHr:X8}");
            }
            if (abortedHr != S_OK)
            {
                values.Add($"GetAnyOperationsAborted HRESULT 0x{abortedHr:X8}");
            }
            return values.Count == 0 ? null : string.Join(Environment.NewLine, values);
        }
    }
}
