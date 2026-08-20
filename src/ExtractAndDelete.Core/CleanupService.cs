using System.Runtime.InteropServices;

namespace ExtractAndDelete.Core;

public interface ICleanupService
{
    Task<CleanupResult> MoveToRecycleBinAsync(string filePath);
}

/// <summary>
/// Moves files to the Windows Recycle Bin without elevation or permanent-delete fallback.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_NOCONFIRMMKDIR = 0x0200;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOFX_EARLYFAILURE = 0x00100000;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;

    private const uint OperationFlags =
        FOF_SILENT
        | FOF_NOCONFIRMATION
        | FOF_NOCONFIRMMKDIR
        | FOF_NOERRORUI
        | FOFX_EARLYFAILURE
        | FOFX_RECYCLEONDELETE;

    private static readonly Guid FileOperationClassId =
        new("3ad05575-8857-4850-9277-11b85bdb8e09");

    private static readonly Guid ShellItemInterfaceId =
        new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public Task<CleanupResult> MoveToRecycleBinAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(new CleanupResult(
                false,
                ErrorCode.RecycleUnavailable,
                "源压缩包路径无效。"));
        }

        if (!File.Exists(filePath))
        {
            return Task.FromResult(new CleanupResult(
                false,
                ErrorCode.RecycleUnavailable,
                "源压缩包不存在，无法移入回收站。"));
        }

        return Task.Run(() => RunOnDedicatedSta(filePath));
    }

    private static CleanupResult RunOnDedicatedSta(string filePath)
    {
        CleanupResult? result = null;
        Exception? threadException = null;

        Thread thread = new(() =>
        {
            try
            {
                result = MoveOnSta(filePath);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        })
        {
            IsBackground = true,
            Name = "ExtractAndDelete-RecycleBin"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
        {
            return new CleanupResult(
                false,
                ErrorCode.RecycleFailed,
                "源压缩包无法移入回收站，源文件已保留。",
                threadException.ToString());
        }

        return result ?? new CleanupResult(
            false,
            ErrorCode.RecycleFailed,
            "源压缩包无法移入回收站，源文件已保留。");
    }

    private static CleanupResult MoveOnSta(string filePath)
    {
        object? operationObject = null;
        IShellItem? shellItem = null;

        try
        {
            Guid shellItemIid = ShellItemInterfaceId;
            int shellItemHr = SHCreateItemFromParsingName(
                filePath,
                IntPtr.Zero,
                ref shellItemIid,
                out shellItem);
            if (shellItemHr < 0 || shellItem is null)
            {
                return Failure(
                    ErrorCode.RecycleUnavailable,
                    "当前文件所在位置不支持回收站操作，源文件已保留。",
                    $"SHCreateItemFromParsingName HRESULT: 0x{shellItemHr:X8}");
            }

            Type? operationType = Type.GetTypeFromCLSID(FileOperationClassId);
            if (operationType is null)
            {
                return Failure(
                    ErrorCode.RecycleUnavailable,
                    "当前系统不支持回收站操作，源文件已保留。",
                    "IFileOperation class is unavailable.");
            }

            operationObject = Activator.CreateInstance(operationType);
            if (operationObject is not IFileOperation operation)
            {
                return Failure(
                    ErrorCode.RecycleUnavailable,
                    "当前系统不支持回收站操作，源文件已保留。",
                    "IFileOperation COM activation returned an unexpected object.");
            }

            int hr = operation.SetOperationFlags(OperationFlags);
            if (hr < 0)
            {
                return Failure(
                    ErrorCode.RecycleUnavailable,
                    "无法配置回收站操作，源文件已保留。",
                    $"SetOperationFlags HRESULT: 0x{hr:X8}");
            }

            hr = operation.DeleteItem(shellItem, IntPtr.Zero);
            if (hr < 0)
            {
                return Failure(
                    ErrorCode.RecycleUnavailable,
                    "无法准备回收站操作，源文件已保留。",
                    $"DeleteItem HRESULT: 0x{hr:X8}");
            }

            hr = operation.PerformOperations();
            if (hr < 0)
            {
                return Failure(
                    ErrorCode.RecycleFailed,
                    "源压缩包无法移入回收站，源文件已保留。",
                    $"PerformOperations HRESULT: 0x{hr:X8}");
            }

            hr = operation.GetAnyOperationsAborted(out int aborted);
            if (hr < 0 || aborted != 0)
            {
                return Failure(
                    ErrorCode.RecycleFailed,
                    "源压缩包无法移入回收站，源文件已保留。",
                    $"GetAnyOperationsAborted HRESULT: 0x{hr:X8}; aborted: {aborted}.");
            }

            if (File.Exists(filePath))
            {
                return Failure(
                    ErrorCode.RecycleFailed,
                    "源压缩包无法移入回收站，源文件已保留。",
                    "The source path still exists after IFileOperation completed.");
            }

            return new CleanupResult(true, ErrorCode.None, "源压缩包已移入回收站。");
        }
        catch (COMException ex)
        {
            return Failure(
                ErrorCode.RecycleFailed,
                "源压缩包无法移入回收站，源文件已保留。",
                ex.ToString());
        }
        catch (Exception ex)
        {
            return Failure(
                ErrorCode.RecycleFailed,
                "源压缩包无法移入回收站，源文件已保留。",
                ex.ToString());
        }
        finally
        {
            if (shellItem is not null && Marshal.IsComObject(shellItem))
            {
                Marshal.FinalReleaseComObject(shellItem);
            }

            if (operationObject is not null && Marshal.IsComObject(operationObject))
            {
                Marshal.FinalReleaseComObject(operationObject);
            }
        }
    }

    private static CleanupResult Failure(
        ErrorCode errorCode,
        string userMessage,
        string diagnosticMessage) =>
        new(false, errorCode, userMessage, diagnosticMessage);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig]
        int Advise(IntPtr pfops, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int SetOperationFlags(uint dwOperationFlags);

        [PreserveSig]
        int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

        [PreserveSig]
        int SetProgressDialog(IntPtr popd);

        [PreserveSig]
        int SetProperties(IntPtr pproparray);

        [PreserveSig]
        int ApplyPropertiesToItem(IShellItem psiItem, IntPtr pproparray);

        [PreserveSig]
        int ApplyPropertiesToItems(IntPtr punkItems, IntPtr pproparray);

        [PreserveSig]
        int RenameItem(
            IShellItem psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
            IntPtr pfopsItem);

        [PreserveSig]
        int RenameItems(
            IntPtr pUnkItems,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig]
        int MoveItem(
            IShellItem psiItem,
            IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            IntPtr pfopsItem);

        [PreserveSig]
        int MoveItems(
            IntPtr punkItems,
            IShellItem psiDestinationFolder,
            IntPtr pfopsItem);

        [PreserveSig]
        int CopyItem(
            IShellItem psiItem,
            IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            IntPtr pfopsItem);

        [PreserveSig]
        int CopyItems(
            IntPtr punkItems,
            IShellItem psiDestinationFolder,
            IntPtr pfopsItem);

        [PreserveSig]
        int DeleteItem(IShellItem psiItem, IntPtr pfopsItem);

        [PreserveSig]
        int DeleteItems(IntPtr punkItems);

        [PreserveSig]
        int NewItem(
            IShellItem psiDestinationFolder,
            uint dwFileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName,
            IntPtr pfopsItem);

        [PreserveSig]
        int PerformOperations();

        [PreserveSig]
        int GetAnyOperationsAborted(out int pfAnyOperationsAborted);
    }
}
