# Deployment Guide

> The approved deployment contract is in [PRD.md](PRD.md), and the related
> delivery work is in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## 1. Supported deployment profiles

| Profile | Intended use | Docker required | JobSpy availability |
|---|---|---:|---|
| Native development | Local development and debugging | No | Optional local Python sidecar |
| Native published app | Everyday personal use on Windows/macOS/Linux | No | Optional local Python sidecar |
| Docker Compose | Deferred post-native-MVP Worker packaging; optional JobSpy image is available | Yes, by choice | Optional container sidecar |

The native profile is primary. A user must be able to run the DOU, SQLite, hard
filters, mandatory Copilot qualification, and Telegram pipeline without Docker
or Python. JobSpy/LinkedIn is an experimental opt-in integration and is not a
prerequisite for the core application.

## 2. Native development

Prerequisites:

- .NET 10 SDK.
- A valid YAML or JSON candidate profile.
- A Telegram bot token and a distinct private chat initiated by the user only
  when Telegram is enabled.
- GitHub Copilot CLI authentication and entitlement.
- Python 3.11 only when the optional local JobSpy sidecar is enabled.

The intended development command is:

```powershell
dotnet run --project src\JobHunter.Worker -- run --Profile:FilePath C:\JobHunterData\profile.yaml
```

The implemented operational commands are:

```powershell
dotnet run --project src\JobHunter.Worker -- doctor --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- show-profile --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- run-once --source dou --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- setup-telegram --Telegram:Enabled=true --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- migrate
dotnet run --project src\JobHunter.Worker -- backup --output C:\JobHunterBackups\job-hunter.db
dotnet run --project src\JobHunter.Worker -- integrity-check
dotnet run --project src\JobHunter.Worker -- integrity-check --input C:\JobHunterBackups\job-hunter.db
dotnet run --project src\JobHunter.Worker -- restore --input C:\JobHunterBackups\job-hunter.db --output C:\JobHunterRestore\job-hunter.db
```

Backup and restore output paths must be absolute. Restore is deliberately
non-destructive and refuses to overwrite an existing database. Backups are
manual: the operator must select an encrypted or access-controlled destination
and manage copy rotation. No backup destination is saved by the application.

`doctor` checks database integrity, profile parsing, configured sources, the
JobSpy health/version contract when enabled, the Telegram bot/private
destination when enabled, and mandatory Copilot authentication/model
availability. It also fails on a persisted blocked/disabled source or a missing or
disabled Telegram destination. Its Telegram connectivity check does not send a
message. `setup-telegram` sends one test message and re-enables the configured
persisted destination after successful validation.

`show-profile` is a local, read-only inspection command. It prints the canonical
YAML/JSON profile representation, including bounded AI preferences, plus the
redacted Markdown-CV evidence sent to the mandatory AI evaluator. Preferences
never bypass deterministic hard filters. The command never calls a job
source, Telegram, or the AI provider. Its output can contain private career
information, so do not share it broadly.

`run-once --source <dou|linkedin-jobspy>` bypasses the normal due time for the
selected enabled source, but never bypasses disabled or blocked state. It runs
source ingestion, hard filters, mandatory analysis, and durable notification-intent
creation; it does not start the continuous Telegram dispatcher and exits
nonzero for skipped, partial, blocked, or failed execution. `run` starts the
source scheduler, retention cleanup, and the dispatcher when Telegram is
enabled.

## 3. Native published application

Publish a self-contained build for the user's architecture when a local .NET
runtime should not be required:

```powershell
dotnet publish src\JobHunter.Worker -c Release -r win-x64 --self-contained true
dotnet publish src\JobHunter.Worker -c Release -r osx-arm64 --self-contained true
dotnet publish src\JobHunter.Worker -c Release -r osx-x64 --self-contained true
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

Store SQLite, WAL/SHM files, privacy-safe logs, and durable state below that
directory. Keep backups in a separate operator-selected protected location.
Do not place the live database on SMB, NFS, network-sync folders, or the source
checkout.

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

For local development, provision placeholders through Secret Manager without
placing real values in shell history, documentation, or source control:

```powershell
dotnet user-secrets set "Telegram:BotToken" "<bot-token>" --project src\JobHunter.Worker
dotnet user-secrets set "Telegram:ChatId" "<private-test-chat-id>" --project src\JobHunter.Worker
dotnet user-secrets set "AI:Copilot:GitHubToken" "<optional-token>" --project src\JobHunter.Worker
```

The Copilot token setting is optional. When it is absent, the adapter uses the
SDK's supported logged-in-user authentication path. Never pass a token on the
command line. User Secrets are for development only; a published application
must receive secrets from an OS-protected or access-controlled configuration
source available to its process.

### Mandatory Copilot analysis

Copilot authentication and a supported model are required before `run` or
`run-once` can start:

```powershell
dotnet run --project src\JobHunter.Worker -- doctor `
  --Profile:FilePath C:\JobHunterData\profile.yaml

dotnet run --project src\JobHunter.Worker -- run `
  --Profile:FilePath C:\JobHunterData\profile.yaml
