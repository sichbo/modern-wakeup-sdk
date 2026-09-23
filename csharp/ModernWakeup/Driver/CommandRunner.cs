using System.ComponentModel;
using System.Diagnostics;

namespace ModernWakeup.Driver;

internal sealed record CommandResult(int ExitCode, string Output)
{
    internal void EnsureSuccess(string operation, params int[] alsoAllowed)
    {
        if (ExitCode == 0 || alsoAllowed.Contains(ExitCode))
            return;
        throw new Win32Exception(ExitCode,
            $"{operation} failed with exit code {ExitCode}. {Output}".Trim());
    }
}

internal static class CommandRunner
{
    internal static CommandResult Run(string executable, params string[] arguments)
    {
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Windows could not start {executable}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        string output = string.Join(Environment.NewLine,
            new[] { stdout.Result.Trim(), stderr.Result.Trim() }.Where(value => value.Length != 0));
        return new CommandResult(process.ExitCode, output);
    }
}

