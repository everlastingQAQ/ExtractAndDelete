using ExtractAndDelete.Core;

namespace ExtractAndDelete.Tests;
public class ArchiveExtractorTests
{
    [Fact]
    public void Extract_FileDoesNotExist_ReturnsFailure()
    {
        // Arrange
        string filePath = @"C:\this-file-should-not-exist";
        string destinationPath = @"C:\temp\ExtractAndDeleteTest";

        // Act
        ExtractionResult result = ArchiveExtractor.Extract(filePath, destinationPath);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("File doesn't exist.", result.ErrorMessage);
    }

    [Fact]
    public void Extract_FileIsNotZIP_ReturnsFailure()
    {
        // Arrange
        string filePath = Path.Combine(Path.GetTempPath(), "not-a-zip.txt");
        string destinationPath = Path.Combine(Path.GetTempPath(), "ExtractAndDeleteTest");
        File.WriteAllText(filePath, "hello");

        // Act
        ExtractionResult result = ArchiveExtractor.Extract(filePath, destinationPath);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("The file extension is illeagl.", result.ErrorMessage);

        // Cleanup
        File.Delete(filePath);
    }
}
