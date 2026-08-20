using ExtractAndDelete.Core;
using System.IO.Compression;

namespace ExtractAndDelete.Tests;
public class ArchiveExtractorTests
{
    // 测试文件不存在时无法解压
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

    // 测试文件后缀不是.zip时无法解压
    [Fact]
    public void Extract_FileIsNotZIP_ReturnsFailure()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "not-a-zip.txt");
        try
        {
            // Arrange
            string destinationPath = Path.Combine(Path.GetTempPath(), "ExtractAndDeleteTest");
            File.WriteAllText(filePath, "hello");

            // Act
            ExtractionResult result = ArchiveExtractor.Extract(filePath, destinationPath);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("The file extension is illegal.", result.ErrorMessage);


        }
        finally
        {
            // Cleanup
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        
    }

    // 测试文件后缀名是.zip但是文件本身不是zip文件
    [Fact]
    public void Extract_FileWithZIPExtensionButNotZIPFile_ReturnsFailure()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "not-a-zip.zip");
        try
        {
            // Arrange
            string destinationPath = Path.Combine(Path.GetTempPath(), "ExtractAndDeleteTest");
            File.WriteAllText(filePath, "hello");

            // Act
            ExtractionResult result = ArchiveExtractor.Extract(filePath, destinationPath);

            // Assert
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            // Cleanup
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        
    }

    // 测试遇到合法的.zip时解压成功
    [Fact]
    public void Extract_ValidZIPFile_ReturnsSuccess()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string destinationPath = Path.Combine(root, "output");
        try
        {
            // Arrange
            string sourceDirectory = Path.Combine(root, "source");
            string zipPath = Path.Combine(root, "test.zip");
            
            Directory.CreateDirectory(sourceDirectory);

            string sourceFile = Path.Combine(sourceDirectory, "hello.txt");
            File.WriteAllText(sourceFile, "Hello");

            ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

            // Act
            ExtractionResult result = ArchiveExtractor.Extract(zipPath, destinationPath);

            // Assert
            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);

            string extractedFile = Path.Combine(destinationPath, "hello.txt");

            Assert.True(File.Exists(extractedFile));

            Assert.Equal("Hello", File.ReadAllText(extractedFile));

        }
        finally
        {
            // Cleanup
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(destinationPath))
            {
                Directory.Delete(destinationPath, recursive: true);
            }
        }
    }

    // 测试遇到大写的.ZIP时解压成功
    [Fact]
    public void Extract_ValidZIPFileWithUppercaseExtension_ReturnsSuccess()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );

        try
        {
            // Arrange
            string sourceDirectory = Path.Combine(root, "source");
            string zipPath = Path.Combine(root, "test.ZIP");
            string destinationPath = Path.Combine(root, "output");

            Directory.CreateDirectory(sourceDirectory);

            string sourceFile = Path.Combine(sourceDirectory, "hello.txt");
            File.WriteAllText(sourceFile, "Hello");

            ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

            // Act
            ExtractionResult result =
                ArchiveExtractor.Extract(zipPath, destinationPath);

            // Assert
            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);

            Assert.True(
                File.Exists(Path.Combine(destinationPath, "hello.txt"))
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // 测试目标目录遇到同名文件时失败
    [Fact]
    public void Extract_DestinationContainsSameFile_ReturnsFailure()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );

        try
        {
            // Arrange
            string sourceDirectory = Path.Combine(root, "source");
            string zipPath = Path.Combine(root, "test.zip");
            string destinationPath = Path.Combine(root, "output");

            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(destinationPath);

            File.WriteAllText(
                Path.Combine(sourceDirectory, "hello.txt"),
                "new"
            );

            File.WriteAllText(
                Path.Combine(destinationPath, "hello.txt"),
                "old"
            );

            ZipFile.CreateFromDirectory(sourceDirectory, zipPath);

            // Act
            ExtractionResult result =
                ArchiveExtractor.Extract(zipPath, destinationPath);

            // Assert
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);

            // 更重要：原文件不能被覆盖
            Assert.Equal(
                "old",
                File.ReadAllText(
                    Path.Combine(destinationPath, "hello.txt")
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
