using System.Diagnostics;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.System;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => tcs.TrySetResult(true);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        if (process.HasExited)
        {
            tcs.TrySetResult(true);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (ct.CanBeCanceled)
        {
            using (ct.Register(() => 
            {
                try { process.Kill(); }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Process kill failed: " + ex.Message);
                }
                tcs.TrySetCanceled(ct);
            }))
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }
        else
        {
            await tcs.Task.ConfigureAwait(false);
        }

        process.WaitForExit();

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString()
        };
    }

    public bool ExistsInPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVar))
            return false;

        var paths = pathVar.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, fileName);
            if (File.Exists(fullPath))
                return true;
        }

        return false;
    }
}
