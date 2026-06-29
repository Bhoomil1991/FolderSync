# Folder Sync

A small .NET 8 WinForms app that mirrors your OneDrive documents folder into your
iCloud Drive and Google Drive local folders. Because each cloud app (OneDrive,
iCloud, Google Drive for Desktop) syncs its own local folder to the cloud, copying
between those local folders is all that's needed — no cloud APIs or logins.

## Editing source & destinations

Everything is editable right in the app's **Configuration** box — changes save
automatically to `config.json`:

- **Source:** click **Browse…** to pick the folder to sync from (or type the path).
- **Mirror:** tick/untick the checkbox to switch between exact-mirror and additive mode.
- **Destinations:** a table you can grow or shrink:
  - **Add Destination…** — pick any folder; it's added (name defaults to the folder name).
  - **Remove Selected** — select a row and remove it (only stops syncing; no files deleted).
  - **On** column — toggle a destination off without removing it.
  - Double-click **Name** or **Destination** cells to edit them inline.

## Default config (first run)

| | Path |
|---|---|
| **Source** | `C:\Users\Lenovo\OneDrive\Documents\Bhoomil's Documents` |
| **iCloud** | `C:\Users\Lenovo\iCloudDrive\Bhoomil's Documents` |
| **Google Drive** | `G:\My Drive\Bhoomil's Documents` |

**Mode: Mirror** — destinations become an exact copy of the source. Files deleted
from the source are also deleted from the destinations. (Change `"Mirror": false`
in the config for additive-only / never-delete behavior.)

## How to run

- **Manual (button):** double-click the **Folder Sync** shortcut on your Desktop,
  then click **Sync Now**.
- **Headless (no window):** `FolderSync.exe --sync` — used by the scheduled tasks.

## Scheduling (daily / monthly / at startup)

Open the app and use the **Automatic scheduling** box:
- Pick a time, click **Enable Daily** or **Enable Monthly** (and the monthly day).
- Click **Enable At Startup** to sync every time you log in.
- The status line shows which tasks are installed; use the **Remove** buttons to undo.

These create entries in **Windows Task Scheduler** (named `FolderSync - Daily`, etc.)
that run `FolderSync.exe --sync` as your user — no admin rights required.

## Email notifications (after scheduled syncs)

In the **Email notification** box you can have the app email you a summary (with the
day's log file attached) after each **scheduled** sync. Manual "Sync Now" runs don't
email — use **Send Test Email** to verify setup.

Setup for Gmail:
1. Turn on **2-Step Verification** on your Google account.
2. Create an **App Password**: Google Account → Security → 2-Step Verification →
   App passwords → generate one for "Mail". You get a 16-character code.
3. In the app: tick **Enable**, enter your **Gmail address**, paste the **App Password**,
   set **Send to**, leave host `smtp.gmail.com` / port `587`, choose **When = Always**.
4. Click **Send Test Email** to confirm.

Notes:
- The password is stored **DPAPI-encrypted** (tied to your Windows user) in `config.json`,
  never in plaintext.
- "Other / custom SMTP": just change the host/port (port 465 = SSL, 587 = STARTTLS).
- Corporate Office365/TELUS accounts often block SMTP sign-in — a personal Gmail is
  the most reliable sender for automation.

## Files & logs

- **Config:** `%LOCALAPPDATA%\FolderSync\config.json` (edit source/destinations here)
- **Logs:** `%LOCALAPPDATA%\FolderSync\logs\sync-YYYYMMDD.log`

## Notes

- The first sync may be slow if your OneDrive files are *online-only* (Files
  On-Demand) — robocopy downloads them on demand. Subsequent syncs are fast.
- The sync engine wraps Windows' built-in **robocopy** (`/MIR`), which handles long
  paths, retries, and multithreading reliably.

## Build from source

```
dotnet build  -c Release            # compile
dotnet publish -c Release -o publish # produce publish\FolderSync.exe
```
