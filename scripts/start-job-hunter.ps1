[CmdletBinding()]
param()

# Starts Job Hunter in its normal continuous-search mode on Windows.
# Telegram credentials remain in .NET User Secrets, never in this script.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workerProject = Join-Path $repositoryRoot 'src\JobHunter.Worker\JobHunter.Worker.csproj'
$jobSpyDirectory = Join-Path $repositoryRoot 'services\jobspy-api'
$dataDirectory = if ([string]::IsNullOrWhiteSpace($env:JOB_HUNTER_DATA_DIR)) {
    Join-Path $env:LOCALAPPDATA 'JobHunter'
}
else {
    $env:JOB_HUNTER_DATA_DIR
}
$profilePath = Join-Path $dataDirectory 'profile.yaml'
$jobSpyPython = Join-Path $jobSpyDirectory '.venv\Scripts\python.exe'
$jobSpyLog = Join-Path $dataDirectory 'jobspy.log'
$jobSpyPidFile = Join-Path $dataDirectory 'jobspy.pid'

function Require-Command {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$DisplayName
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$DisplayName is required but was not found on PATH."
    }
}

function Test-TelegramCredentials {
    $secretNames = & dotnet user-secrets list --project $workerProject 2>$null |
        ForEach-Object { ($_ -split ' = ', 2)[0] }
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read .NET User Secrets.'
    }

    return $secretNames -contains 'Telegram:BotToken' -and
        $secretNames -contains 'Telegram:ChatId'
}

function Initialize-Profile {
    New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null

    if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
        Copy-Item (Join-Path $repositoryRoot 'deploy\examples\profile.yaml') $profilePath
        Copy-Item (Join-Path $repositoryRoot 'deploy\examples\cv.md') (Join-Path $dataDirectory 'cv.md')
        Write-Host "Created $profilePath and $(Join-Path $dataDirectory 'cv.md') from the examples."
        Write-Host 'Update them with your details before relying on the search results.'
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $dataDirectory 'cv.md') -PathType Leaf)) {
        Copy-Item (Join-Path $repositoryRoot 'deploy\examples\cv.md') (Join-Path $dataDirectory 'cv.md')
        Write-Host "Created the missing $(Join-Path $dataDirectory 'cv.md') from the example."
    }
}

function Initialize-Telegram {
    if (Test-TelegramCredentials) {
        return
    }

    Write-Host 'Telegram credentials are not configured for this Windows user.'
    & (Join-Path $repositoryRoot 'scripts\set-user-secrets.ps1')

    Write-Host 'Validating Telegram and sending its one-time test message...'
    & dotnet run --project $workerProject -- setup-telegram `
        --Storage:DataDirectory $dataDirectory `
        --Profile:FilePath $profilePath `
        --Telegram:Enabled=true
    if ($LASTEXITCODE -ne 0) {
        throw 'Telegram setup failed.'
    }
}

function Start-JobSpy {
    if (-not (Test-Path -LiteralPath $jobSpyPython -PathType Leaf)) {
        Require-Command -Name py -DisplayName 'Python Launcher (py) with Python 3.11'
        & py -3.11 -m venv (Join-Path $jobSpyDirectory '.venv')
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to create the JobSpy Python environment.'
        }

        & $jobSpyPython -m pip install --require-hashes `
            -r (Join-Path $jobSpyDirectory 'requirements-dev.lock')
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to install JobSpy dependencies.'
        }
    }

    if (Test-Path -LiteralPath $jobSpyPidFile -PathType Leaf) {
        $existingProcess = Get-Process -Id (Get-Content -LiteralPath $jobSpyPidFile -Raw).Trim() -ErrorAction SilentlyContinue
        if ($null -ne $existingProcess) {
            Write-Host 'JobSpy is already running.'
            return
        }

        Remove-Item -LiteralPath $jobSpyPidFile
    }

    $jobSpyProcess = Start-Process -FilePath $jobSpyPython `
        -ArgumentList '-m', 'uvicorn', 'app.main:app', '--host', '127.0.0.1', '--port', '8080', '--no-server-header' `
        -WorkingDirectory $jobSpyDirectory `
        -RedirectStandardOutput $jobSpyLog `
        -RedirectStandardError (Join-Path $dataDirectory 'jobspy-error.log') `
        -PassThru
    Set-Content -LiteralPath $jobSpyPidFile -Value $jobSpyProcess.Id -NoNewline
    Write-Host "Started JobSpy (log: $jobSpyLog)."
}

Require-Command -Name dotnet -DisplayName '.NET SDK'
Initialize-Profile
Initialize-Telegram
Start-JobSpy

Write-Host 'Starting Job Hunter. Press Ctrl+C to stop the Worker; JobSpy remains available for the next launch.'
& dotnet run --project $workerProject -- run `
    --Storage:DataDirectory $dataDirectory `
    --Profile:FilePath $profilePath `
    --Telegram:Enabled=true
exit $LASTEXITCODE
