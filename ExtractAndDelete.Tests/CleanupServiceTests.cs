using ExtractAndDelete.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace ExtractAndDelete.Tests;
public class CleanupServiceTests
{
    // 测试文件不存在时回收失败
    [Fact]
    public void MoveToRecycleBin_FileDoesNotExist_ReturnsFailure()
    {
        // Arrange
        string filePath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.txt"
        );

        // Act
        CleanupResult result =
            CleanupService.MoveToRecycleBin(filePath);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("File doesn't exist.", result.ErrorMessage);
    }

    // 测试文件存在时解压成功
    [Fact]
    public void MoveToRecycleBin_ExistingFile_ReturnsSuccess()
    {
        // Arrange
        string root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString()
        );

        Directory.CreateDirectory(root);

        string filePath = Path.Combine(root, "test-delete-me.txt");

        File.WriteAllText(
            filePath,
            "This file is created only for CleanupService testing."
        );

        try
        {
            // Act
            CleanupResult result =
                CleanupService.MoveToRecycleBin(filePath);

            // Assert
            Assert.True(result.Success);
            Assert.Null(result.ErrorMessage);

            // 原位置应该已经不存在
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            // Cleanup test directory itself
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
