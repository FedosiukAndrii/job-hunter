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

Implementation is in progress.

The current foundation includes:

- the approved .NET 10 solution and project boundaries;
- validated Worker and storage configuration;
- native per-user application-data path resolution;
- a single-instance file lease acquired before database initialization;
- EF Core SQLite startup migrations with foreign keys, WAL, and a bounded busy
  timeout;
- initial domain value objects for canonical job URLs and deterministic scores;
- automated unit and integration tests, executed by CI on Windows and Linux.

DOU ingestion, the full persistence model, scoring orchestration, Telegram, and
optional AI/JobSpy adapters remain upcoming work.

## Build and test

```powershell
dotnet restore JobHunter.slnx
dotnet build JobHunter.slnx -c Release --no-restore
dotnet test JobHunter.slnx -c Release --no-build
```

Normal tests use only local files and SQLite databases in temporary directories;
they do not contact job boards, Telegram, or AI providers.

## Run the foundation

Apply migrations to the platform-default application-data directory:

```powershell
dotnet run --project src/JobHunter.Worker -- migrate
```

Start the Worker host:

```powershell
dotnet run --project src/JobHunter.Worker -- run
```

For an isolated local database, pass an absolute directory through standard
.NET command-line configuration:

```powershell
dotnet run --project src/JobHunter.Worker -- migrate --Storage:DataDirectory C:\JobHunterData
```

No Telegram or AI credentials are needed for the current rules-only foundation.
