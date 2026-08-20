using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace ExtractAndDelete.Core;

public sealed record SevenZipProcessResult(
    int ExitCode,
    bool WasCancelled,
    bool TerminationConfirmed,
    string StandardOutputTail,
    string StandardErrorTail);

public sealed class SevenZipProcessRunner
{
    private const int DiagnosticTailLimit = 64 * 1024;
    private static readonly TimeSpan TerminationWait = TimeSpan.FromSeconds(10);

    public async Task<SevenZipProcessResult> RunAsync(
        SevenZipToolPaths tools,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? standardOutputLine = null,
        Action<string>? standardErrorLine = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(arguments);

        if (cancellationToken.IsCancellationRequested)
        {
            return new SevenZipProcessResult(
                255,
                WasCancelled: true,
                TerminationConfirmed: true,
                string.Empty,
                string.Empty);
        }

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = tools.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(tools.ExecutablePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new SevenZipEngineException(
                    ErrorCode.ArchiveEngineUnavailable,
                    "无法启动内置 7-Zip 引擎。",
                    "Process.Start returned false.");
            }
        }
        catch (SevenZipEngineException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new SevenZipEngineException(
                ErrorCode.ArchiveEngineUnavailable,
                "无法启动内置 7-Zip 引擎。",
                ex.ToString());
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        var stdoutTail = new DiagnosticTail(DiagnosticTailLimit);
        var stderrTail = new DiagnosticTail(DiagnosticTailLimit);
        Task stdoutTask = DrainAsync(
            process.StandardOutput,
            stdoutTail,
            standardOutputLine);
        Task stderrTask = DrainAsync(
            process.StandardError,
            stderrTail,
            standardErrorLine);

        bool wasCancelled = false;
        bool terminationConfirmed = true;
        Task waitTask = process.WaitForExitAsync();
        Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        Task completedTask = await Task.WhenAny(waitTask, cancellationTask).ConfigureAwait(false);
        if (completedTask == cancellationTask && !waitTask.IsCompleted)
        {
            wasCancelled = true;
            terminationConfirmed = TryTerminate(process);
            if (terminationConfirmed)
            {
                terminationConfirmed = await WaitForExitAsync(process).ConfigureAwait(false);
            }
        }

        if (terminationConfirmed)
        {
            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                terminationConfirmed = false;
            }
        }

        if (terminationConfirmed)
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TerminationWait).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The process and its pipes are deliberately left alone. The caller
                // must not delete a staging directory while a writer may still exist.
            }
        }

        return new SevenZipProcessResult(
            process.HasExited ? process.ExitCode : -1,
            wasCancelled,
            terminationConfirmed && process.HasExited,
            stdoutTail.ToString(),
            stderrTail.ToString());
    }

    private static async Task DrainAsync(
        StreamReader reader,
        DiagnosticTail tail,
        Action<string>? lineHandler)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            if (line.Length > DiagnosticTailLimit)
            {
                throw new InvalidDataException("7-Zip output line exceeded the protocol limit.");
            }

            tail.AppendLine(line);
            lineHandler?.Invoke(line);
        }
    }

    private static bool TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return process.HasExited;
        }
        catch (Win32Exception)
        {
            return process.HasExited;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TerminationWait).ConfigureAwait(false);
            return process.HasExited;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return process.HasExited;
        }
    }

    private sealed class DiagnosticTail
    {
        private readonly int _limit;
        private readonly StringBuilder _builder = new();

        public DiagnosticTail(int limit) => _limit = limit;

        public void AppendLine(string line)
        {
            if (_builder.Length > 0)
            {
                _builder.AppendLine();
            }

            _builder.Append(line);
            if (_builder.Length > _limit)
            {
                _builder.Remove(0, _builder.Length - _limit);
            }
        }

        public override string ToString() => _builder.ToString();
    }
}
