# Job Hunter

Local, cross-platform .NET service that discovers .NET vacancies, ranks them
against a personal profile, optionally analyses them with AI, and sends relevant
results to Telegram.

The implementation contract is:

- [Product requirements document](docs/PRD.md)
- [Implementation plan](docs/IMPLEMENTATION_PLAN.md)
- [Deployment guide](docs/DEPLOYMENT.md)
- [Copilot instructions](.github/copilot-instructions.md)

## Target architecture

```text
DOU RSS ─────────┐
                 ├─> .NET Worker -> SQLite -> rules -> optional AI -> Telegram
JobSpy API opt-in ┘
```

The native .NET Worker is the primary runtime: it runs directly on Windows,
macOS, and Linux without Docker. Docker Compose is an optional deployment
profile for a reproducible isolated JobSpy sidecar or a home-lab/server setup.

The .NET Worker is the only SQLite writer. JobSpy is an isolated Python
sidecar used only for the experimental, opt-in LinkedIn connector.

## Status

Implementation is in progress. WP-01 through WP-05 now provide:

- the .NET 10 Worker foundation, project boundaries, configuration validation,
  single-instance guard, SQLite migration protocol, and native data paths;
- the full versioned domain/persistence model, idempotent ingestion, source-run
  leases, and backup/restore/integrity-check services;
- YAML/JSON candidate profiles, bounded and redacted supplemental CV loading,
  hard filters, and deterministic `rules-v1` scoring;
- a bounded DOU RSS adapter with safe XML/HTML handling, conditional requests,
  retries, canonical URLs, fixture tests, and optional detail enrichment;
- an isolated, opt-in JobSpy/FastAPI service plus a loopback-only .NET adapter
  with explicit blocked/degraded states and no access-control evasion;
- local-only automated .NET and Python tests in CI; normal tests do not contact
  job boards, Telegram, or AI providers.

WP-06 source scheduling and scoring orchestration, optional AI, and Telegram
delivery remain upcoming. The current `run` command initializes the database and
candidate profile, but does not yet schedule source fetches.

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
dotnet run --project src/JobHunter.Worker -- migrate
```

Copy and customize the safe example profile, then start the Worker host:

```powershell
Copy-Item deploy\examples\profile.yaml C:\JobHunterData\profile.yaml
dotnet run --project src/JobHunter.Worker -- run --Storage:DataDirectory C:\JobHunterData --Profile:FilePath C:\JobHunterData\profile.yaml
```

Database maintenance commands require absolute output paths and never overwrite
an existing restore destination:

```powershell
dotnet run --project src/JobHunter.Worker -- backup --output C:\JobHunterBackups\job-hunter.db
dotnet run --project src/JobHunter.Worker -- integrity-check
dotnet run --project src/JobHunter.Worker -- restore --input C:\JobHunterBackups\job-hunter.db --output C:\JobHunterRestore\job-hunter.db
```

No Telegram, AI, Python, or Docker dependency is needed for the native
DOU/rules foundation. See the [deployment guide](docs/DEPLOYMENT.md) and the
[JobSpy sidecar guide](services/jobspy-api/README.md) for optional setup.
