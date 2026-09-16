# Deployment Guide

> The approved deployment contract is in [PRD.md](PRD.md), and the related
> delivery work is in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## 1. Supported deployment profiles

| Profile | Intended use | Docker required | JobSpy availability |
|---|---|---:|---|
| Native development | Local development and debugging | No | Optional local Python sidecar |
| Native published app | Everyday personal use on Windows/macOS/Linux | No | Optional local Python sidecar |
| Docker Compose | Reproducible isolated deployment or home-lab | Yes, by choice | Optional container sidecar |

The native profile is primary. A user must be able to run the DOU, SQLite,
deterministic scoring, optional Copilot, and Telegram pipeline without Docker or
Python. JobSpy/LinkedIn is an experimental opt-in integration and is not a
prerequisite for the core application.

## 2. Native development

Prerequisites:

- .NET 10 SDK.
- A Telegram bot token and a private chat initiated by the user.
- GitHub Copilot CLI authentication and entitlement only when the optional
  Copilot analyzer is enabled.
- Python 3.10+ only when the optional local JobSpy sidecar is enabled.

The intended development command is:

```powershell
dotnet run --project src\JobHunter.Worker -- run --Profile:FilePath C:\JobHunterData\profile.yaml
```

The currently implemented WP-01 through WP-05 operational commands are:

```powershell
dotnet run --project src\JobHunter.Worker -- migrate
dotnet run --project src\JobHunter.Worker -- backup --output C:\JobHunterBackups\job-hunter.db
dotnet run --project src\JobHunter.Worker -- integrity-check
dotnet run --project src\JobHunter.Worker -- integrity-check --input C:\JobHunterBackups\job-hunter.db
dotnet run --project src\JobHunter.Worker -- restore --input C:\JobHunterBackups\job-hunter.db --output C:\JobHunterRestore\job-hunter.db
```

Backup and restore output paths must be absolute. Restore is deliberately
non-destructive and refuses to overwrite an existing database. `doctor`,
`run-once`, and `setup-telegram` remain WP-09 work. Source scheduling remains
WP-06 work, so the current `run` command initializes the database and profile
without fetching vacancies.

## 3. Native published application

Publish a self-contained build for the user's architecture when a local .NET
runtime should not be required:

```powershell
dotnet publish src/JobHunter.Worker -c Release -r win-x64 --self-contained true
dotnet publish src/JobHunter.Worker -c Release -r osx-arm64 --self-contained true
dotnet publish src/JobHunter.Worker -c Release -r osx-x64 --self-contained true
```

Framework-dependent publishes are acceptable when the organization controls .NET
servicing. The release process must state which option it distributes and
support only the tested runtime identifiers.

The initial native user experience is manual start of the published executable.
Auto-start integrations are a later packaging enhancement:

- Windows: Windows Service or Task Scheduler, depending on per-user versus
  machine-wide ownership.
- macOS: per-user `launchd` LaunchAgent.
- Linux: systemd user or system service.

Service wrappers must use absolute executable/config/data paths, bounded restart
policy, a least-privileged identity, and a stop deadline longer than the
application shutdown timeout.

## 4. Native data, configuration, and secrets

The worker resolves an `IAppDataDirectory` once at startup. The directory must
be local, writable by the running identity, and independent of the executable
installation path. Suggested locations:

| Platform | Per-user application data |
|---|---|
| Windows | `%LOCALAPPDATA%\JobHunter` |
| macOS | `~/Library/Application Support/JobHunter` |
| Linux | `$XDG_DATA_HOME/job-hunter`, falling back to `~/.local/share/job-hunter` |

Store SQLite, WAL/SHM files, backups, privacy-safe logs, and durable state below
that directory. Do not place the database on SMB, NFS, network-sync folders, or
the source checkout.

Configuration precedence follows standard .NET configuration: safe
`appsettings.json` defaults, optional environment-specific values, environment
variables, and command line. Validate required values at startup.

Secrets must not be in `appsettings.json`, a committed `.env`, source control,
SQLite, logs, or crash dumps:

