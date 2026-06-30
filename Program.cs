using System.Text;

namespace FolderSync;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool headless = args.Any(a =>
            a.Equals("--sync", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/sync", StringComparison.OrdinalIgnoreCase));

        if (headless)
            return RunHeadless();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>Silent run used by Task Scheduler. Writes a fresh log file per run and returns 0/1.</summary>
    private static int RunHeadless()
    {
        var config = SyncConfig.Load();
        var logPath = SyncConfig.NewSyncLogPath();   // unique file per sync, never appended to an old one
        var sb = new StringBuilder();

        var engine = new SyncEngine(config);
        engine.Log += line => sb.AppendLine(line);

        SyncResult result;
        bool fatal = false;
        try
        {
            result = engine.RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            sb.AppendLine("FATAL: " + ex);
            result = new SyncResult { Success = false };
            fatal = true;
        }

        try { File.WriteAllText(logPath, sb.ToString()); } catch { }
        SyncConfig.CleanupOldLogs(7);                 // prune logs older than 7 days
        TrySendEmail(config, result, logPath);

        return (result.Success && !fatal) ? 0 : 1;
    }

    /// <summary>Sends the notification email if enabled and the mode condition is met. Never throws.</summary>
    private static void TrySendEmail(SyncConfig config, SyncResult result, string logPath)
    {
        var email = config.Email;
        if (email is null || !email.Enabled) return;
        if (!email.ShouldSend(result.Success)) return;

        try
        {
            var (subject, body) = Emailer.BuildSummary(result, config, logPath);
            Emailer.Send(email, subject, body, logPath);
            TryAppend(logPath, $"[Email] notification sent to {email.To}.");
        }
        catch (Exception ex)
        {
            TryAppend(logPath, $"[Email] FAILED to send: {ex.Message}");
        }
    }

    private static void TryAppend(string path, string line)
    {
        try { File.AppendAllText(path, line + Environment.NewLine); } catch { }
    }
}
