using Microsoft.VisualBasic.FileIO;

namespace ExtractAndDelete.Core;
public class CleanupService
{
    public static CleanupResult MoveToRecycleBin(string filePath)
    {
        // 判断文件是否存在
        if (!File.Exists(filePath))
        {
            return new CleanupResult
            {
                Success = false,
                ErrorMessage = "File doesn't exist."
            };
        }

        // 将文件移入回收站
        try
        {
            FileSystem.DeleteFile(
                filePath,
                UIOption.OnlyErrorDialogs, // 正常移入回收站时不弹出窗口
                RecycleOption.SendToRecycleBin, // 将文件移入回收站
                UICancelOption.ThrowException // 如果用户取消，则抛出异常
            );
        }
        catch (Exception ex)
        {
            return new CleanupResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        return new CleanupResult
        {
            Success = true,
            ErrorMessage = null
        };
    }
}
