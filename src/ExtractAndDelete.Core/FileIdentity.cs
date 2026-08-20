using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ExtractAndDelete.Core;

internal sealed record FileIdentity(
    uint VolumeSerialNumber,
    ulong FileIndex,
    ulong FileSize,
    long LastWriteTimeUtcTicks);

internal static class FileIdentityReader
{
    public static FileIdentity? TryRead(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                options: FileOptions.SequentialScan);

            if (!GetFileInformationByHandle(
                    stream.SafeFileHandle,
                    out ByHandleFileInformation info))
            {
                return null;
            }

            ulong fileIndex = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            ulong fileSize = ((ulong)info.FileSizeHigh << 32) | info.FileSizeLow;
            long lastWriteFileTime = ((long)(uint)info.LastWriteTime.dwHighDateTime << 32)
                | (uint)info.LastWriteTime.dwLowDateTime;
            long lastWriteTicks = DateTime.FromFileTimeUtc(lastWriteFileTime).Ticks;
            return new FileIdentity(
                info.VolumeSerialNumber,
                fileIndex,
                fileSize,
                lastWriteTicks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
