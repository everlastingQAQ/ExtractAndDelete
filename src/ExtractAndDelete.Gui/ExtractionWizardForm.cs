using ExtractAndDelete.Core;
using ExtractAndDelete.Gui.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ExtractAndDelete.Gui;

internal sealed class ExtractionWizardForm : Form
{
    private const int BaselineClientWidth = 766;
    private const int BaselineClientHeight = 538;
    private const int FooterHeight = 49;

    private readonly MainViewModel _viewModel;
    private readonly TextBox _targetTextBox;
    private readonly Button _browseButton;
    private readonly CheckBox _showFilesCheckBox;
    private readonly Button _executeButton;
    private readonly Button _cancelButton;
    private readonly Panel _footerPanel;
    private readonly PictureBox _archiveIcon;
    private readonly Label _headingLabel;
    private readonly Label _pathLabel;
    private readonly System.Windows.Forms.Timer _progressCancelTimer;

    private IExtractionProgressUi? _progressUi;
    private Task? _workflowTask;
    private bool _syncingTarget;
    private bool _allowClose;
    private bool _closePromptShowing;
    private bool _exitRequested;

    public ExtractionWizardForm()
    {
        _viewModel = new MainViewModel();
        _viewModel.SetServiceFactory(() => ExtractionService.CreateGui(Handle));

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(BaselineClientWidth, BaselineClientHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = string.Empty;
        BackColor = Color.White;
        KeyPreview = true;
        Opacity = 0;

        _archiveIcon = new PictureBox
        {
            Location = new Point(47, 15),
            Size = new Size(20, 20),
            SizeMode = PictureBoxSizeMode.CenterImage,
            TabStop = false
        };

        Button backButton = new()
        {
            Location = new Point(8, 9),
            Size = new Size(28, 28),
            Text = "←",
            Enabled = false,
            TabStop = false,
            FlatStyle = FlatStyle.System,
            AccessibleName = "返回"
        };

        Label titleLabel = new()
        {
            AutoSize = true,
            Location = new Point(73, 12),
            Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
            Text = "提取压缩(Zipped)文件夹",
            UseCompatibleTextRendering = false,
            TabStop = false
        };

        _headingLabel = new Label
        {
            AutoSize = true,
            Location = new Point(48, 75),
            Font = new Font("Segoe UI", 15F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(0, 103, 192),
            Text = "选择一个目标并提取文件",
            UseCompatibleTextRendering = false,
            TabStop = false
        };

        _pathLabel = new Label
        {
            AutoSize = true,
            Location = new Point(48, 123),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Text = "文件将被提取到这个文件夹(&F):",
            UseMnemonic = true,
            UseCompatibleTextRendering = false,
            TabStop = false
        };

        _targetTextBox = new TextBox
        {
            Location = new Point(49, 149),
            Size = new Size(562, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            AccessibleName = "提取目标文件夹",
            TabIndex = 0
        };

        _browseButton = new Button
        {
            Location = new Point(623, 148),
            Size = new Size(112, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = "浏览(&R)...",
            UseMnemonic = true,
            FlatStyle = FlatStyle.System,
            AccessibleName = "浏览目标文件夹",
            TabIndex = 1
        };

        _showFilesCheckBox = new CheckBox
        {
            AutoSize = true,
            Location = new Point(49, 213),
            Text = "完成时显示提取的文件(&H)",
            UseMnemonic = true,
            Checked = true,
            FlatStyle = FlatStyle.System,
            AccessibleName = "完成时显示提取的文件",
            TabIndex = 2
        };

        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = FooterHeight,
            BackColor = Color.FromArgb(245, 245, 245),
            TabStop = false
        };

        _cancelButton = new Button
        {
            Location = Point.Empty,
            Size = new Size(89, 28),
            Text = "取消",
            FlatStyle = FlatStyle.System,
            AccessibleName = "取消",
            TabIndex = 4
        };

        _executeButton = new Button
        {
            Location = Point.Empty,
            Size = new Size(130, 28),
            Text = "提取并回收(&E)",
            UseMnemonic = true,
            FlatStyle = FlatStyle.System,
            AccessibleName = "提取并回收",
            TabIndex = 3
        };

        _footerPanel.Controls.Add(_executeButton);
        _footerPanel.Controls.Add(_cancelButton);
        _footerPanel.Resize += FooterPanel_Resize;
        LayoutFooterButtons();

        Controls.Add(_archiveIcon);
        Controls.Add(backButton);
        Controls.Add(titleLabel);
        Controls.Add(_headingLabel);
        Controls.Add(_pathLabel);
        Controls.Add(_targetTextBox);
        Controls.Add(_browseButton);
        Controls.Add(_showFilesCheckBox);
        Controls.Add(_footerPanel);

        AcceptButton = _executeButton;
        CancelButton = _cancelButton;

        _targetTextBox.TextChanged += TargetTextBox_TextChanged;
        _browseButton.Click += BrowseButton_Click;
        _showFilesCheckBox.CheckedChanged += ShowFilesCheckBox_CheckedChanged;
        _executeButton.Click += ExecuteButton_Click;
        _cancelButton.Click += CancelButton_Click;
        FormClosing += ExtractionWizardForm_FormClosing;
        DpiChanged += ExtractionWizardForm_DpiChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        _progressCancelTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _progressCancelTimer.Tick += ProgressCancelTimer_Tick;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        ApplyTheme();
        UpdateControls();
        LoadArchiveIcon();
    }

    public bool ExitRequested => _exitRequested;

    public void HandleActivation(string? archivePath)
    {
        if (IsDisposed)
        {
            return;
        }

        if (Visible)
        {
            BringToFront();
        }

        if (string.IsNullOrWhiteSpace(archivePath))
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.ArchivePath))
            {
                return;
            }

            string? selectedArchive = PickArchive();
            if (selectedArchive is null)
            {
                _exitRequested = true;
                if (Visible)
                {
                    _allowClose = true;
                    Close();
                }

                return;
            }

            _viewModel.SetArchiveFromPicker(selectedArchive);
            RevealWindow();
            return;
        }

        if (_viewModel.IsRunning)
        {
            ShowMessage("当前已有解压任务正在进行。", "Extract & Delete", MessageBoxIcon.Information);
            BringToFront();
            return;
        }

        _viewModel.SetArchiveFromActivation(archivePath);
        RevealWindow();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_exitRequested)
        {
            _allowClose = true;
            Close();
            return;
        }