```

The checked-in default requests `gpt-5.6-luna`. `run` and `run-once` validate
the authenticated model before a source scan or notification can start. A normal
catalogue that omits the named model fails with
`CopilotModelUnavailable`. The SDK can expose only `auto` while still accepting
a named model, so the worker then creates and immediately removes a restricted,
no-tool session to validate the configured model. A failed probe stops the
command with its explicit availability status; it never silently switches to
`auto`. Use `--AI:Copilot:Model=auto` only as an explicit operator choice.

The AI instructions live in the versioned embedded template
`src/JobHunter.AI.Copilot/PromptTemplates/JobAnalysis.json`, rather than in C#
or deployment configuration. Edit it through normal code review; the worker
validates the template at startup and keeps tool permissions and evidence
delimiting in code.
Defaults bound each analysis to 48,000 input
characters, 4,000 validated output characters, 60 seconds, concurrency `1`,
queue capacity `32`, one transient retry, and one corrective retry after a
locally invalid structured output. Confidence below `0.65` is stored as
insufficient and creates no notification intent. A validated `overallFit` score
at or above `AI:MinimumFitScore` (default `75`) qualifies the vacancy; a lower
score is an AI rejection.
Debug Telegram messages show actual token usage and AI credits only when the SDK
returns them; they do not infer a USD price.
Transient failure states become eligible for a new attempt after 60 minutes.
Input budgeting uses the actual Unicode-preserving JSON representation so
Cyrillic vacancy text is not rejected merely because of serializer escaping.

Changing only `AI:Copilot:Model` affects new analyses but does not invalidate an
already cached analysis identity. A job revision, profile revision, or rubric
version change creates a new analysis identity.

The stable SDK does not currently expose a supported per-session credit cap.
The adapter instead creates a fresh constrained session with no built-in,
filesystem, shell, browser, network, MCP, skill, or agent tools and exposes
only the terminal `submit_job_analysis` tool. A failed analysis does not delete
the persisted vacancy; transient statuses are eligible for a later retry.

### Telegram notifications

After storing the development secrets, validate a distinct private test chat:

```powershell
dotnet run --project src\JobHunter.Worker -- setup-telegram `
  --Telegram:Enabled=true `
  --Profile:FilePath C:\JobHunterData\profile.yaml
```

Then run `doctor` with `--Telegram:Enabled=true`, followed by the continuous
`run` command with the same setting. Telegram delivery uses a durable outbox,
HTML escaping, a 4,096-character limit, one message per second per destination,
bounded retries, `retry_after` handling, and timeout-as-unknown semantics. The
`retry_after` value is applied to every pending row for that destination, not
only the message that received HTTP 429. The destination cooldown is persisted,
survives worker restart, and also applies to rows enqueued before it expires.
The MVP sends at most one notification for each job/destination pair, including
after a job, profile, or rubric revision; those changes may still be re-scored.
The user-facing Telegram text and HTML layout are in the versioned embedded
template `src/JobHunter.Notifications.Telegram/Templates/TelegramNotification.json`.
Edit it through normal code review. The renderer, not the template, continues
to validate HTTPS URLs, redact sensitive text, HTML-escape dynamic values, and
enforce Telegram's 4,096-character limit.

### Retention

Continuous `run` performs cleanup at startup and then every 24 hours by default.
Completed source runs and their observations, delivery attempts, and
application events older than 30 days are deleted in bounded batches. Old
terminal outbox payloads are replaced with `{}`, but the rows and unique keys
remain as deduplication tombstones. Jobs, revisions, profile snapshots, and
evaluation history are not removed by this policy.

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
6. Run `doctor` to validate `/health` and `/version` without making an
   aggressive scrape.

The adapter is enabled only when both settings are supplied:

```powershell
dotnet run --project src\JobHunter.Worker -- run `
  --Profile:FilePath C:\JobHunterData\profile.yaml `
  --Sources:LinkedInJobSpy:Enabled=true `
  --Sources:LinkedInJobSpy:ExperimentalAcknowledged=true `
  --Sources:LinkedInJobSpy:Endpoint=http://127.0.0.1:8080/
```

The scheduled pipeline invokes the adapter when it is enabled and due.
`run-once --source linkedin-jobspy` can invoke it immediately only while the
persisted subscription is enabled and not blocked.

If the sidecar is unavailable, only that source is degraded. The DOU source and
all downstream core behavior remain operational.

After a blocked JobSpy result, review the reason and wait for its configured
backoff. Re-enabling remains an explicit operator action: run once with the
source configured disabled so that state is synchronized, then restore the
enabled setting only after the policy/access issue is resolved. `doctor`
reports the persisted blocked state rather than treating endpoint health alone
as readiness.

JobSpy must never receive the Telegram token, local database path, full
candidate profile/CV, Copilot credentials, or a mounted application-data
directory. On LinkedIn 403/429/challenge/sign-in response, it must stop the
source and report `Blocked`; it must not use bypass mechanisms.

## 6. Deferred Docker Compose profile

Worker Docker/Compose packaging and multi-architecture worker images are
deferred until after the native MVP. The existing JobSpy sidecar image may be
used independently over loopback, but no current Compose profile is a supported
Worker deployment. The target design remains:

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
2. Add and validate `profile.yaml` or `profile.json`.
3. Run `migrate`, then run `doctor` with a configured Copilot model.
4. Create and start a Telegram bot chat only if notifications are wanted;
   provision secrets and run `setup-telegram`.
5. Run `doctor` with every intended integration enabled and resolve its findings.
6. Run `run-once --source dou`; confirm expected source-run, job, hard-filter,
  AI-analysis, and outbox records.
7. Start `run` for continuous scanning and notification dispatch.
8. Confirm Copilot authentication and no-tool constraints.
9. Optionally install/start JobSpy and explicitly enable its LinkedIn source.
10. Back up to a protected location and test restore before relying on history.

## 8. Verification matrix

| Check | Native Windows | Native macOS | Compose |
|---|---:|---:|---:|
| DOU scan without Docker | Required | Required | N/A |
| SQLite restart persistence | Required | Required | Required |
| Telegram setup/send | Required | Required | Required |
| Copilot authenticated model validation | Required | Required | Required |
| Deferred notification after AI failure | Required | Required | Required |
| JobSpy unavailable isolation | Required if configured | Required if configured | Required if configured |
| Local secret read without printing value | Required | Required | Required |
| Docker volume restart | N/A | N/A | Deferred with Worker Compose profile |
