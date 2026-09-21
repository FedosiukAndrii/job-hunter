# Job Hunter

A local .NET application that reads .NET vacancies from DOU, filters them
against your profile, evaluates the match with GitHub Copilot, and sends
relevant results to Telegram.

```text
DOU → local Worker + SQLite → filters → Copilot → Telegram
```

The default workflow runs DOU and the local LinkedIn JobSpy sidecar. It does
not require Docker. Your profile, CV, SQLite database, Worker state, and
Copilot workspace remain on your machine. Copilot and Telegram requests require
an internet connection.

## Prerequisites

- **.NET SDK 10.0.300 or later** — the baseline version is defined in
  `global.json`.
- A GitHub account with an active Copilot subscription. Install the
  [GitHub CLI (`gh`)](https://cli.github.com/) to select and verify the local
  GitHub account:

  ```bash
  gh auth login
  gh auth status
  ```

- The active account reported by `gh auth status` must be the one with the
  Copilot entitlement. Also install the [GitHub Copilot CLI](https://github.com/github/copilot-cli#installation)
  and authenticate its local session:

  ```bash
  copilot auth login
  ```

  The Worker uses the credentials saved by `copilot auth login`; `gh auth login`
  alone does not provide the stored Copilot credentials required by the SDK.
  The .NET SDK manages the compatible Copilot runtime for the Worker. Do not add
  a GitHub token to project files or Worker command-line arguments.
- A Telegram bot created with [@BotFather](https://t.me/BotFather) and a
  private chat with it. Telegram is enabled by default, so send the bot `/start`
  before setup and configure its credentials with the provided User Secrets
  script.
- **Python 3.11+** for the local LinkedIn JobSpy sidecar. LinkedIn is enabled
  by default. To run DOU only, pass `--Sources:LinkedInJobSpy:Enabled=false`
  to every Worker command.

For a private chat, the required `chat ID` is your numeric Telegram user ID.
You can retrieve it with an ID bot such as [@userinfobot](https://t.me/userinfobot).

## Profile and CV format

Start with [`deploy/examples/profile.yaml`](deploy/examples/profile.yaml) and
[`deploy/examples/cv.md`](deploy/examples/cv.md). `schemaVersion` and
`targetTitles` are required; the remaining fields refine matching.

```yaml
schemaVersion: 2
targetTitles:
  - Senior .NET Backend Developer
requiredSkills:
  - .NET
  - C#
hardFilters:
  locations:
    - Ukraine
  remotePolicy: remoteOnly # any | remoteOnly | remoteOrHybrid | onSiteOnly
  excludedKeywords:
    - unpaid
aiPreferences:
  - Prefer backend-focused roles.
supplementalCvPath: cv.md # relative to this YAML file, or an absolute path
```

`requiredSkills`, `hardFilters`, and `aiPreferences` are optional. Use
`aiPreferences` for concise matching preferences (up to 12 entries), not facts
that contradict the CV. The full field definition is in
[`schemas/candidate-profile.schema.json`](schemas/candidate-profile.schema.json).

`supplementalCvPath` must point to a `.md` file. The Markdown has no required
heading layout, but this compact structure gives Copilot useful evidence:

```md
# Your name

## Summary
Senior backend engineer focused on .NET services and APIs.

## Skills
- C#, .NET, ASP.NET Core, SQL, Azure

## Experience
### Company — Senior Backend Engineer
- Built and maintained production APIs and background services.

## Education and languages
- BSc in Computer Science; English: Upper-Intermediate.
```

## Quick start: macOS / Linux

Start JobSpy in one terminal and keep it running. Then run the Worker commands
in a second terminal from the repository root. Keep application data outside
the Git checkout and cloud-synced folders.

```bash
# Terminal 1: local LinkedIn JobSpy sidecar
cd services/jobspy-api
python3.11 -m venv .venv
./.venv/bin/python -m pip install --require-hashes -r requirements-dev.lock
./.venv/bin/python -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --no-server-header
```

```bash
# Terminal 2: Worker, from the repository root
# macOS
data_dir="$HOME/Library/Application Support/JobHunter"

# Linux: use this line instead
# data_dir="${XDG_DATA_HOME:-$HOME/.local/share}/job-hunter"

mkdir -p "$data_dir"
cp deploy/examples/profile.yaml deploy/examples/cv.md "$data_dir/"
${EDITOR:-vi} "$data_dir/profile.yaml"

# Prompts for the token and chat ID without echoing the token.
./scripts/set-user-secrets.sh

dotnet restore JobHunter.slnx
dotnet run --project src/JobHunter.Worker -- migrate --Storage:DataDirectory "$data_dir"

# Sends one test message and enables the Telegram destination.
dotnet run --project src/JobHunter.Worker -- setup-telegram \
  --Storage:DataDirectory "$data_dir" \
  --Profile:FilePath "$data_dir/profile.yaml" \
  --Telegram:Enabled=true

# Checks the profile, SQLite, Telegram, and local Copilot authentication.
dotnet run --project src/JobHunter.Worker -- doctor \
  --Storage:DataDirectory "$data_dir" \
  --Profile:FilePath "$data_dir/profile.yaml" \
  --Telegram:Enabled=true

# Starts the continuous search. Stop it with Ctrl+C.
dotnet run --project src/JobHunter.Worker -- run \
  --Storage:DataDirectory "$data_dir" \
  --Profile:FilePath "$data_dir/profile.yaml" \
  --Telegram:Enabled=true
```

## Quick start: Windows PowerShell

```powershell
# Terminal 1: local LinkedIn JobSpy sidecar
Push-Location services\jobspy-api
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --require-hashes -r requirements-dev.lock
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --no-server-header
```

```powershell
# Terminal 2: Worker, from the repository root
$dataDir = Join-Path $env:LOCALAPPDATA 'JobHunter'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
Copy-Item deploy\examples\profile.yaml, deploy\examples\cv.md -Destination $dataDir
notepad "$dataDir\profile.yaml"

.\scripts\set-user-secrets.ps1

dotnet restore JobHunter.slnx
dotnet run --project src\JobHunter.Worker -- migrate --Storage:DataDirectory $dataDir
dotnet run --project src\JobHunter.Worker -- setup-telegram --Storage:DataDirectory $dataDir --Profile:FilePath "$dataDir\profile.yaml" --Telegram:Enabled=true
dotnet run --project src\JobHunter.Worker -- doctor --Storage:DataDirectory $dataDir --Profile:FilePath "$dataDir\profile.yaml" --Telegram:Enabled=true
dotnet run --project src\JobHunter.Worker -- run --Storage:DataDirectory $dataDir --Profile:FilePath "$dataDir\profile.yaml" --Telegram:Enabled=true
```

If PowerShell blocks the local script, run `Set-ExecutionPolicy -Scope Process Bypass`
for the current process only, then rerun `.\scripts\set-user-secrets.ps1`.

## Secrets and local Copilot authentication

`scripts/set-user-secrets.sh` and `scripts/set-user-secrets.ps1` save
`Telegram:BotToken` and `Telegram:ChatId` to [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).
Those values belong to the current OS user and are not written to
`appsettings.json`, Git, or SQLite. Run the relevant script again to replace
either value.

User Secrets are appropriate for local development and prevent accidental
commits, but they are not a complete secret store for a shared server. Never
pass a Telegram token as a Worker argument or save it in `.env`, a profile,
or documentation.

The Copilot entitlement comes from the GitHub account used by the local Copilot
sign-in. If `doctor` reports that the configured `gpt-5.6-luna` model is
unavailable for your subscription, explicitly choose an available model, for
example by adding `--AI:Copilot:Model=auto` to both `doctor` and `run`.

## Daily use

After initial setup, run the final `run` command for your operating system.
It runs in the foreground, scans DOU and LinkedIn JobSpy regularly, and sends
newly accepted vacancies. By default, the Worker pauses both scans and
notifications from 22:00 to 09:00 in `Europe/Kyiv`; change
`Worker:QuietHours:*` if needed.
It searches vacancies posted within the past 24 hours across every enabled
source. Change the shared period with `Search:LookbackHours`, for example
`--Search:LookbackHours=48`.

To force a single scan without starting the continuous process:

```bash
dotnet run --project src/JobHunter.Worker -- run-once --source dou \
  --Storage:DataDirectory "$data_dir" \
  --Profile:FilePath "$data_dir/profile.yaml" \
  --Telegram:Enabled=true
```

`run-once` only enqueues notifications; `run` starts the dispatcher that
delivers them. `show-profile` validates the profile locally, but its output
may contain personal data.

## JobSpy and further reading

- [JobSpy sidecar](services/jobspy-api/README.md): the default local LinkedIn
  source, which requires Python 3.11+.
- [Deployment and operations](docs/DEPLOYMENT.md): backups, restore,
  retention, quiet hours, and native publishing.
- [Architecture](docs/ARCHITECTURE.md) and [product requirements](docs/PRD.md).
