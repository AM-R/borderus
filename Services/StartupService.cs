using System.Diagnostics;
using System.IO;

namespace Borderus.Services;

internal static class StartupService
{
    private const string TaskName = "Borderus";

    internal static bool IsEnabled() => Run("/Query", "/TN", TaskName) == 0;

    internal static bool SetEnabled(bool enabled)
    {
        if (!enabled) return !IsEnabled() || Run("/Delete", "/TN", TaskName, "/F") == 0;
        string? executable = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(executable) && Run(
            "/Create", "/TN", TaskName, "/TR", $"\"{executable}\" --minimized",
            "/SC", "ONLOGON", "/RL", "HIGHEST", "/F") == 0;
    }

    internal static bool Synchronize(bool enabled) => enabled ? SetEnabled(true) : SetEnabled(false);

    private static int Run(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            using Process? process = Process.Start(startInfo);
            if (process is null) return -1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
