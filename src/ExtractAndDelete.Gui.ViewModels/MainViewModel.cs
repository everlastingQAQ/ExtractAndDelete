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

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string? _archivePath;
    private string? _parentDirectory;
    private string? _destinationPath;
    private string _currentEntry = string.Empty;
    private string _statusMessage = "请选择 ZIP 和目标父目录。";
    private double _progressPercentage;
    private bool _isRunning;
    private bool _completedSuccessfully;
    private bool _canCancel;
    private StatusTone _statusTone = StatusTone.Normal;
    private CancellationTokenSource? _cancellationTokenSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? ArchivePath
    {
        get => _archivePath;
        private set => SetField(ref _archivePath, value);
    }

    public string? ParentDirectory
    {
        get => _parentDirectory;
        private set => SetField(ref _parentDirectory, value);
    }

    public string? DestinationPath
    {
        get => _destinationPath;
        private set => SetField(ref _destinationPath, value);
    }

    public string CurrentEntry
    {
        get => _currentEntry;
        private set => SetField(ref _currentEntry, value);
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
            }
        }
    }

    public bool CanSelectPaths => !IsRunning;

    public bool CanExecute =>
        !IsRunning
        && !_completedSuccessfully
        && !string.IsNullOrWhiteSpace(ArchivePath)
        && !string.IsNullOrWhiteSpace(ParentDirectory)
        && !string.IsNullOrWhiteSpace(DestinationPath)
        && !Directory.Exists(DestinationPath)
        && !File.Exists(DestinationPath);

    public void SetArchiveFromActivation(string path) => SetArchive(path);

    public void SetArchive(string path)
    {
        if (IsRunning)
        {
            SetStatus("当前已有解压任务正在进行。", StatusTone.Warning);
            return;
        }

        ArchivePath = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        ParentDirectory = null;
        DestinationPath = null;
        _completedSuccessfully = false;
        ProgressPercentage = 0;
        CurrentEntry = string.Empty;
        SetStatus(
            string.IsNullOrWhiteSpace(ArchivePath)
                ? "请选择 ZIP 和目标父目录。"
                : "已选择 ZIP，请重新选择目标父目录。",
            StatusTone.Normal);
        Raise(nameof(CanExecute));
    }

    public void SetParentDirectory(string path)
    {
        if (IsRunning)
        {
            SetStatus("当前已有解压任务正在进行。", StatusTone.Warning);
            return;
        }

        ParentDirectory = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        DestinationPath = ComputeDestinationPath(ArchivePath, ParentDirectory);
        _completedSuccessfully = false;

        if (!string.IsNullOrWhiteSpace(DestinationPath)
            && (Directory.Exists(DestinationPath) || File.Exists(DestinationPath)))
        {
            SetStatus("最终目录已存在，请重新选择目标父目录。", StatusTone.Error);
        }
        else
        {
            SetStatus("已选择目标父目录，可以开始安全解压。", StatusTone.Normal);
        }

        Raise(nameof(CanExecute));
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute || ArchivePath is null || DestinationPath is null)
        {
            return;
        }

        IsRunning = true;
        _completedSuccessfully = false;
        _cancellationTokenSource = new CancellationTokenSource();
        ProgressPercentage = 0;
        CurrentEntry = string.Empty;
        SetStatus("正在验证操作……", StatusTone.Normal);

        Progress<ExtractionProgress> progress = new(UpdateProgress);
        ExtractAndDeleteResult result;
        try
        {
            ExtractionService service = ExtractionService.CreateDefault();
            result = await service.ExecuteAsync(
                new ExtractAndDeleteRequest(ArchivePath, DestinationPath),
                progress,
                _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            result = new ExtractAndDeleteResult(
                WorkflowOutcome.ExtractionFailed,
                ErrorCode.Unexpected,
                "操作过程中发生未预期错误。",
                ArchivePath,
                DestinationPath,
                false,
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

        ApplyResult(result);
    }

    public void RequestCancellation()
    {
        if (CanCancel)
        {
            SetStatus("正在取消解压并清理临时目录，请稍候……", StatusTone.Warning);
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

    private void UpdateProgress(ExtractionProgress progress)
    {
        if (!IsRunning)
        {
            return;
        }

        CanCancel = progress.CanCancel
            && progress.Stage is WorkflowStage.Scanning or WorkflowStage.Extracting;
        CurrentEntry = progress.CurrentEntry ?? string.Empty;
        ProgressPercentage = Math.Clamp(progress.Percentage, 0, 100);
        SetStatus(progress.Stage switch
        {
            WorkflowStage.Validating => "正在验证……",
            WorkflowStage.Scanning => "正在扫描 ZIP……",
            WorkflowStage.Extracting => "正在解压……",
            WorkflowStage.Publishing => "正在发布完整目录，请稍候……",
            WorkflowStage.Recycling => "正在移入回收站，请稍候……",
            WorkflowStage.Completed => "已完成。",
            _ => "正在处理……"
        }, progress.Stage is WorkflowStage.Publishing or WorkflowStage.Recycling
            ? StatusTone.Warning
            : StatusTone.Normal);
    }

    private void ApplyResult(ExtractAndDeleteResult result)
    {
        _completedSuccessfully = result.Outcome == WorkflowOutcome.Completed;
        ProgressPercentage = result.DestinationPublished ? 100 : ProgressPercentage;
        CurrentEntry = string.Empty;

        StatusTone tone = result.Outcome switch
        {
            WorkflowOutcome.Completed => StatusTone.Success,
            WorkflowOutcome.CleanupFailed => StatusTone.Warning,
            WorkflowOutcome.Cancelled => StatusTone.Warning,
            _ => StatusTone.Error
        };
        SetStatus(result.UserMessage, tone);
        Raise(nameof(CanExecute));
    }

    private static string? ComputeDestinationPath(string? archivePath, string? parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(archivePath)
            || string.IsNullOrWhiteSpace(parentDirectory))
        {
            return null;
        }

        string archiveName = Path.GetFileNameWithoutExtension(archivePath);
        return Path.Combine(parentDirectory, archiveName);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
