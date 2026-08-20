namespace ExtractAndDelete.Core;

public enum ArchiveFormat
{
    Zip,
    SevenZip,
    Rar,
    Tar
}

public sealed record ArchiveFormatDescriptor(
    ArchiveFormat Format,
    string Extension,
    string SevenZipType,
    string DisplayName);

public static class SupportedArchiveFormats
{
    private static readonly IReadOnlyList<ArchiveFormatDescriptor> AllFormats =
        new[]
        {
            new ArchiveFormatDescriptor(ArchiveFormat.Zip, ".zip", "zip", "ZIP"),
            new ArchiveFormatDescriptor(ArchiveFormat.SevenZip, ".7z", "7z", "7Z"),
            new ArchiveFormatDescriptor(ArchiveFormat.Rar, ".rar", "rar", "RAR"),
            new ArchiveFormatDescriptor(ArchiveFormat.Tar, ".tar", "tar", "TAR")
        };

    private static readonly IReadOnlyDictionary<string, ArchiveFormatDescriptor> ByExtension =
        AllFormats.ToDictionary(value => value.Extension, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<ArchiveFormat, ArchiveFormatDescriptor> ByFormat =
        AllFormats.ToDictionary(value => value.Format);

    public static IReadOnlyList<ArchiveFormatDescriptor> All => AllFormats;

    public static IReadOnlyList<string> Extensions =>
        AllFormats.Select(value => value.Extension).ToArray();

    public static bool TryResolve(
        string? archivePath,
        out ArchiveFormatDescriptor descriptor)
    {
        descriptor = null!;
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return false;
        }

        string extension;
        try
        {
            extension = Path.GetExtension(archivePath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return ByExtension.TryGetValue(extension, out descriptor!);
    }

    public static bool TryGet(
        ArchiveFormat format,
        out ArchiveFormatDescriptor descriptor) =>
        ByFormat.TryGetValue(format, out descriptor!);
}
