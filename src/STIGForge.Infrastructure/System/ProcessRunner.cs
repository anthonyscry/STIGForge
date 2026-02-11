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

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // WaitForExitAsync logic for .NET 4.8 compatibility if needed, 
        // but here we are likely targeting .NET 8+. 
        // However, existing code used manual TaskCompletionSource, so I'll stick to that to be safe.
        
        var tcs = new TaskCompletionSource<bool>();
        process.Exited += (_, _) => tcs.TrySetResult(true);

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

        if (!process.WaitForExit(30000))
        {
            process.Kill();
            throw new TimeoutException("Process did not exit within 30 seconds.");
        }

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
