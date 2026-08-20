using System.IO.Compression;

namespace ExtractAndDelete.Core;

public class ArchiveExtractor
{
    public static ExtractionResult Extract(string filePath, string destinationPath)
    {
        // judge the file exists or not
        if (!File.Exists(filePath))
        {
            return new ExtractionResult
            {
                Success = false,
                ErrorMessage = "File doesn't exist."
            };
        }
        
        // judge the file's extension is legal or not
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

        // try to extract the file
        try
        {
            // create the destinated path
            Directory.CreateDirectory(destinationPath);

            ZipFile.ExtractToDirectory(filePath, destinationPath);
        }
        // check the file is extracted or not
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