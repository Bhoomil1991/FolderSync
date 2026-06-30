using System.Diagnostics;
using System.Text;

namespace FolderSync;

public sealed class TargetResult
{
    public string Name { get; set; } = "";
    public string Destination { get; set; } = "";
    public int ExitCode { get; set; }
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
}

public sealed class SyncResult
{
    public bool Success { get; set; }
    public List<TargetResult> Targets { get; set; } = new();
}

/// <summary>
/// Performs the actual folder mirroring by wrapping Windows' built-in robocopy,
/// which handles long paths, retries, multithreading and exact mirroring far more
/// robustly than hand-rolled copy logic.
/// </summary>
public sealed class SyncEngine
{
    private readonly SyncConfig _config;

    /// <summary>Raised for each line of progress/log output (UI + file logging).</summary>
    public event Action<string>? Log;

    public SyncEngine(SyncConfig config) => _config = config;

    private void Emit(string line) => Log?.Invoke(line);

    public async Task<SyncResult> RunAsync(CancellationToken ct = default)
    {
        var result = new SyncResult { Success = true };

        Emit($"===== Sync started {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        Emit($"Source : {_config.Source}");
        Emit($"Mode   : {(_config.Mirror ? "MIRROR (exact copy, deletes extras)" : "ADDITIVE (copy/update only)")}");

        if (!Directory.Exists(_config.Source))
        {
            Emit($"ERROR: Source folder does not exist: {_config.Source}");
            result.Success = false;
            return result;
        }

        foreach (var target in _config.Targets)
        {
            ct.ThrowIfCancellationRequested();

            if (!target.Enabled)
            {
                Emit($"--- Skipping (disabled): {target.Name}");
                continue;
            }

            Emit("");
            Emit($"--- {target.Name}  ->  {target.Destination}");

            // Safety: never let a (mirror) sync target the source itself or a folder nested
            // with it — that could delete data or loop endlessly.
            if (PathUtil.SameOrNested(_config.Source, target.Destination))
            {
                Emit("ERROR: destination is the source folder or nested inside/around it — skipped.");
                result.Targets.Add(new TargetResult { Name = target.Name, Destination = target.Destination, Success = false, Summary = "unsafe path (same as / nested with source)" });
                result.Success = false;
                continue;
            }

            try
            {
                Directory.CreateDirectory(target.Destination);
            }
            catch (Exception ex)
            {
                Emit($"ERROR: cannot create destination: {ex.Message}");
                result.Targets.Add(new TargetResult { Name = target.Name, Destination = target.Destination, Success = false, Summary = ex.Message });
                result.Success = false;
                continue;
            }

            var tr = await RunRobocopyAsync(_config.Source, target.Destination, ct);
            tr.Name = target.Name;
            result.Targets.Add(tr);
            if (!tr.Success) result.Success = false;

            Emit($"--- {target.Name}: {(tr.Success ? "OK" : "FAILED")} (robocopy exit {tr.ExitCode}) {tr.Summary}");
        }

        Emit("");
        Emit($"===== Sync {(result.Success ? "completed" : "completed WITH ERRORS")} {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        return result;
    }

    private async Task<TargetResult> RunRobocopyAsync(string src, string dst, CancellationToken ct)
    {
        // /MIR  = mirror (copy + purge extras) ; for additive we use /E (copy subdirs incl. empty)
        // /R:2 /W:2 = 2 retries, 2s wait      ; /MT:16 = 16 threads
        // /FFT  = relaxed (2s) timestamp compare, friendlier to cloud-sync drives
        // /XJ   = skip junctions (avoids loops) ; /NP = no per-file percent (cleaner log)
        // /NDL  = no dir listing                 ; /TEE not used (we capture the stream)
        string copyMode = _config.Mirror ? "/MIR" : "/E";
        string args = $"\"{src}\" \"{dst}\" {copyMode} /R:2 /W:2 /MT:16 /FFT /XJ /NP /NDL";

        var tr = new TargetResult { Destination = dst };
        var summary = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Read both streams to completion (more reliable than the event-based API, which can
        // drop the final summary lines), then await exit. Each stream is read on its own task.
        var stdoutTask = ReadStreamAsync(proc.StandardOutput, line =>
        {
            Emit("    " + line);
            if (line.Contains("Files :") || line.Contains("Dirs :") || line.Contains("Bytes :"))
                summary.Append(line.Trim()).Append("  ");
        });
        var stderrTask = ReadStreamAsync(proc.StandardError, line => Emit("    ERR: " + line));

        // If cancelled, stop robocopy rather than leaving it running in the background.
        using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { /* already gone */ } }))
        {
            try { await proc.WaitForExitAsync(ct); }
            finally { await Task.WhenAll(stdoutTask, stderrTask); }
        }

        tr.ExitCode = proc.ExitCode;
        // robocopy: 0-7 = success (8+ = at least one failure). Bit 3 (8) and above are errors.
        tr.Success = proc.ExitCode < 8;
        tr.Summary = summary.ToString().Trim();
        return tr;
    }

    /// <summary>Reads a redirected stream line-by-line, passing each non-blank line to <paramref name="onLine"/>.</summary>
    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onLine)
    {
        string? raw;
        while ((raw = await reader.ReadLineAsync()) is not null)
        {
            var line = raw.TrimEnd();
            if (line.Length > 0) onLine(line);
        }
    }
}