        _targetTextBox.Focus();
        SelectTargetText();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _progressCancelTimer.Dispose();
            _progressUi?.Dispose();
            _archiveIcon.Image?.Dispose();
            _archiveIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void TargetTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (_syncingTarget)
        {
            return;
        }

        _viewModel.SetTargetPath(_targetTextBox.Text);
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "选择目标文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_targetTextBox.Text)
                ? _targetTextBox.Text
                : Path.GetDirectoryName(_targetTextBox.Text) ?? string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _viewModel.SetTargetPath(dialog.SelectedPath);
        }
    }

    private void ShowFilesCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        _viewModel.ShowExtractedFiles = _showFilesCheckBox.Checked;
    }

    private void FooterPanel_Resize(object? sender, EventArgs e)
    {
        LayoutFooterButtons();
    }

    private void LayoutFooterButtons()
    {
        if (_footerPanel is null || _executeButton is null || _cancelButton is null)
        {
            return;
        }

        int top = Math.Max(0, (_footerPanel.ClientSize.Height - _cancelButton.Height) / 2);
        int cancelLeft = Math.Max(0, _footerPanel.ClientSize.Width - 9 - _cancelButton.Width);
        _cancelButton.Location = new Point(cancelLeft, top);
        _executeButton.Location = new Point(
            Math.Max(0, cancelLeft - 11 - _executeButton.Width),
            top);
    }

    private async void ExecuteButton_Click(object? sender, EventArgs e)
    {
        if (_workflowTask is not null)
        {
            return;
        }

        if (!_viewModel.CanExecute)
        {
            ShowMessage("请先选择有效的压缩包和目标文件夹。", "无法提取", MessageBoxIcon.Warning);
            return;
        }

        _workflowTask = RunWorkflowAsync();
        await _workflowTask.ConfigureAwait(true);
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (_viewModel.IsRunning)
        {
            _viewModel.RequestCancellation();
            return;
        }

        _allowClose = true;
        Close();
    }

    private async Task RunWorkflowAsync()
    {
        _progressUi = new NativeProgressDialog();
        bool progressStarted = false;
        try
        {
            try
            {
                _progressUi.Start(Handle);
                progressStarted = true;
                _progressCancelTimer.Start();
                Hide();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Native progress dialog unavailable: {ex}");
                _progressUi.Dispose();
                _progressUi = null;
            }

            ExtractAndDeleteResult? result = await _viewModel.ExecuteAsync().ConfigureAwait(true);
            if (result is not null)
            {
                _progressCancelTimer.Stop();
                _progressUi?.Stop();
                await HandleResultAsync(result).ConfigureAwait(true);
            }
        }
        finally
        {
            if (progressStarted)
            {
                _progressCancelTimer.Stop();
            }

            _progressUi?.Stop();
            _progressUi?.Dispose();
            _progressUi = null;
            _workflowTask = null;
            UpdateControls();
        }
    }

    private async Task HandleResultAsync(ExtractAndDeleteResult result)
    {
        bool destinationOpened = true;
        if (result.DestinationPublished && _viewModel.ShowExtractedFiles)
        {
            destinationOpened = TryOpenDestination(result.DestinationPath);
        }

        if (result.Success && destinationOpened)
        {
            _allowClose = true;
            Close();
            return;
        }

        Show();
        BringToFront();

        string message = result.Success && !destinationOpened
            ? "已完成，但无法打开目标文件夹。"
            : result.UserMessage;
        MessageBoxIcon icon = result.Outcome == WorkflowOutcome.Completed
            ? MessageBoxIcon.Information
            : result.Outcome is WorkflowOutcome.CompletedWithSkippedItems or WorkflowOutcome.CleanupFailed or WorkflowOutcome.Cancelled
                ? MessageBoxIcon.Warning
                : MessageBoxIcon.Error;
        ShowResultDialog(result, message, icon);
        await Task.CompletedTask;
    }

    private void ProgressCancelTimer_Tick(object? sender, EventArgs e)
    {
        if (_progressUi?.IsCancellationRequested == true && _viewModel.CanCancel)
        {
            _viewModel.RequestCancellation();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TargetPath) or null)
        {
            if (!_syncingTarget && !string.Equals(_targetTextBox.Text, _viewModel.TargetPath, StringComparison.Ordinal))
            {
                _syncingTarget = true;
                try
                {
                    _targetTextBox.Text = _viewModel.TargetPath ?? string.Empty;
                    SelectTargetText();
                }
                finally
                {
                    _syncingTarget = false;
                }
            }
        }

        if (e.PropertyName is nameof(MainViewModel.CurrentEntry)
            or nameof(MainViewModel.ProgressPercentage)
            or nameof(MainViewModel.CurrentStage)
            or nameof(MainViewModel.IsRunning))
        {
            if (_viewModel.CurrentStage == WorkflowStage.Publishing)
            {
                _progressUi?.Stop();
            }
            else if (_progressUi is not null && _viewModel.IsRunning)
            {
                _progressUi.Report(
                    _viewModel.CurrentEntry,
                    _viewModel.CompletedBytes,
                    _viewModel.TotalBytes,
                    _viewModel.CompletedEntries,
                    _viewModel.TotalEntries);
            }
        }

        UpdateControls();
    }

    private void RevealWindow()
    {
        Opacity = 1;
        ShowInTaskbar = true;
        BringToFront();
        _targetTextBox.Focus();
        SelectTargetText();
    }

    private void UpdateControls()
    {
        bool canSelect = _viewModel.CanSelectPaths;
        _targetTextBox.Enabled = canSelect;
        _browseButton.Enabled = canSelect;
        _showFilesCheckBox.Enabled = canSelect;
        _executeButton.Enabled = _viewModel.CanExecute;
        _cancelButton.Enabled = !_viewModel.IsRunning || _viewModel.CanCancel;

        if (!_viewModel.IsRunning && Visible)
        {
            _cancelButton.Text = "取消";
        }
    }

    private void ExtractionWizardForm_DpiChanged(object? sender, DpiChangedEventArgs e)
    {
        LoadArchiveIcon();
        ApplyTheme();
    }

    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General)
        {
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        bool highContrast = SystemInformation.HighContrast;
        BackColor = highContrast ? SystemColors.Window : Color.White;
        _footerPanel.BackColor = highContrast ? SystemColors.Control : Color.FromArgb(245, 245, 245);
        _headingLabel.ForeColor = highContrast ? SystemColors.HotTrack : Color.FromArgb(0, 103, 192);
        _pathLabel.ForeColor = highContrast ? SystemColors.WindowText : Color.Black;
    }

    private void LoadArchiveIcon()
    {
        Icon? icon = NativeShellIcons.GetZipIcon(DeviceDpi);
        Image? previous = _archiveIcon.Image;
        _archiveIcon.Image = icon?.ToBitmap();
        icon?.Dispose();
        previous?.Dispose();
    }

    private string? PickArchive()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "选择压缩文件夹",
            Filter = "压缩文件 (*.zip;*.7z;*.rar;*.tar)|*.zip;*.7z;*.rar;*.tar",
            Multiselect = false,
            CheckFileExists = true,
            RestoreDirectory = false,
            AutoUpgradeEnabled = true
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private bool TryOpenDestination(string destinationPath)
    {
        try
        {
            if (!Directory.Exists(destinationPath))
            {
                return false;
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = "explorer.exe",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(destinationPath);
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unable to open destination: {ex}");
            return false;
        }
    }

    private void ExtractionWizardForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || !_viewModel.IsRunning)
        {
            return;
        }

        e.Cancel = true;
        if (!_viewModel.CanCancel)
        {
            ShowMessage("正在完成安全操作，请稍候。", "Extract & Delete", MessageBoxIcon.Information);
            return;
        }

        if (_closePromptShowing)
        {
            return;
        }

        _closePromptShowing = true;
        try
        {
            DialogResult result = MessageBox.Show(
                this,
                "提取正在进行，是否取消提取并退出？",
                "确认退出",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            _viewModel.RequestCancellation();
            _ = CloseAfterCancellationAsync();
        }
        finally
        {
            _closePromptShowing = false;
        }
    }

    private async Task CloseAfterCancellationAsync()
    {
        if (_workflowTask is not null)
        {
            await _workflowTask.ConfigureAwait(true);
        }

        if (!IsDisposed)
        {
            _allowClose = true;
            Close();
        }
    }

    private void ShowMessage(string message, string title, MessageBoxIcon icon)
    {
        if (IsDisposed)
        {
            return;
        }

        MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
    }

    private void ShowResultDialog(
        ExtractAndDeleteResult result,
        string message,
        MessageBoxIcon messageBoxIcon)
    {
        bool canOpenDestination = result.DestinationPublished
            || result.DestinationState is DestinationState.PartiallyModified or DestinationState.CompletedWithSkippedItems;
        canOpenDestination &= Directory.Exists(result.DestinationPath);

        try
        {
            TaskDialogPage page = new()
            {
                Caption = "Extract & Delete",
                Heading = result.Success
                    ? "解压并回收完成"
                    : result.Outcome switch
                    {
                        WorkflowOutcome.CompletedWithSkippedItems => "提取已完成，但有文件被跳过",
                        WorkflowOutcome.Cancelled => "提取已取消",
                        WorkflowOutcome.CleanupFailed => "文件已提取，但回收失败",
                        _ => "提取未完成"
                    },
                Text = message,
                Icon = messageBoxIcon switch
                {
                    MessageBoxIcon.Warning => TaskDialogIcon.Warning,
                    MessageBoxIcon.Information => TaskDialogIcon.Information,
                    _ => TaskDialogIcon.Error
                },
                AllowCancel = false,
                SizeToContent = true
            };

            TaskDialogButton? openButton = null;
            if (canOpenDestination)
            {
                openButton = new TaskDialogButton("打开目标文件夹(&O)", true, true);
                page.Buttons.Add(openButton);
            }

            TaskDialogButton closeButton = new("确定", true, true);
            page.Buttons.Add(closeButton);
            page.DefaultButton = closeButton;

            TaskDialogButton selected = TaskDialog.ShowDialog(
                this,
                page,
                TaskDialogStartupLocation.CenterOwner);
            if (openButton is not null && ReferenceEquals(selected, openButton))
            {
                _ = TryOpenDestination(result.DestinationPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Native result task dialog unavailable: {ex}");
            ShowMessage(message, "Extract & Delete", messageBoxIcon);
        }
    }

    private void SelectTargetText()
    {
        if (!_targetTextBox.IsHandleCreated)
        {
            _targetTextBox.SelectAll();
            return;
        }

        SendMessage(_targetTextBox.Handle, EditSetSelectionMessage, IntPtr.Zero, new IntPtr(-1));
    }

    private static class NativeShellIcons
    {
        private const uint ShellGetFileInfoIcon = 0x000000100;
        private const uint ShellGetFileInfoUseFileAttributes = 0x000000010;
        private const uint ShellGetFileInfoLargeIcon = 0x000000000;
        private const uint FileAttributeNormal = 0x00000080;

        public static Icon? GetZipIcon(int dpi)
        {
            SHFILEINFO info = new();
            uint flags = ShellGetFileInfoIcon | ShellGetFileInfoUseFileAttributes
                | (dpi >= 144 ? ShellGetFileInfoLargeIcon : 0x000000001);
            IntPtr result = SHGetFileInfo(
                "archive.zip",
                FileAttributeNormal,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return (Icon)Icon.FromHandle(info.hIcon).Clone();
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }
    }

    private const uint EditSetSelectionMessage = 0x00B1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
