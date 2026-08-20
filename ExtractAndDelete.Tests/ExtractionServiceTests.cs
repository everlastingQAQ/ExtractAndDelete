using ExtractAndDelete.Core;
using System.IO.Compression;

namespace ExtractAndDelete.Tests;

public class ExtractionServiceTests
{
    // 测试解压失败会保留文件
    [Fact]
    public void Execute_InvalidZIPFile_ReturnsFailureAndKeepsSourceFile()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );

        try
        {
            // Arrange
            Directory.CreateDirectory(root);

            string zipPath = Path.Combine(root, "invalid.zip");
            string destinationPath = Path.Combine(root, "output");

            // 文件后缀是 .zip，但内容不是合法 ZIP
            File.WriteAllText(zipPath, "This is not a ZIP file.");

            // Act
            ExtractAndDeleteResult result =
                ExtractionService.Execute(zipPath, destinationPath);

            // Assert
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);

            // 最关键：
            // 解压失败后，原 ZIP 必须仍然存在
            Assert.True(File.Exists(zipPath));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // 测试合法的解压和回收
    [Fact]
    public void Execute_ValidZIPFile_ReturnsSuccessAndRemovesSourceZIP()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );

        try
        {
            // Arrange
            string sourceDirectory = Path.Combine(root, "source");
            string destinationPath = Path.Combine(root, "output");

            string zipPath = Path.Combine(
                root,
                $"ExtractAndDeleteTest-{Guid.NewGuid():N}.zip"
            );

            Directory.CreateDirectory(sourceDirectory);

            string sourceFile =
                Path.Combine(sourceDirectory, "hello.txt");

            File.WriteAllText(sourceFile, "Hello");

            // 创建真正合法的 ZIP
            ZipFile.CreateFromDirectory(
                sourceDirectory,
                zipPath
            );

            // Act
            ExtractAndDeleteResult result =
                ExtractionService.Execute(
                    zipPath,
                    destinationPath
                );

            // Assert
            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);

            // 检查文件确实解压出来了
            string extractedFile =
                Path.Combine(destinationPath, "hello.txt");

            Assert.True(File.Exists(extractedFile));

            // 检查内容正确
            Assert.Equal(
                "Hello",
                File.ReadAllText(extractedFile)
            );

            // 原 ZIP 应该已经离开原位置
            Assert.False(File.Exists(zipPath));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}