using System.IO.Compression;

namespace ExtractAndDelete.Core;

public class ArchiveExtractor
{
    public static ExtractionResult Extract(string filePath, string destinationPath)
    {
        // 判断文件是否存在
        if (!File.Exists(filePath))
        {
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = "File doesn't exist."
            };
        }
        
        // 判断文件的拓展名是否合法
        string filePathLowerKey = filePath.ToLower();
        string extension = Path.GetExtension(filePathLowerKey);
        if (!extension.Equals(".zip"))
        {
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = "The file extension is illegal."
            };
        }

        // 尝试解压文件
        try
        {
            // 创建解压目录
            Directory.CreateDirectory(destinationPath);

            ZipFile.ExtractToDirectory(filePath, destinationPath);
        }
        // 查看文件是否解压成功
        catch (Exception ex)
        {
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        return new ExtractionResult
        {
            Success = true,
            ErrorMessage = null
        };
    }
}