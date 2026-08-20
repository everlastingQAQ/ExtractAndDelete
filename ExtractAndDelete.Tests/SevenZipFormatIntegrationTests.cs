using System.Diagnostics;
using System.Formats.Tar;
using ExtractAndDelete.Core;

namespace ExtractAndDelete.Tests;

public sealed class SevenZipFormatIntegrationTests
{
    [Fact]
    public async Task ExtractsSevenZipArchive()
    {
        using TemporaryDirectory temp = new();
        string source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "中文.txt"), "7z content");
        string archivePath = Path.Combine(temp.Path, "sample.7z");
        await CreateArchiveAsync(source, archivePath, "7z");

        ArchiveExtractionResult result = await ExtractAsync(archivePath, temp.Path);

        Assert.True(result.Success, result.DiagnosticMessage);
        Assert.Equal("7z content", File.ReadAllText(Path.Combine(temp.Path, ".staging", "中文.txt")));
    }

    [Fact]
    public async Task ExtractsTarArchive()
    {
        using TemporaryDirectory temp = new();
        string source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "hello.txt"), "tar content");
        string archivePath = Path.Combine(temp.Path, "sample.tar");
        TarFile.CreateFromDirectory(source, archivePath, includeBaseDirectory: false);

        ArchiveExtractionResult result = await ExtractAsync(archivePath, temp.Path);

        Assert.True(result.Success, result.DiagnosticMessage);
        Assert.Equal("tar content", File.ReadAllText(Path.Combine(temp.Path, ".staging", "hello.txt")));
    }

    [Theory]
    [InlineData(".ZIP", ArchiveFormat.Zip)]
    [InlineData(".7Z", ArchiveFormat.SevenZip)]
    [InlineData(".RaR", ArchiveFormat.Rar)]
    [InlineData(".tar", ArchiveFormat.Tar)]
    public void ResolvesSupportedExtensions(string extension, ArchiveFormat expected)
    {
        Assert.True(SupportedArchiveFormats.TryResolve("archive" + extension, out ArchiveFormatDescriptor descriptor));
        Assert.Equal(expected, descriptor.Format);
    }

    private static async Task<ArchiveExtractionResult> ExtractAsync(string archivePath, string root)
    {
        string stagingPath = Path.Combine(root, ".staging");
        return await new SevenZipArchiveExtractor(
                new SevenZipToolProvider(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "7-Zip")))
            .ExtractAsync(archivePath, stagingPath, progress: null, CancellationToken.None);
    }

    private static async Task CreateArchiveAsync(
        string sourceDirectory,
        string archivePath,
        string type)
    {
        string toolsRoot = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "7-Zip");
        SevenZipToolPaths tools = new SevenZipToolProvider(toolsRoot).ResolveAndVerify();
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = tools.ExecutablePath,
            WorkingDirectory = toolsRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        process.StartInfo.ArgumentList.Add("a");
        process.StartInfo.ArgumentList.Add($"-t{type}");
        process.StartInfo.ArgumentList.Add("-mx=0");
        process.StartInfo.ArgumentList.Add(archivePath);
        process.StartInfo.ArgumentList.Add(Path.Combine(sourceDirectory, "*"));

        Assert.True(process.Start());
        await process.WaitForExitAsync();
        string error = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, error);
    }
}
