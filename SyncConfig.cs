using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderSync;

/// <summary>One source-to-destination mirroring job.</summary>
public sealed class SyncTarget
{
    public string Name { get; set; } = "";
    public string Destination { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>When a scheduled sync should send a notification email.</summary>
public enum EmailMode { Always, OnError, OnSuccess }

/// <summary>
/// SMTP email-notification settings. The password is stored DPAPI-encrypted
/// (per-Windows-user) — never in plaintext in the JSON file.
/// </summary>
public sealed class EmailSettings
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";   // full email address used to sign in
    public string From { get; set; } = "";        // defaults to Username if blank
    public string To { get; set; } = "";          // one or more, comma/semicolon separated

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EmailMode Mode { get; set; } = EmailMode.Always;

    public bool AttachLog { get; set; } = true;

    /// <summary>DPAPI-encrypted password as Base64. Use Set/GetPassword to access.</summary>
    public string ProtectedPassword { get; set; } = "";

    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(ProtectedPassword);

    public void SetPassword(string plain)
    {
        if (string.IsNullOrEmpty(plain)) { ProtectedPassword = ""; return; }
        byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        ProtectedPassword = Convert.ToBase64String(enc);
    }

    public string GetPassword()
    {
        if (string.IsNullOrEmpty(ProtectedPassword)) return "";
        try
        {
            byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return ""; }
    }

    public string EffectiveFrom => string.IsNullOrWhiteSpace(From) ? Username : From;

    public bool ShouldSend(bool success) => Mode switch
    {
        EmailMode.OnError => !success,
        EmailMode.OnSuccess => success,
        _ => true,
    };
}

/// <summary>
/// Persisted configuration, stored as JSON under %LOCALAPPDATA%\FolderSync\config.json.
/// Created with sensible defaults on first run.
/// </summary>
public sealed class SyncConfig
{
    public string Source { get; set; } = "";
    public List<SyncTarget> Targets { get; set; } = new();

    /// <summary>Mirror = destination becomes an exact copy (deletes extras). False = additive only.</summary>
    public bool Mirror { get; set; } = true;

    public EmailSettings Email { get; set; } = new();

    [JsonIgnore]
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderSync");

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    [JsonIgnore]
    public static string LogDir => Path.Combine(AppDataDir, "logs");

    /// <summary>A fresh, uniquely-named log file for one sync run (never reused).</summary>
    public static string NewSyncLogPath()
    {
        Directory.CreateDirectory(LogDir);
        return Path.Combine(LogDir, $"sync-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
    }

    /// <summary>Deletes sync log files older than the given number of days.</summary>
    public static void CleanupOldLogs(int days = 7)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-days);
            foreach (var f in Directory.EnumerateFiles(LogDir, "sync-*.log"))
            {
                try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
                catch { /* skip files we can't delete */ }
            }
        }
        catch { /* logs dir missing etc. — nothing to clean */ }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static SyncConfig Load()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(LogDir);

        if (File.Exists(ConfigPath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(ConfigPath));
                if (cfg is not null && !string.IsNullOrWhiteSpace(cfg.Source))
                {
                    cfg.Email ??= new EmailSettings();
                    return cfg;
                }
            }
            catch
            {
                // Fall through to defaults if the file is corrupt.
            }
        }

        var def = Default();
        def.Save();
        return def;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }

    private static SyncConfig Default()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new SyncConfig
        {
            Source = Path.Combine(user, "OneDrive", "Documents", "Bhoomil's Documents"),
            Mirror = true,
            Targets = new List<SyncTarget>
            {
                new() { Name = "iCloud Drive", Destination = Path.Combine(user, "iCloudDrive", "Bhoomil's Documents"), Enabled = true },
                new() { Name = "Google Drive", Destination = @"G:\My Drive\Bhoomil's Documents", Enabled = true },
            }
        };
    }
}
