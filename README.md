# Job Hunter

A local .NET Worker that finds .NET vacancies on DOU and LinkedIn, matches
them to your profile with GitHub Copilot, and sends suitable jobs to Telegram.

```text
DOU + LinkedIn JobSpy → SQLite → filters + Copilot → Telegram
```

Docker is not required. Your profile, CV, SQLite database, and Worker state
stay on your machine; Copilot and Telegram need an internet connection.

## Prerequisites

- **.NET SDK 10.0.300+** (see `global.json`).
- GitHub Copilot subscription, [GitHub CLI](https://cli.github.com/), and
  [Copilot CLI](https://github.com/github/copilot-cli#installation):

  ```bash
  gh auth login
  copilot auth login
  ```

  Use the same GitHub account for both; the Worker uses the credentials from
  `copilot auth login`.
- A Telegram bot from [@BotFather](https://t.me/BotFather). Send it `/start`;
  the setup script will request its credentials.
- **Python 3.11+** for the local LinkedIn JobSpy sidecar. LinkedIn is enabled
  by default. To run DOU only, pass `--Sources:LinkedInJobSpy:Enabled=false`
  to every Worker command.

For a private chat, use your numeric Telegram user ID as the chat ID (for
example, retrieve it with [@userinfobot](https://t.me/userinfobot)).

## Profile and CV format

Start with [`deploy/examples/profile.yaml`](deploy/examples/profile.yaml) and
[`deploy/examples/cv.md`](deploy/examples/cv.md). Only `schemaVersion` and
`targetTitles` are required; other fields refine matching.

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

`requiredSkills`, `hardFilters`, and `aiPreferences` are optional. See the
[profile schema](schemas/candidate-profile.schema.json) for all fields.
`supplementalCvPath` must point to a Markdown CV; a factual summary, skills,
experience, education, and languages are sufficient.

## Quick start: macOS / Linux

Start JobSpy in one terminal, then run the Worker commands from the repository
root in a second. Keep application data outside the Git checkout.

```bash
# Terminal 1: local LinkedIn JobSpy sidecar
cd services/jobspy-api
python3.11 -m venv .venv
./.venv/bin/python -m pip install --require-hashes -r requirements-dev.lock
./.venv/bin/python -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --no-server-header
```

```bash
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

The setup scripts save `Telegram:BotToken` and `Telegram:ChatId` to
[.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets),
not Git, `appsettings.json`, or SQLite. Do not put a token in `.env`, a
profile, documentation, or Worker arguments. Rerun the script to change it.

If `doctor` reports that the configured Copilot model is unavailable, add
`--AI:Copilot:Model=auto` to both `doctor` and `run`.

## Daily use

After setup, run the final `run` command for your OS. It continuously scans
enabled sources and sends new matches. Defaults: vacancies from the past 24
hours and quiet hours from 22:00–09:00 (`Europe/Kyiv`). Override them with
`--Search:LookbackHours=48` or `Worker:QuietHours:*`.

To force a single scan without starting the continuous process:

```bash
dotnet run --project src/JobHunter.Worker -- run-once --source dou \
  --Storage:DataDirectory "$data_dir" \
  --Profile:FilePath "$data_dir/profile.yaml" \
  --Telegram:Enabled=true
```

`run-once` queues notifications; `run` also delivers them.

### One-command launch scripts

The launchers start JobSpy and continuous Worker mode, and create example
profile/CV files on first run. They securely prompt for missing Telegram
credentials and save them in .NET User Secrets.

```bash
# macOS — make executable once, then double-click it in Finder or run it here.
chmod +x scripts/start-job-hunter.command scripts/start-job-hunter.sh
./scripts/start-job-hunter.command
```

```powershell
# Windows — double-click start-job-hunter.cmd in Explorer, or run it here.
.\scripts\start-job-hunter.cmd
```

Set `JOB_HUNTER_DATA_DIR` to an absolute local directory to use a separate
data set. Logs and process-ID files are kept there.

## JobSpy and further reading

- [JobSpy sidecar](services/jobspy-api/README.md): the default local LinkedIn
  source, which requires Python 3.11+.
- [Deployment and operations](docs/DEPLOYMENT.md): backups, restore,
  retention, quiet hours, and native publishing.
- [Architecture](docs/ARCHITECTURE.md) and [product requirements](docs/PRD.md).
