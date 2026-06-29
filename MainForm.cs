using System.ComponentModel;
using System.Diagnostics;

namespace FolderSync;

public sealed class MainForm : Form
{
    private readonly SyncConfig _config;
    private readonly BindingList<SyncTarget> _targets;
    private bool _loaded;

    private readonly TextBox _sourceBox;
    private readonly CheckBox _mirrorCheck;
    private readonly DataGridView _destGrid;
    private readonly Button _syncButton;
    private readonly object _logLock = new();
    private readonly Label _statusLabel;
    private readonly CheckBox _schedEnable;
    private readonly DateTimePicker _timePicker;
    private readonly NumericUpDown _monthDay;

    private sealed record SchedRow(string TaskName, Button Enable, Button Disable, Button Remove);
    private readonly List<SchedRow> _scheduleRows = new();

    private readonly CheckBox _emailEnable;
    private readonly TextBox _emailFrom;
    private readonly TextBox _emailPass;
    private readonly TextBox _emailTo;
    private readonly TextBox _emailHost;
    private readonly NumericUpDown _emailPort;
    private readonly ComboBox _emailMode;
    private readonly Label _emailStatus;
    private readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 200, ReshowDelay = 100 };

    private const string AppPasswordHelpText =
        "Gmail needs a 16-character App Password (NOT your normal password).\r\n\r\n" +
        "1. Turn ON 2-Step Verification:\r\n     myaccount.google.com/signinoptions/twosv\r\n" +
        "2. Create an App Password (app = Mail):\r\n     myaccount.google.com/apppasswords\r\n" +
        "3. Paste the 16-character code here (spaces are fine).\r\n\r\n" +
        "Each Google account has its own App Password; you can revoke it anytime.\r\n" +
        "Click this icon to open the App Passwords page.";

    private CancellationTokenSource? _cts;

    public MainForm()
    {
        _config = SyncConfig.Load();
        // BindingList wraps the existing list, so add/remove/edit flow straight back into _config.Targets.
        _targets = new BindingList<SyncTarget>(_config.Targets);

        Text = "Folder Sync";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(760, 880);
        MinimumSize = new Size(720, 400);

        // Scrollable host so the whole window scrolls when content is taller than the window.
        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scrollHost);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // config
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // sync row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // schedule
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // email
        scrollHost.Controls.Add(root);

        // ===== Configuration (editable) =====
        var info = new GroupBox { Text = "Configuration", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 5 };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0: Source
        infoLayout.Controls.Add(new Label { Text = "Source:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 6, 0) }, 0, 0);
        _sourceBox = new TextBox { Text = _config.Source, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 6, 0) };
        _sourceBox.Leave += (_, _) => SaveConfig();
        infoLayout.Controls.Add(_sourceBox, 1, 0);
        var browseSrc = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
        browseSrc.Click += (_, _) => BrowseSource();
        infoLayout.Controls.Add(browseSrc, 2, 0);

        // Row 1: Mirror toggle
        _mirrorCheck = new CheckBox
        {
            Text = "Mirror — make destinations an exact copy (deletes files in destinations that are gone from source)",
            Checked = _config.Mirror,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };
        _mirrorCheck.CheckedChanged += (_, _) => SaveConfig();
        infoLayout.Controls.Add(_mirrorCheck, 1, 1);
        infoLayout.SetColumnSpan(_mirrorCheck, 2);

        // Row 2: Destinations label
        infoLayout.Controls.Add(new Label { Text = "Destinations:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) }, 0, 2);

        // Row 3: Destinations grid
        _destGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            Height = 130,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            Margin = new Padding(0, 0, 0, 4),
            DataSource = _targets,
        };
        _destGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(SyncTarget.Enabled), HeaderText = "On", Width = 40 });
        _destGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SyncTarget.Name), HeaderText = "Name", Width = 150 });
        _destGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SyncTarget.Destination), HeaderText = "Destination folder", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        // Commit checkbox edits immediately so they save without losing focus.
        _destGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_destGrid.IsCurrentCellDirty) _destGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _destGrid.CellValueChanged += (_, _) => SaveConfig();
        _targets.ListChanged += (_, _) => SaveConfig();
        infoLayout.Controls.Add(_destGrid, 1, 3);
        infoLayout.SetColumnSpan(_destGrid, 2);

        // Row 4: Add / Remove / open config
        var destBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        AddButton(destBtns, "Add Destination…", (_, _) => AddDestination());
        AddButton(destBtns, "Remove Selected", (_, _) => RemoveSelectedDestination());
        var editLink = new LinkLabel { Text = "Open config file", AutoSize = true, Margin = new Padding(14, 6, 0, 0) };
        editLink.Click += (_, _) => OpenInEditor(SyncConfig.ConfigPath);
        destBtns.Controls.Add(editLink);
        infoLayout.Controls.Add(destBtns, 1, 4);
        infoLayout.SetColumnSpan(destBtns, 2);

        info.Controls.Add(infoLayout);
        root.Controls.Add(info, 0, 0);

        // ===== Sync row =====
        var syncRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 0) };
        _syncButton = new Button { Text = "Sync Now", Width = 130, Height = 40, Font = new Font("Segoe UI", 11F, FontStyle.Bold) };
        _syncButton.Click += async (_, _) => await DoSyncAsync();
        _statusLabel = new Label { Text = "Ready.", AutoSize = true, Margin = new Padding(14, 12, 0, 0) };
        var openLogs = new LinkLabel { Text = "Open log folder", AutoSize = true, Margin = new Padding(14, 14, 0, 0) };
        openLogs.Click += (_, _) => OpenInEditor(SyncConfig.LogDir);
        syncRow.Controls.Add(_syncButton);
        syncRow.Controls.Add(_statusLabel);
        syncRow.Controls.Add(openLogs);
        root.Controls.Add(syncRow, 0, 1);

        // ===== Schedule group =====
        var sched = new GroupBox { Text = "Automatic scheduling (Windows Task Scheduler)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 14) };
        var schedOuter = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Top, AutoSize = true, WrapContents = false };

        bool anyInstalled = ScheduleManager.Exists(ScheduleManager.DailyTask)
                         || ScheduleManager.Exists(ScheduleManager.MonthlyTask)
                         || ScheduleManager.Exists(ScheduleManager.LogonTask);
        _schedEnable = new CheckBox { Text = "Enable automatic scheduling (show options)", Checked = anyInstalled, AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
        schedOuter.Controls.Add(_schedEnable);

        var schedDetails = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Visible = anyInstalled };

        var row1 = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        row1.Controls.Add(new Label { Text = "Run at:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _timePicker = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 130, Value = DateTime.Today.AddHours(18) };
        row1.Controls.Add(_timePicker);
        row1.Controls.Add(new Label { Text = "Monthly day:", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
        _monthDay = new NumericUpDown { Minimum = 1, Maximum = 28, Value = 1, Width = 50 };
        row1.Controls.Add(_monthDay);
        schedDetails.Controls.Add(row1);

        // One row per schedule: Enable (create/replace) · Disable (pause) · Remove (delete).
        // Buttons grey out to reflect the current state, so no separate status text is needed.
        schedDetails.Controls.Add(MakeScheduleRow("Daily",
            () => ScheduleManager.RegisterDaily(TimeOnly.FromDateTime(_timePicker.Value)),
            ScheduleManager.DailyTask));
        schedDetails.Controls.Add(MakeScheduleRow("Monthly",
            () => ScheduleManager.RegisterMonthly(TimeOnly.FromDateTime(_timePicker.Value), (int)_monthDay.Value),
            ScheduleManager.MonthlyTask));
        schedDetails.Controls.Add(MakeScheduleRow("At startup",
            ScheduleManager.RegisterLogon,
            ScheduleManager.LogonTask));

        schedOuter.Controls.Add(schedDetails);
        _schedEnable.CheckedChanged += (_, _) => schedDetails.Visible = _schedEnable.Checked;

        sched.Controls.Add(schedOuter);
        root.Controls.Add(sched, 0, 2);

        // ===== Email notifications =====
        var em = _config.Email;
        var emailGroup = new GroupBox { Text = "Email notification after a scheduled sync", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 16) };
        // Outer 1-column layout: checkbox on top, collapsible detail grid below.
        var emailOuter = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        emailOuter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        emailOuter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        emailOuter.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _emailEnable = new CheckBox { Text = "Enable email notifications (sent after each scheduled run)", Checked = em.Enabled, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        emailOuter.Controls.Add(_emailEnable, 0, 0);

        var emailDetails = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, Visible = em.Enabled };
        emailDetails.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        emailDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        emailDetails.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        emailDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        // From/login + Password
        emailDetails.Controls.Add(new Label { Text = "Gmail address:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 6, 0) }, 0, 0);
        _emailFrom = new TextBox { Text = em.From, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 0) };
        _emailFrom.Leave += (_, _) => SaveEmailConfig();
        emailDetails.Controls.Add(_emailFrom, 1, 0);

        // "App password:" label with a clickable ⓘ hint that explains how to generate one.
        var pwLabelPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };
        pwLabelPanel.Controls.Add(new Label { Text = "App password:", AutoSize = true, Margin = new Padding(0, 2, 2, 0) });
        var pwHint = new LinkLabel { Text = "ⓘ", AutoSize = true, Margin = new Padding(0, 2, 0, 0), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        pwHint.Click += (_, _) => ShowAppPasswordHelp();
        _tip.SetToolTip(pwHint, AppPasswordHelpText);
        pwLabelPanel.Controls.Add(pwHint);
        emailDetails.Controls.Add(pwLabelPanel, 2, 0);

        _emailPass = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(0, 5, 0, 0), PlaceholderText = em.HasPassword ? "•••••••• (saved — type to change)" : "16-char Google App Password" };
        _emailPass.Leave += (_, _) => SaveEmailConfig();
        _tip.SetToolTip(_emailPass, AppPasswordHelpText);
        emailDetails.Controls.Add(_emailPass, 3, 0);

        // To
        emailDetails.Controls.Add(new Label { Text = "Send to:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 6, 0) }, 0, 1);
        _emailTo = new TextBox { Text = em.To, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 0), PlaceholderText = "recipient@example.com (comma-separate for several)" };
        _emailTo.Leave += (_, _) => SaveEmailConfig();
        emailDetails.Controls.Add(_emailTo, 1, 1);
        emailDetails.SetColumnSpan(_emailTo, 3);

        // SMTP host + port
        emailDetails.Controls.Add(new Label { Text = "SMTP host:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 6, 0) }, 0, 2);
        _emailHost = new TextBox { Text = em.SmtpHost, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 0) };
        _emailHost.Leave += (_, _) => SaveEmailConfig();
        emailDetails.Controls.Add(_emailHost, 1, 2);
        emailDetails.Controls.Add(new Label { Text = "Port:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 6, 0) }, 2, 2);
        _emailPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = em.SmtpPort, Width = 80, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 0, 0) };
        _emailPort.ValueChanged += (_, _) => SaveEmailConfig();
        emailDetails.Controls.Add(_emailPort, 3, 2);

        // When + test button
        emailDetails.Controls.Add(new Label { Text = "When:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 6, 0) }, 0, 3);
        _emailMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 200, Margin = new Padding(0, 5, 0, 0) };
        _emailMode.Items.AddRange(new object[] { "Always", "Only on errors", "Only on success" });
        _emailMode.SelectedIndex = em.Mode switch { EmailMode.OnError => 1, EmailMode.OnSuccess => 2, _ => 0 };
        _emailMode.SelectedIndexChanged += (_, _) => SaveEmailConfig();
        emailDetails.Controls.Add(_emailMode, 1, 3);
        var testRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        AddButton(testRow, "Send Test Email", (_, _) => SendTestEmail());
        _emailStatus = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
        testRow.Controls.Add(_emailStatus);
        emailDetails.Controls.Add(testRow, 2, 3);
        emailDetails.SetColumnSpan(testRow, 2);

        emailOuter.Controls.Add(emailDetails, 0, 1);

        // Show the fields only when the checkbox is ticked.
        _emailEnable.CheckedChanged += (_, _) => { emailDetails.Visible = _emailEnable.Checked; SaveEmailConfig(); };

        emailGroup.Controls.Add(emailOuter);
        root.Controls.Add(emailGroup, 0, 3);

        _loaded = true;
        RefreshScheduleStates();

        // Nested AutoSize TableLayoutPanels don't always finish measuring on the first layout
        // pass, which clipped the email panel's last row until some later relayout occurred.
        // Force a full relayout once the window is shown so it renders correctly from the start.
        Shown += (_, _) =>
        {
            var s = ClientSize;
            ClientSize = new Size(s.Width, s.Height + 1);
            ClientSize = s;
            root.PerformLayout();
        };
    }

    // ===== Configuration editing =====

    private void BrowseSource()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the source folder to sync from", UseDescriptionForTitle = true };
        if (Directory.Exists(_sourceBox.Text)) dlg.SelectedPath = _sourceBox.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _sourceBox.Text = dlg.SelectedPath;
            SaveConfig();
        }
    }

    private void AddDestination()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select a destination folder to sync to", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string path = dlg.SelectedPath;
        if (_targets.Any(t => string.Equals(t.Destination, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That destination is already in the list.", "Folder Sync");
            return;
        }
        _targets.Add(new SyncTarget { Name = new DirectoryInfo(path).Name, Destination = path, Enabled = true });
        // ListChanged handler saves automatically.
    }

    private void RemoveSelectedDestination()
    {
        if (_destGrid.CurrentRow?.DataBoundItem is not SyncTarget target) return;
        if (MessageBox.Show(this, $"Remove destination:\n{target.Name} — {target.Destination}?\n\n(This only stops syncing to it; the folder's files are not deleted.)",
                "Folder Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _targets.Remove(target);
        }
    }

    private void SaveConfig()
    {
        if (!_loaded) return;
        _config.Source = _sourceBox.Text.Trim();
        _config.Mirror = _mirrorCheck.Checked;
        // _config.Targets is the same list backing _targets, so it is already current.
        try { _config.Save(); }
        catch (Exception ex) { AppendLog("[Config] save failed: " + ex.Message); }
    }

    // ===== Email settings =====

    private void SaveEmailConfig()
    {
        if (!_loaded) return;
        var em = _config.Email;
        em.Enabled = _emailEnable.Checked;
        em.From = _emailFrom.Text.Trim();
        em.Username = em.From;          // for Gmail the login is the same address
        em.To = _emailTo.Text.Trim();
        em.SmtpHost = _emailHost.Text.Trim();
        em.SmtpPort = (int)_emailPort.Value;
        em.Mode = _emailMode.SelectedIndex switch { 1 => EmailMode.OnError, 2 => EmailMode.OnSuccess, _ => EmailMode.Always };
        // Only overwrite the stored password if the user actually typed a new one.
        if (_emailPass.Text.Length > 0)
        {
            // Google shows App Passwords with spaces; strip them so the pasted code works.
            em.SetPassword(_emailPass.Text.Replace(" ", ""));
            _emailPass.Clear();
            _emailPass.PlaceholderText = "•••••••• (saved — type to change)";
        }
        try { _config.Save(); }
        catch (Exception ex) { AppendLog("[Email] config save failed: " + ex.Message); }
    }

    private void ShowAppPasswordHelp()
    {
        var answer = MessageBox.Show(this,
            AppPasswordHelpText + "\r\n\r\nOpen the Google App Passwords page now?",
            "How to get a Gmail App Password",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
            OpenInEditor("https://myaccount.google.com/apppasswords");
    }

    private void SendTestEmail()
    {
        SaveEmailConfig();
        var em = _config.Email;
        if (!em.HasPassword) { _emailStatus.Text = "Enter the app password first."; return; }

        _emailStatus.Text = "Sending…";
        try
        {
            Emailer.Send(em, "[Folder Sync] Test email",
                $"This is a test from Folder Sync on {Environment.MachineName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}.\n\nIf you received this, notifications are configured correctly.");
            _emailStatus.Text = "Test email sent ✓";
            AppendLog($"[Email] test sent to {em.To}.");
        }
        catch (Exception ex)
        {
            _emailStatus.Text = "Failed.";
            AppendLog("[Email] test FAILED: " + ex.Message);

            string msg = ex.Message;
            bool needsAppPassword = msg.Contains("Application-specific password", StringComparison.OrdinalIgnoreCase)
                                 || msg.Contains("InvalidSecondFactor", StringComparison.OrdinalIgnoreCase)
                                 || msg.Contains("5.7.9", StringComparison.OrdinalIgnoreCase)
                                 || msg.Contains("Username and Password not accepted", StringComparison.OrdinalIgnoreCase);
            if (needsAppPassword)
            {
                msg = "Google rejected the sign-in because this isn't an App Password.\r\n\r\n"
                    + "Gmail does not allow your normal account password for apps. You need a 16-character "
                    + "App Password:\r\n\r\n"
                    + "1. Turn ON 2-Step Verification:  myaccount.google.com/signinoptions/twosv\r\n"
                    + "2. Create an App Password:  myaccount.google.com/apppasswords\r\n"
                    + "   (choose app = Mail, then copy the 16-character code)\r\n"
                    + "3. Paste that code into the App password box here and click Send Test Email again.\r\n\r\n"
                    + "Tip: enter it with or without spaces — both work.\r\n\r\n"
                    + "Original error:\r\n" + ex.Message;
            }
            MessageBox.Show(this, msg, "Test email failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ===== Helpers =====

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0), Height = 28 };
        b.Click += onClick;
        return b;
    }

    private static void AddButton(Control parent, string text, EventHandler onClick)
        => parent.Controls.Add(MakeButton(text, onClick));

    /// <summary>Builds a labelled row with Enable / Disable / Remove buttons for one schedule.</summary>
    private FlowLayoutPanel MakeScheduleRow(string label, Func<(bool ok, string msg)> enableAction, string taskName)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Width = 80, Margin = new Padding(0, 6, 8, 0) });
        var enable = MakeButton("Enable", (_, _) => Apply(enableAction, $"{label}: enable"));
        var disable = MakeButton("Disable", (_, _) => Apply(() => ScheduleManager.SetEnabled(taskName, false), $"{label}: disable"));
        var remove = MakeButton("Remove", (_, _) => Apply(() => ScheduleManager.Remove(taskName), $"{label}: remove"));
        row.Controls.Add(enable);
        row.Controls.Add(disable);
        row.Controls.Add(remove);
        _scheduleRows.Add(new SchedRow(taskName, enable, disable, remove));
        return row;
    }

    /// <summary>Greys out buttons that don't apply to each task's current state.</summary>
    private void RefreshScheduleStates()
    {
        foreach (var r in _scheduleRows)
        {
            var st = ScheduleManager.GetState(r.TaskName);
            r.Enable.Enabled = st != ScheduleManager.TaskState.Enabled;        // greyed when already enabled
            r.Disable.Enabled = st == ScheduleManager.TaskState.Enabled;       // only an enabled task can be paused
            r.Remove.Enabled = st != ScheduleManager.TaskState.NotInstalled;   // nothing to remove if not installed
        }
    }

    private void Apply(Func<(bool ok, string msg)> action, string what)
    {
        try
        {
            var (ok, msg) = action();
            AppendLog($"[Schedule] {what}: {(ok ? "OK" : "FAILED")} {msg}");
        }
        catch (Exception ex)
        {
            AppendLog($"[Schedule] {what}: ERROR {ex.Message}");
        }
        RefreshScheduleStates();
    }

    private async Task DoSyncAsync()
    {
        if (_cts is not null) return; // already running

        // Make sure any in-progress grid edit is committed before syncing.
        _destGrid.EndEdit();
        SaveConfig();

        _cts = new CancellationTokenSource();
        _syncButton.Enabled = false;
        _statusLabel.Text = "Syncing…";

        var engine = new SyncEngine(_config);
        engine.Log += AppendLogThreadSafe;

        try
        {
            var result = await Task.Run(() => engine.RunAsync(_cts.Token));
            _statusLabel.Text = result.Success
                ? $"Done — last sync {DateTime.Now:HH:mm:ss}."
                : $"Completed with errors — {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            AppendLog("FATAL: " + ex.Message);
            _statusLabel.Text = "Failed.";
        }
        finally
        {
            _syncButton.Enabled = true;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void AppendLogThreadSafe(string line) => AppendLog(line);

    /// <summary>Writes a line to today's log file (the GUI no longer shows a live log panel).</summary>
    private void AppendLog(string line)
    {
        try
        {
            Directory.CreateDirectory(SyncConfig.LogDir);
            var path = Path.Combine(SyncConfig.LogDir, $"sync-{DateTime.Now:yyyyMMdd}.log");
            lock (_logLock) { File.AppendAllText(path, line + Environment.NewLine); }
        }
        catch { /* never let logging break the UI */ }
    }

    private static void OpenInEditor(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open:\n{path}\n\n{ex.Message}");
        }
    }
}
