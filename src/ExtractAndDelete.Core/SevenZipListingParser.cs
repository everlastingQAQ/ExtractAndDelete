using System.Globalization;

namespace ExtractAndDelete.Core;

public sealed record SevenZipArchiveEntry(
    string EntryPath,
    bool IsDirectory,
    long Size,
    bool IsEncrypted,
    string? SymbolicLink,
    string? HardLink,
    bool IsAlternateStream,
    bool IsAntiItem,
    bool IsReparsePoint,
    int? VolumeIndex,
    IReadOnlyDictionary<string, string> Properties);

public sealed record SevenZipArchiveListing(
    string ArchiveType,
    int Volumes,
    IReadOnlyList<SevenZipArchiveEntry> Entries);

public sealed class SevenZipListingParser
{
    private const int MaxLineLength = 64 * 1024;
    private const int MaxEntries = 1_000_000;
    private readonly Dictionary<string, string> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SevenZipArchiveEntry> _entries = new();
    private string? _archiveType;
    private int _volumes = 1;
    private string? _protocolError;

    public string? ProtocolError => _protocolError;

    public int EntryCount => _entries.Count;

    public void AppendLine(string line)
    {
        if (_protocolError is not null)
        {
            return;
        }

        if (line.Length > MaxLineLength)
        {
            _protocolError = "7-Zip listing line exceeded the protocol limit.";
            return;
        }

        if (line.Length == 0)
        {
            FinalizeRecord();
            return;
        }

        int separator = line.IndexOf(" = ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            // The 7-Zip listing command writes a banner, scan summary and
            // separator lines around the key/value records. They are not part
            // of the machine-readable records and are intentionally ignored.
            return;
        }

        string key = line[..separator];
        string value = line[(separator + 3)..];
        _current[key] = value;
    }

    public SevenZipArchiveListing Complete()
    {
        if (_protocolError is not null)
        {
            throw new SevenZipProtocolException(_protocolError);
        }

        FinalizeRecord();
        if (_protocolError is not null)
        {
            throw new SevenZipProtocolException(_protocolError);
        }

        if (string.IsNullOrWhiteSpace(_archiveType))
        {
            throw new SevenZipProtocolException("7-Zip listing did not contain an archive type.");
        }

        return new SevenZipArchiveListing(_archiveType, _volumes, _entries.ToArray());
    }

    private void FinalizeRecord()
    {
        if (_current.Count == 0 || _protocolError is not null)
        {
            _current.Clear();
            return;
        }

        if (_current.TryGetValue("Type", out string? type)
            && !string.IsNullOrWhiteSpace(type)
            && !_current.ContainsKey("Folder"))
        {
            _archiveType ??= type.Trim();
            if (_current.TryGetValue("Volumes", out string? volumeText)
                && int.TryParse(volumeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedVolumes)
                && parsedVolumes > 0)
            {
                _volumes = Math.Max(_volumes, parsedVolumes);
            }
        }

        bool isEntryRecord = _current.ContainsKey("Folder")
            || (_current.ContainsKey("Path") && !_current.ContainsKey("Type"));
        if (isEntryRecord)
        {
            if (!_current.TryGetValue("Path", out string? path)
                || string.IsNullOrEmpty(path))
            {
                _protocolError = "7-Zip entry did not contain a path.";
                _current.Clear();
                return;
            }

            bool isDirectory = IsPlus(_current, "Folder")
                || IsDirectoryAttribute(_current);
            long size = 0;
            if (_current.TryGetValue("Size", out string? sizeText))
            {
                if (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out size)
                    || size < 0)
                {
                    _protocolError = $"Invalid size for 7-Zip entry '{path}'.";
                    _current.Clear();
                    return;
                }
            }
            else if (!isDirectory)
            {
                _protocolError = $"7-Zip file entry '{path}' did not contain a size.";
                _current.Clear();
                return;
            }

            if (_entries.Count >= MaxEntries)
            {
                _protocolError = "The archive contains too many entries.";
                _current.Clear();
                return;
            }

            int? volumeIndex = null;
            if (_current.TryGetValue("Volume Index", out string? volumeIndexText)
                && int.TryParse(volumeIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
            {
                volumeIndex = parsedIndex;
                if (parsedIndex > 0)
                {
                    _volumes = Math.Max(_volumes, 2);
                }
            }

            _entries.Add(new SevenZipArchiveEntry(
                path,
                isDirectory,
                size,
                IsPlus(_current, "Encrypted"),
                GetNonEmpty(_current, "Symbolic Link"),
                GetNonEmpty(_current, "Hard Link"),
                IsPlus(_current, "Alternate Stream")
                    || IsPlus(_current, "Stream")
                    || ContainsAttribute(_current, "Alternate"),
                IsPlus(_current, "Anti"),
                IsReparse(_current),
                volumeIndex,
                new Dictionary<string, string>(_current, StringComparer.OrdinalIgnoreCase)));
        }

        _current.Clear();
    }

    private static string? GetNonEmpty(
        IReadOnlyDictionary<string, string> properties,
        string key) =>
        properties.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool IsPlus(
        IReadOnlyDictionary<string, string> properties,
        string key) =>
        properties.TryGetValue(key, out string? value)
        && string.Equals(value.Trim(), "+", StringComparison.Ordinal);

    private static bool ContainsAttribute(
        IReadOnlyDictionary<string, string> properties,
        string text) =>
        properties.TryGetValue("Attributes", out string? value)
        && value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectoryAttribute(
        IReadOnlyDictionary<string, string> properties)
    {
        if (!properties.TryGetValue("Attributes", out string? value))
        {
            return false;
        }

        string normalized = value.Trim();
        return normalized.Equals("D", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("D ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("D/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparse(IReadOnlyDictionary<string, string> properties)
    {
        if (GetNonEmpty(properties, "Symbolic Link") is not null
            || GetNonEmpty(properties, "Hard Link") is not null
            || IsPlus(properties, "Reparse Point"))
        {
            return true;
        }

        if (properties.TryGetValue("Mode", out string? mode)
            && mode.StartsWith("l", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsAttribute(properties, "Reparse")
            || ContainsAttribute(properties, "Symbolic");
    }
}

public sealed class SevenZipProtocolException : Exception
{
    public SevenZipProtocolException(string message)
        : base(message)
    {
    }
}
