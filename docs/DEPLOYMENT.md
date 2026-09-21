# Deployment

The supported deployment is a native, single-user Worker on Windows, macOS,
or Linux. Docker is not required.

## Defaults

- DOU, LinkedIn JobSpy, and Telegram are enabled.
- JobSpy runs as a separate local Python process at `http://127.0.0.1:8080`.
- The Worker is the only SQLite writer. Run one Worker for each data directory.
- Scheduled scans and Telegram delivery pause from 22:00 to 09:00 in
  `Europe/Kyiv` by default.

## Requirements

- .NET SDK 10.0.300 or later.
- Python 3.11+ for JobSpy.
- A GitHub account with an active Copilot subscription. Authenticate the
  local account with `gh auth login`, then sign in to Copilot with
  `copilot auth login`.
- A Telegram bot token and private chat ID. Store them with the supplied
  User Secrets script; never put them in source control.
- A profile file based on `deploy/examples/profile.yaml` and its `cv.md`.

Full first-run instructions, including Windows and macOS/Linux commands, are
in the [README](../README.md).

## Start

1. Start JobSpy and leave it running:

   ```bash
   cd services/jobspy-api
   python3.11 -m venv .venv
   ./.venv/bin/python -m pip install --require-hashes -r requirements-dev.lock
   ./.venv/bin/python -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --no-server-header
   ```

2. From the repository root, migrate, validate, and run:

   ```bash
   dotnet run --project src/JobHunter.Worker -- migrate --Storage:DataDirectory <data-dir>
   dotnet run --project src/JobHunter.Worker -- setup-telegram --Storage:DataDirectory <data-dir> --Profile:FilePath <profile-path>
   dotnet run --project src/JobHunter.Worker -- doctor --Storage:DataDirectory <data-dir> --Profile:FilePath <profile-path>
   dotnet run --project src/JobHunter.Worker -- run --Storage:DataDirectory <data-dir> --Profile:FilePath <profile-path>
   ```

`setup-telegram` sends a test message. `doctor` checks the profile, SQLite,
JobSpy, Telegram, and Copilot. `run` is a foreground process; stop it with
Ctrl+C.

## Data and secrets

| Platform | Suggested data directory |
|---|---|
| Windows | `%LOCALAPPDATA%\JobHunter` |
| macOS | `~/Library/Application Support/JobHunter` |
| Linux | `$XDG_DATA_HOME/job-hunter` or `~/.local/share/job-hunter` |

Use a local writable directory, not a network share, cloud-sync folder, or
source checkout. It contains SQLite, state, and Copilot working files. Keep
backups elsewhere in an access-controlled location.

Use [`.NET User Secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets)
only for local development. A published or service deployment must use an
OS-protected credential store available to its running user.

## Common operations

```bash
# One source scan; run starts the dispatcher that sends any queued notification.
dotnet run --project src/JobHunter.Worker -- run-once --source dou --Storage:DataDirectory <data-dir> --Profile:FilePath <profile-path>

# Database maintenance; backup and restore paths must be absolute.
dotnet run --project src/JobHunter.Worker -- backup --output <absolute-backup-path>
dotnet run --project src/JobHunter.Worker -- integrity-check
dotnet run --project src/JobHunter.Worker -- restore --input <absolute-backup-path> --output <new-absolute-database-path>
```

## Configuration

Command-line settings override `appsettings.json`. The main operational
overrides are:

- `--Sources:LinkedInJobSpy:Enabled=false` — run DOU only; JobSpy/Python are
  then unnecessary.
- `--Search:LookbackHours=48` — search a different posting-age window
  (default: 24 hours).
- `--Worker:QuietHours:Enabled=false` — disable scheduled quiet hours.
- `--AI:Copilot:Model=auto` — use an available Copilot model when the
  configured model is not entitled.

## Publish

For a self-contained local installation, choose the target runtime identifier:

```bash
dotnet publish src/JobHunter.Worker -c Release -r osx-arm64 --self-contained true
```

Replace `osx-arm64` with `win-x64`, `osx-x64`, or `linux-x64` as appropriate.
Run the published executable with the same `--Storage:DataDirectory` and
`--Profile:FilePath` settings. Keep JobSpy running under the same user.
