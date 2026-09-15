# Job Hunter

Local, cross-platform .NET service that discovers .NET vacancies, ranks them
against a personal profile, optionally analyses them with AI, and sends relevant
results to Telegram.

This repository intentionally starts as a documentation-first project. Its
implementation contract is:

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

Planning complete. No production implementation exists yet.
