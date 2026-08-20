using ExtractAndDelete.Core;

// 检查命令行长度
if (args.Length != 2)
{
    Console.WriteLine(
        "Usage: ExtractAndDelete.Cli <zipPath> <destinationPath>"
    );

    return 1;
}

// 获取文件路径和解压路径
string filePath = args[0];
string destinationPath = args[1];

// 尝试解压并删除
ExtractAndDeleteResult result =
    ExtractionService.Execute(filePath, destinationPath);

// 解压失败则报错
if (!result.Success)
{
    Console.Error.WriteLine(
        $"Failed: {result.ErrorMessage}"
    );
    return 1;
}

// 解压成功
Console.WriteLine(
    "Extract and delete completed successfully."
);

return 0;
