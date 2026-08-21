using ExtractAndDelete.Core;

if (args.Length != 2)
{
    Console.Error.WriteLine("用法：ExtractAndDelete.Cli.exe <压缩包路径> <准确目标目录>");
    return 1;
}

using CancellationTokenSource cancellationTokenSource = new();
int canCancel = 0;
ConsoleCancelEventHandler? cancelHandler = null;
cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    if (Volatile.Read(ref canCancel) == 0)
    {
        Console.Error.WriteLine("当前阶段不可取消，正在等待安全操作完成……");
        return;
    }

    if (!cancellationTokenSource.IsCancellationRequested)
    {
        Console.Error.WriteLine("正在取消解压，请稍候清理临时目录……");
        cancellationTokenSource.Cancel();
    }
    else
    {
        Console.Error.WriteLine("已收到取消请求，正在等待安全清理完成……");
    }
};
Console.CancelKeyPress += cancelHandler;

try
{
    WorkflowStage? lastStage = null;
    string? lastEntry = null;
    IProgress<ExtractionProgress> progress = new SynchronousProgress(value =>
    {
        bool isCancelableStage = value.Stage is WorkflowStage.Scanning or WorkflowStage.Extracting;
        Volatile.Write(
            ref canCancel,
            value.CanCancel && isCancelableStage ? 1 : 0);
        if (value.Stage != lastStage || !string.Equals(value.CurrentEntry, lastEntry, StringComparison.Ordinal))
        {
            lastStage = value.Stage;
            lastEntry = value.CurrentEntry;
            string entry = string.IsNullOrEmpty(value.CurrentEntry)
                ? string.Empty
                : $"：{value.CurrentEntry}";
            Console.WriteLine($"{GetStageMessage(value.Stage)}{entry}");
        }
    });

    ExtractionService service = ExtractionService.CreateDefault();
    ExtractAndDeleteResult result = await service.ExecuteAsync(
        new ExtractAndDeleteRequest(args[0], args[1]),
        progress,
        cancellationTokenSource.Token);

    if (result.Outcome == WorkflowOutcome.Completed)
    {
        Console.WriteLine(result.UserMessage);
        return 0;
    }

    Console.Error.WriteLine($"失败：{result.UserMessage}");
    if (!string.IsNullOrWhiteSpace(result.DiagnosticMessage))
    {
        Console.Error.WriteLine($"错误码：{result.ErrorCode}");
    }

    return result.Outcome switch
    {
        WorkflowOutcome.Cancelled => 3,
        WorkflowOutcome.CleanupFailed => 2,
        _ => 1
    };
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static string GetStageMessage(WorkflowStage stage) => stage switch
{
    WorkflowStage.Validating => "正在验证",
            WorkflowStage.Scanning => "正在扫描压缩包",
    WorkflowStage.Extracting => "正在解压",
    WorkflowStage.Publishing => "正在发布",
    WorkflowStage.Recycling => "正在移入回收站",
    WorkflowStage.Completed => "已完成",
    _ => "正在处理"
};

sealed class SynchronousProgress(Action<ExtractionProgress> handler) : IProgress<ExtractionProgress>
{
    public void Report(ExtractionProgress value) => handler(value);
}
