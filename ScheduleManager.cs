using System.Diagnostics;
using System.Text;

namespace FolderSync;

/// <summary>
/// Manages the automatic-sync triggers. Daily/Monthly use Windows Task Scheduler
/// (schtasks.exe). "At logon" uses a shortcut in the user's Startup folder instead,
/// because a logon-triggered scheduled task requires admin rights. Everything here
/// runs the app headless ("FolderSync.exe --sync") as the current user, no elevation.
/// </summary>
public static class ScheduleManager
{
    public const string DailyTask = "FolderSync - Daily";
    public const string MonthlyTask = "FolderSync - Monthly";
    public const string LogonTask = "FolderSync - At Logon";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FolderSync.exe");

    // "At logon" is implemented as a Startup-folder shortcut (no admin needed).
    private static string StartupLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "FolderSync.lnk");
    private static string StartupLnkDisabled => StartupLnk + ".disabled";

    private static void CreateShortcut(string lnkPath, string target, string args)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(t)!;
        var sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = target;
        sc.Arguments = args;
        sc.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
        sc.Description = "Folder Sync — run at logon";
        sc.Save();
    }

    private static (int code, string output) Run(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, outp.Trim());
    }

    public static (bool ok, string msg) RegisterDaily(TimeOnly time)
    {
        var (code, output) = Run(
            $"/Create /F /TN \"{DailyTask}\" /TR \"\\\"{ExePath}\\\" --sync\" /SC DAILY /ST {time:HH:mm}");
        return (code == 0, output);
    }

    public static (bool ok, string msg) RegisterMonthly(TimeOnly time, int day = 1)
    {
        var (code, output) = Run(
            $"/Create /F /TN \"{MonthlyTask}\" /TR \"\\\"{ExePath}\\\" --sync\" /SC MONTHLY /D {day} /ST {time:HH:mm}");
        return (code == 0, output);
    }

    public static (bool ok, string msg) RegisterLogon()
    {
        try
        {
            if (File.Exists(StartupLnkDisabled)) File.Delete(StartupLnkDisabled);
            CreateShortcut(StartupLnk, ExePath, "--sync");
            return (true, "startup shortcut created");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static (bool ok, string msg) Remove(string taskName)
    {
        if (taskName == LogonTask)
        {
            try
            {
                if (File.Exists(StartupLnk)) File.Delete(StartupLnk);
                if (File.Exists(StartupLnkDisabled)) File.Delete(StartupLnkDisabled);
                return (true, "startup shortcut removed");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
        var (code, output) = Run($"/Delete /F /TN \"{taskName}\"");
        return (code == 0, output);
    }

    /// <summary>Pauses (disable) or resumes (enable) an already-registered trigger without deleting it.</summary>
    public static (bool ok, string msg) SetEnabled(string taskName, bool enabled)
    {
        if (taskName == LogonTask)
        {
            try
            {
                if (enabled) return RegisterLogon();
                if (File.Exists(StartupLnk))
                {
                    if (File.Exists(StartupLnkDisabled)) File.Delete(StartupLnkDisabled);
                    File.Move(StartupLnk, StartupLnkDisabled);   // keep it, but stop it running
                }
                return (true, "startup shortcut disabled");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        if (GetState(taskName) == TaskState.NotInstalled)
            return (false, $"Task \"{taskName}\" is not installed yet — enable it first.");
        var (code, output) = Run($"/Change /TN \"{taskName}\" {(enabled ? "/ENABLE" : "/DISABLE")}");
        return (code == 0, output);
    }

    public enum TaskState { NotInstalled, Enabled, Disabled }

    public static TaskState GetState(string taskName)
    {
        if (taskName == LogonTask)
        {
            if (File.Exists(StartupLnk)) return TaskState.Enabled;
            if (File.Exists(StartupLnkDisabled)) return TaskState.Disabled;
            return TaskState.NotInstalled;
        }

        var (code, output) = Run($"/Query /TN \"{taskName}\" /FO LIST /V");
        if (code != 0) return TaskState.NotInstalled;
        // Only the "Scheduled Task State:" line tells enabled vs disabled — other fields
        // (e.g. "Idle Time: Disabled") also contain the word, so don't scan the whole output.
        foreach (var line in output.Split('\n'))
        {
            if (line.TrimStart().StartsWith("Scheduled Task State:", StringComparison.OrdinalIgnoreCase))
                return line.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ? TaskState.Disabled : TaskState.Enabled;
        }
        return TaskState.Enabled; // installed but state line not found
    }

    public static bool Exists(string taskName) => GetState(taskName) != TaskState.NotInstalled;

    public static string Status()
    {
        static string Text(string t) => GetState(t) switch
        {
            TaskState.Enabled => "enabled",
            TaskState.Disabled => "disabled",
            _ => "not installed",
        };
        var sb = new StringBuilder();
        sb.AppendLine($"Daily   : {Text(DailyTask)}");
        sb.AppendLine($"Monthly : {Text(MonthlyTask)}");
        sb.AppendLine($"At logon: {Text(LogonTask)}");
        return sb.ToString().TrimEnd();
    }
}