| Context | Allowed approach |
|---|---|
| Development | .NET Secret Manager / user secrets |
| Windows published app | A Windows-protected credential store or access-controlled secret file |
| macOS published app | Keychain-backed or access-controlled per-user secret provider |
| Linux published app | An access-controlled credential file or systemd credential facility |
| Docker Compose | Docker secrets mounted as files |

The concrete platform secret provider is an implementation decision, but the
`ISecretReader` application boundary must keep its details out of business logic.
`doctor` must report a missing/unreadable secret without printing its value.

## 5. Optional local JobSpy sidecar

The .NET worker communicates with JobSpy only through a configured loopback URL,
for example `http://127.0.0.1:8080`. It does not start, manage, or require the
sidecar for core behavior.

If enabled natively:

1. Follow the exact locked setup in
   [`services/jobspy-api/README.md`](../services/jobspy-api/README.md), keeping
   the virtual environment outside the worker's application-data directory.
2. Start the FastAPI sidecar under the same user's local process manager.
3. Verify `http://127.0.0.1:8080/health` and `/version`.
4. Bind only to loopback.
5. Configure its URL and explicitly acknowledge the LinkedIn experimental risk.
6. Once WP-09 adds `doctor`, use it to validate the narrow API contract without
   making an aggressive scrape.

The adapter is enabled only when both settings are supplied:

```powershell
dotnet run --project src\JobHunter.Worker -- run `
  --Profile:FilePath C:\JobHunterData\profile.yaml `
  --Sources:LinkedInJobSpy:Enabled=true `
  --Sources:LinkedInJobSpy:ExperimentalAcknowledged=true `
  --Sources:LinkedInJobSpy:Endpoint=http://127.0.0.1:8080/
```

This currently validates and registers the adapter; WP-06 will invoke it from
the scheduled pipeline.

If the sidecar is unavailable, only that source is degraded. The DOU source and
all downstream core behavior remain operational.

JobSpy must never receive the Telegram token, local database path, full
candidate profile/CV, Copilot credentials, or a mounted application-data
directory. On LinkedIn 403/429/challenge/sign-in response, it must stop the
source and report `Blocked`; it must not use bypass mechanisms.

## 6. Optional Docker Compose profile

Compose is useful for reproducible Python isolation or a home-lab/server
installation, but it is not a supported prerequisite for native use.

```text
JobSpy container (optional)
    -> loopback/internal HTTP
.NET Worker container (single SQLite writer)
    -> one named local volume for SQLite/WAL/SHM
```

The Compose worker must remain usable when the JobSpy container is stopped or not
included in the profile. Compose secrets are mounted only into the worker that
needs them. The JobSpy service receives no application data or credentials.

Use one worker replica per SQLite volume. A Docker volume is local host storage,
not a mechanism for multi-host or multi-writer SQLite.

## 7. Native first-run checklist

1. Install the chosen .NET runtime or download the published executable.
2. Create and start the Telegram bot chat; provision the token through the
   platform secret mechanism.
3. Add and validate `profile.yaml` or `profile.json`.
4. Run `doctor`; resolve data path, secret, Telegram, and optional AI findings.
5. Run `setup-telegram` to verify and persist the chosen private destination.
6. Run `run-once --source dou`; confirm expected source-run and job records.
7. Start `run` for continuous scanning.
8. Optionally enable Copilot after confirming authentication and no-tool
   constraints.
9. Optionally install/start JobSpy and explicitly enable its LinkedIn source.
10. Back up and test restore before relying on the history.

## 8. Verification matrix

| Check | Native Windows | Native macOS | Compose |
|---|---:|---:|---:|
| DOU scan without Docker | Required | Required | N/A |
| SQLite restart persistence | Required | Required | Required |
| Telegram setup/send | Required | Required | Required |
| AI disabled fallback | Required | Required | Required |
| Copilot optional adapter | Required when enabled | Required when enabled | Required only if profile supports it |
| JobSpy unavailable isolation | Required if configured | Required if configured | Required if configured |
| Local secret read without printing value | Required | Required | Required |
| Docker volume restart | N/A | N/A | Required |
