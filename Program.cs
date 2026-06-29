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

    /// <summary>Silent run used by Task Scheduler. Writes to a dated log file and returns 0/1.</summary>
    private static int RunHeadless()
    {
        var config = SyncConfig.Load();
        var logPath = Path.Combine(SyncConfig.LogDir, $"sync-{DateTime.Now:yyyyMMdd}.log");
        var sb = new StringBuilder();

        var engine = new SyncEngine(config);
        engine.Log += line => sb.AppendLine(line);

        SyncResult result;
        try
        {
            result = engine.RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            sb.AppendLine("FATAL: " + ex);
            File.AppendAllText(logPath, sb.ToString());
            TrySendEmail(config, new SyncResult { Success = false }, logPath, sb);
            File.AppendAllText(logPath, sb.ToString());
            return 1;
        }

        TrySendEmail(config, result, logPath, sb);
        File.AppendAllText(logPath, sb.ToString());
        return result.Success ? 0 : 1;
    }

    /// <summary>Sends the notification email if enabled and the mode condition is met. Never throws.</summary>
    private static void TrySendEmail(SyncConfig config, SyncResult result, string logPath, StringBuilder log)
    {
        var email = config.Email;
        if (email is null || !email.Enabled) return;
        if (!email.ShouldSend(result.Success)) return;

        try
        {
            var (subject, body) = Emailer.BuildSummary(result, config, logPath);
            // Flush current log to disk first so the attachment includes this run.
            File.AppendAllText(logPath, log.ToString());
            log.Clear();
            Emailer.Send(email, subject, body, logPath);
            log.AppendLine($"[Email] notification sent to {email.To}.");
        }
        catch (Exception ex)
        {
            log.AppendLine($"[Email] FAILED to send: {ex.Message}");
        }
    }
}
