namespace ExtractAndDelete.Core;
public class ExtractionService
{
    public static ExtractAndDeleteResult Execute (
        string filePath,
        string destinationPath)
    {
        // 调用 ArchiveExtractor 尝试解压
        ExtractionResult extractionResult = 
            ArchiveExtractor.Extract(filePath, destinationPath);

        // 解压失败则结束
        if (!extractionResult.Success)
        {
            return new ExtractAndDeleteResult
            {
                Success = false,
                ErrorMessage = extractionResult.ErrorMessage
            };
        }

        // 调用 CleanupService 尝试回收
        CleanupResult cleanupResult =
            CleanupService.MoveToRecycleBin(filePath);

        // 回收失败则返回失败结果
        if (!cleanupResult.Success)
        {
            return new ExtractAndDeleteResult
            {
                Success = false,
                ErrorMessage = cleanupResult.ErrorMessage
            };
        }

        return new ExtractAndDeleteResult
        {
            Success = true,
            ErrorMessage = null
        };
    }
}
