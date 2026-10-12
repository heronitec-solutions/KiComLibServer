using System.Diagnostics;
using System.Text;

namespace KiComLibBackend.Services
{
    public sealed class ProcessRunResult
    {
        public int ExitCode { get; init; }

        public string Stdout { get; init; } = "";

        public string Stderr { get; init; } = "";
    }

    /// <summary>
    /// Starts a child process with a timeout, kills it when the caller cancels or the timeout fires,
    /// and keeps only a bounded amount of stdout and stderr.
    /// </summary>
    public class ProcessRunner
    {
        public const int DefaultMaxOutputChars = 1024 * 1024;

        public async Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> args,
            IDictionary<string, string>? environment,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null,
            int maxOutputChars = DefaultMaxOutputChars,
            string? workingDirectory = null)
        {
            if (maxOutputChars < 1)
            {
                maxOutputChars = DefaultMaxOutputChars;
            }

            ProcessStartInfo start = new()
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                start.WorkingDirectory = workingDirectory;
            }

            foreach (string arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    start.Environment[pair.Key] = pair.Value;
                }
            }

            using Process process = new() { StartInfo = start };
            try
            {
                process.Start();
            }
            catch (Exception exception)
            {
                return new ProcessRunResult { ExitCode = -1, Stdout = "", Stderr = exception.Message };
            }

            Task<string> stdoutTask = ReadBoundedAsync(process.StandardOutput, maxOutputChars);
            Task<string> stderrTask = ReadBoundedAsync(process.StandardError, maxOutputChars);

            using CancellationTokenSource timeoutSource = new();
            if (timeout.HasValue)
            {
                timeoutSource.CancelAfter(timeout.Value);
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                TryKill(process);
                string stdout = await AwaitOutputAsync(stdoutTask);
                string stderr = await AwaitOutputAsync(stderrTask);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                string message = string.IsNullOrWhiteSpace(stderr) ? "The process timed out." : stderr;
                return new ProcessRunResult { ExitCode = -2, Stdout = stdout, Stderr = message };
            }

            return new ProcessRunResult
            {
                ExitCode = process.ExitCode,
                Stdout = await AwaitOutputAsync(stdoutTask),
                Stderr = await AwaitOutputAsync(stderrTask)
            };
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // The process may have exited between the check and the kill.
            }
        }

        private static async Task<string> AwaitOutputAsync(Task<string> read)
        {
            try
            {
                return await read;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxChars)
        {
            char[] buffer = new char[4096];
            StringBuilder builder = new();
            bool truncated = false;
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory());
                if (read == 0)
                {
                    break;
                }

                if (builder.Length < maxChars)
                {
                    int take = Math.Min(read, maxChars - builder.Length);
                    builder.Append(buffer, 0, take);
                    if (take < read)
                    {
                        truncated = true;
                    }
                }
                else
                {
                    truncated = true;
                }
            }

            if (truncated)
            {
                builder.Append("\n…(truncated)");
            }

            return builder.ToString();
        }
    }
}
