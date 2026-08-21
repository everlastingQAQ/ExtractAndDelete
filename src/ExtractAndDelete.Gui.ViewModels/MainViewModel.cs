using ExtractAndDelete.Core;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExtractAndDelete.Gui.ViewModels;

public enum StatusTone
{
    Normal,
    Success,
    Warning,
    Error
}

/// <summary>
/// State for the one-page Windows-style extraction wizard. The GUI exposes
/// one exact target path; the Core publisher decides how Windows merges it.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private Func<ExtractionService> _serviceFactory;
    private string? _archivePath;
    private string? _targetPath;
    private string? _parentDirectory;
    private string? _legacyDestinationPath;
    private string _currentEntry = string.Empty;
    private string _statusMessage = "请选择压缩包。";
    private WorkflowStage _currentStage = WorkflowStage.Validating;
    private int _completedEntries;
    private int _totalEntries;
    private long _completedBytes;
    private long _totalBytes;
    private double _progressPercentage;
    private bool _isProgressIndeterminate;
    private bool _isRunning;
    private bool _completedSuccessfully;
    private bool _canCancel;
    private bool _showExtractedFiles = true;
    private bool _legacyFolderMode;
    private StatusTone _statusTone = StatusTone.Normal;
    private CancellationTokenSource? _cancellationTokenSource;

    public MainViewModel(Func<ExtractionService>? serviceFactory = null)
    {
        _serviceFactory = serviceFactory ?? ExtractionService.CreateDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? ArchivePath
    {
        get => _archivePath;
        private set => SetField(ref _archivePath, value);
    }

    public string? TargetPath
    {
        get => _targetPath;
        set => SetTargetPath(value);
    }

    public bool ShowExtractedFiles
    {
        get => _showExtractedFiles;
        set => SetField(ref _showExtractedFiles, value);
    }

    // Kept as source-compatible aliases for V2 consumers. New GUI code only
    // binds TargetPath and never exposes these two properties.
    public string? ParentDirectory => _parentDirectory;

    public string? DestinationPath => _legacyDestinationPath;

    public string CurrentEntry
    {
        get => _currentEntry;
        private set => SetField(ref _currentEntry, value);
    }

    public WorkflowStage CurrentStage
    {
        get => _currentStage;
        private set => SetField(ref _currentStage, value);
    }

    public int CompletedEntries
    {
        get => _completedEntries;
        private set => SetField(ref _completedEntries, value);
    }

    public int TotalEntries
    {
        get => _totalEntries;
        private set => SetField(ref _totalEntries, value);
    }

    public long CompletedBytes
    {
        get => _completedBytes;
        private set => SetField(ref _completedBytes, value);
    }

    public long TotalBytes
    {
        get => _totalBytes;
        private set => SetField(ref _totalBytes, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set => SetField(ref _progressPercentage, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetField(ref _isProgressIndeterminate, value);
    }

    public StatusTone StatusTone => _statusTone;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                Raise(nameof(CanExecute));
                Raise(nameof(CanSelectPaths));
                Raise(nameof(CanCancelButton));
            }
        }
    }

    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            if (SetField(ref _canCancel, value))
            {
                Raise(nameof(CanExecute));
                Raise(nameof(CanSelectPaths));
                Raise(nameof(CanCancelButton));
            }
        }
    }

    public bool CanSelectPaths => !IsRunning;

    public bool CanCancelButton => !IsRunning || CanCancel;

    public bool CanExecute
    {
        get
        {
            if (IsRunning || _completedSuccessfully
                || string.IsNullOrWhiteSpace(ArchivePath)
                || !SupportedArchiveFormats.TryResolve(ArchivePath, out _)
                || string.IsNullOrWhiteSpace(TargetPath)
                || !Path.IsPathFullyQualified(TargetPath)
                || File.Exists(TargetPath))
            {
                return false;
            }

            // The compatibility SetParentDirectory method retains the V2
            // validation rule. The new target text box intentionally allows an
            // existing directory so Windows can merge and show conflicts.
            return !_legacyFolderMode || !Directory.Exists(TargetPath);
        }
    }

    public ExtractAndDeleteResult? LastResult { get; private set; }

    public void SetServiceFactory(Func<ExtractionService> serviceFactory)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
    }

    public void SetArchiveFromActivation(string path) => SetArchiveCore(path, useDefaultTarget: true);

    public void SetArchiveFromPicker(string path) => SetArchiveCore(path, useDefaultTarget: true);

    // V2 compatibility entry point. The packaged GUI uses the two methods
    // above so that a newly selected archive immediately gets Windows' default
    // destination suggestion.
    public void SetArchive(string path) => SetArchiveCore(path, useDefaultTarget: false);

    public void SetTargetPath(string? path)
    {
        if (IsRunning)
        {
            SetStatus("当前已有解压任务正在进行。", StatusTone.Warning);
            return;
        }

        _legacyFolderMode = false;
        string? fullPath = TryGetAbsolutePath(path);
        _targetPath = fullPath;
        Raise(nameof(TargetPath));
        Raise(nameof(CanExecute));

        if (fullPath is null)
        {
            SetStatus(string.IsNullOrWhiteSpace(path) ? "请输入目标文件夹。" : "目标路径无效，请重新输入或浏览选择。",
                string.IsNullOrWhiteSpace(path) ? StatusTone.Normal : StatusTone.Error);
        }
        else if (File.Exists(fullPath))
        {
            SetStatus("目标路径是文件，不是文件夹。", StatusTone.Error);
        }
        else if (ArchivePath is null)
        {
            SetStatus("已设置目标文件夹，请选择压缩包。", StatusTone.Normal);
        }
        else
        {
            SetStatus("目标文件夹已设置，可以开始提取。", StatusTone.Normal);
        }
    }

    // V2 compatibility method. It now simply computes the one exact target
    // path and is not used by the new wizard.
    public void SetParentDirectory(string path)
    {
        if (IsRunning)
        {
            SetStatus("当前已有解压任务正在进行。", StatusTone.Warning);
            return;
        }

        _legacyFolderMode = true;
        _parentDirectory = TryGetAbsolutePath(path);
        _legacyDestinationPath = ComputeDefaultTarget(ArchivePath, _parentDirectory);
        _targetPath = _legacyDestinationPath;
        Raise(nameof(ParentDirectory));
        Raise(nameof(DestinationPath));
        Raise(nameof(TargetPath));
        Raise(nameof(CanExecute));

        if (_parentDirectory is null)
        {
            SetStatus(string.IsNullOrWhiteSpace(path) ? "请选择目标父目录。" : "目标父目录路径无效，请重新选择文件夹。",
                string.IsNullOrWhiteSpace(path) ? StatusTone.Normal : StatusTone.Error);
        }
        else if (!string.IsNullOrWhiteSpace(ArchivePath)
            && !SupportedArchiveFormats.TryResolve(ArchivePath, out _))
        {
            SetStatus("当前版本仅支持 ZIP、7Z、RAR 和 TAR 压缩包，请重新选择。", StatusTone.Error);
        }
        else if (!string.IsNullOrWhiteSpace(_legacyDestinationPath)
            && (Directory.Exists(_legacyDestinationPath) || File.Exists(_legacyDestinationPath)))
        {
            SetStatus("最终目录已存在，请重新选择目标父目录。", StatusTone.Error);
        }
        else
        {
            SetStatus("已选择目标父目录，可以开始安全解压。", StatusTone.Normal);
        }
    }

    public async Task<ExtractAndDeleteResult?> ExecuteAsync()
    {
        if (!CanExecute || ArchivePath is null || TargetPath is null)
        {
            return null;
        }

        IsRunning = true;
        _completedSuccessfully = false;
        LastResult = null;
        _cancellationTokenSource = new CancellationTokenSource();
        ProgressPercentage = 0;
        IsProgressIndeterminate = false;
        CurrentEntry = string.Empty;
        CurrentStage = WorkflowStage.Validating;
        CompletedEntries = 0;
        TotalEntries = 0;
        CompletedBytes = 0;
        TotalBytes = 0;
        SetStatus("正在验证操作……", StatusTone.Normal);

        Progress<ExtractionProgress> progress = new(UpdateProgress);
        ExtractAndDeleteResult result;
        try
        {
            ExtractionService service = _serviceFactory();
            result = await service.ExecuteAsync(
                new ExtractAndDeleteRequest(ArchivePath, TargetPath),
                progress,
                _cancellationTokenSource.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = new ExtractAndDeleteResult(
                WorkflowOutcome.ExtractionFailed,
                ErrorCode.Unexpected,
                "操作过程中发生未预期错误。",
                ArchivePath,
                TargetPath,
                DestinationState.Unchanged,
                SourceDisposition.Retained,
                ex.ToString());
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsRunning = false;
            CanCancel = false;
        }

        LastResult = result;
        ApplyResult(result);
        return result;
    }

    public void RequestCancellation()
    {
        if (CanCancel)
        {
            SetStatus("正在取消操作并清理临时目录，请稍候……", StatusTone.Warning);
            _cancellationTokenSource?.Cancel();
        }
        else if (IsRunning)
        {
            SetStatus("正在完成安全操作，请稍候。", StatusTone.Warning);
        }
    }

    public void SetStatus(string message, StatusTone tone)
    {
        StatusMessage = message;
        _statusTone = tone;
        Raise(nameof(StatusTone));
    }

    private void SetArchiveCore(string path, bool useDefaultTarget)
    {
        if (IsRunning)
        {
            SetStatus("当前已有解压任务正在进行。", StatusTone.Warning);
            return;
        }

        ArchivePath = TryGetAbsolutePath(path);
        _parentDirectory = null;
        _legacyDestinationPath = null;
        _legacyFolderMode = false;
        _targetPath = useDefaultTarget ? ComputeDefaultTarget(ArchivePath, Path.GetDirectoryName(ArchivePath ?? string.Empty)) : null;
        Raise(nameof(ParentDirectory));
        Raise(nameof(DestinationPath));
        Raise(nameof(TargetPath));
        _completedSuccessfully = false;
        LastResult = null;
        ShowExtractedFiles = true;
        ProgressPercentage = 0;
        IsProgressIndeterminate = false;
        CurrentEntry = string.Empty;

        if (ArchivePath is null)
        {
            SetStatus(string.IsNullOrWhiteSpace(path) ? "请选择压缩包。" : "压缩包路径无效，请重新选择。",
                string.IsNullOrWhiteSpace(path) ? StatusTone.Normal : StatusTone.Error);
        }
        else if (!SupportedArchiveFormats.TryResolve(ArchivePath, out _))
        {
            SetStatus("当前版本仅支持 ZIP、7Z、RAR 和 TAR 压缩包，请重新选择。", StatusTone.Error);
        }
        else if (useDefaultTarget)
        {
            SetStatus("请选择一个目标并提取文件。", StatusTone.Normal);
        }
        else
        {
            SetStatus("已选择压缩包，请选择目标文件夹。", StatusTone.Normal);
        }
        Raise(nameof(CanExecute));
    }

    private void UpdateProgress(ExtractionProgress progress)
    {
        if (!IsRunning)
        {
            return;
        }

        CanCancel = progress.CanCancel
            && progress.Stage is WorkflowStage.Scanning or WorkflowStage.Extracting or WorkflowStage.Publishing;
        CurrentEntry = progress.CurrentEntry ?? string.Empty;
        CurrentStage = progress.Stage;
        CompletedEntries = progress.CompletedEntries;
        TotalEntries = progress.TotalEntries;
        CompletedBytes = progress.CompletedBytes;
        TotalBytes = progress.TotalBytes;
        IsProgressIndeterminate = progress.IsIndeterminate;
        ProgressPercentage = Math.Clamp(progress.Percentage, 0, 100);
        SetStatus(progress.Stage switch
        {
            WorkflowStage.Validating => "正在验证……",
            WorkflowStage.Scanning => "正在扫描压缩包……",
            WorkflowStage.Extracting => "正在提取文件……",
            WorkflowStage.Publishing => "正在使用 Windows 文件操作发布文件……",
            WorkflowStage.Recycling => "正在移入回收站，请稍候……",
            WorkflowStage.Completed => "已完成。",
            _ => "正在处理……"
        }, progress.Stage is WorkflowStage.Publishing or WorkflowStage.Recycling ? StatusTone.Warning : StatusTone.Normal);
    }

    private void ApplyResult(ExtractAndDeleteResult result)
    {
        _completedSuccessfully = result.Success;
        IsProgressIndeterminate = false;
        CurrentStage = WorkflowStage.Completed;
        if (result.DestinationPublished)
        {
            ProgressPercentage = 100;
        }
        CurrentEntry = string.Empty;

        StatusTone tone = result.Outcome switch
        {
            WorkflowOutcome.Completed => StatusTone.Success,
            WorkflowOutcome.CompletedWithSkippedItems => StatusTone.Warning,
            WorkflowOutcome.CleanupFailed => StatusTone.Warning,
            WorkflowOutcome.Cancelled => StatusTone.Warning,
            _ => StatusTone.Error
        };
        SetStatus(result.UserMessage, tone);
        Raise(nameof(CanExecute));
    }

    private static string? ComputeDefaultTarget(string? archivePath, string? directory)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(directory)) return null;
        string archiveName = Path.GetFileNameWithoutExtension(archivePath);
        return string.IsNullOrWhiteSpace(archiveName) ? null : Path.Combine(directory, archiveName);
    }

    private static string? TryGetAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Path.IsPathFullyQualified(path)) return null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            return Path.IsPathFullyQualified(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
