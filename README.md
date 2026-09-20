# Job Hunter

Local, cross-platform .NET service that discovers .NET vacancies, ranks them
against a personal profile with Copilot, and sends relevant results to Telegram.

The implementation contract is:

- [Product requirements document](docs/PRD.md)
- [Implementation plan](docs/IMPLEMENTATION_PLAN.md)
- [Deployment guide](docs/DEPLOYMENT.md)
- [Copilot instructions](.github/copilot-instructions.md)

## Target architecture

```text
DOU RSS ─────────┐
                 ├─> .NET Worker -> SQLite -> hard filters -> Copilot -> Telegram
JobSpy API opt-in ┘
```

The native .NET Worker is the primary runtime: it runs directly on Windows,
macOS, and Linux without Docker. Docker Compose is an optional deployment
profile for a reproducible isolated JobSpy sidecar or a home-lab/server setup.

The .NET Worker is the only SQLite writer. JobSpy is an isolated Python
sidecar used only for the experimental, opt-in LinkedIn connector.

## Status

Implementation is in progress. WP-01 through the core WP-08 path now provide:

- the .NET 10 Worker foundation, project boundaries, configuration validation,
  single-instance guard, SQLite migration protocol, and native data paths;
- the full versioned domain/persistence model, idempotent ingestion, source-run
  leases, and backup/restore/integrity-check services;
- compact YAML/JSON candidate profiles, bounded and redacted supplemental CV
  loading, explicit hard filters, bounded AI preferences, and fixed `overallFit`
  semantic evaluation;
- a bounded DOU RSS adapter with safe XML/HTML handling, conditional requests,
  retries, canonical URLs, fixture tests, and optional detail enrichment;
- an isolated, opt-in JobSpy/FastAPI service plus a loopback-only .NET adapter
  with explicit blocked/degraded states and no access-control evasion;
- persisted source scheduling, bounded orchestration, mandatory Copilot fit
  scoring after explicit hard filters,
  continuously renewed run leases, durable notification intent, and idempotent
  cancellation/restart behavior;
- a required constrained Copilot analyzer with provider-neutral contracts,
  local schema/evidence validation, notification deferral on failed analysis,
  and no ambient tools;
- a durable Telegram outbox with safe HTML rendering, bounded retries,
  restart-safe destination-wide rate-limit backoff for existing and newly
  enqueued notifications, unknown-send handling, destination disabling, and
  setup validation;
- `doctor`, `run-once`, 30-day operational-record cleanup, and manual
  backup/restore/integrity commands;
- local-only automated .NET and Python tests in CI; normal tests do not contact
  job boards, Telegram, or AI providers, and CI audits locked dependencies.

Remaining release work is primarily live credential-backed smoke testing,
clean-machine native validation, privacy/log review, and remaining native
packaging validation. A local self-contained `win-x64` publish/startup smoke
test passes, and self-contained `osx-arm64`, `osx-x64`, and `linux-x64`
cross-publishes complete successfully.
Worker Docker/Compose and multi-architecture images are deferred until after the
native MVP; the optional JobSpy sidecar image remains available.

## Build and test

```powershell
dotnet restore JobHunter.slnx
dotnet build JobHunter.slnx -c Release --no-restore
dotnet test JobHunter.slnx -c Release --no-build
```

To test the optional JobSpy sidecar on Windows:

```powershell
Push-Location services\jobspy-api
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --require-hashes -r requirements-dev.lock
.\.venv\Scripts\python.exe -m pytest
Pop-Location
```

## Run locally

Apply migrations to the platform-default application-data directory:

```powershell
dotnet run --project src\JobHunter.Worker -- migrate
```

Copy and customize the safe example profile, then start the Worker host:

```powershell
Copy-Item deploy\examples\profile.yaml C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- doctor --Storage:DataDirectory C:\JobHunterData --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- show-profile --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- run-once --source dou --Storage:DataDirectory C:\JobHunterData --Profile:FilePath C:\JobHunterData\profile.yaml
dotnet run --project src\JobHunter.Worker -- run --Storage:DataDirectory C:\JobHunterData --Profile:FilePath C:\JobHunterData\profile.yaml
```

`run` schedules enabled sources and, when Telegram is enabled, dispatches the
durable outbox. `run-once` forces one enabled, non-blocked source scan regardless
of its next scheduled time, but does not start the continuous Telegram
dispatcher. It returns a nonzero exit code unless every selected subscription
completes successfully.

`show-profile` prints the canonical structured profile, including AI preferences,
and the contact-redacted supplemental Markdown CV used as AI evidence. Preferences
do not bypass deterministic hard filters. Treat its console output as private
candidate data.

Database maintenance commands require absolute output paths and never overwrite
an existing restore destination. Put backups only in an operator-selected,
encrypted or access-controlled location:

```powershell
dotnet run --project src\JobHunter.Worker -- backup --output C:\JobHunterBackups\job-hunter.db
dotnet run --project src\JobHunter.Worker -- integrity-check
dotnet run --project src\JobHunter.Worker -- restore --input C:\JobHunterBackups\job-hunter.db --output C:\JobHunterRestore\job-hunter.db
```

No Telegram, Python, or Docker dependency is needed for native source discovery
and persistence. A working Copilot model is required for `run` and `run-once`.
See the
[deployment guide](docs/DEPLOYMENT.md) for Telegram, User Secrets, Copilot,
retention, and operator commands, and the
[JobSpy sidecar guide](services/jobspy-api/README.md) for optional setup.
