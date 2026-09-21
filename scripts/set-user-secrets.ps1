[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $repositoryRoot 'src/JobHunter.Worker/JobHunter.Worker.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required but dotnet was not found on PATH.'
}

function Set-WorkerUserSecret {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Value
    )

    & dotnet user-secrets set $Name $Value --project $projectFile
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to save the '$Name' user secret."
    }
}

$botToken = $null
try {
    $secureBotToken = Read-Host 'Telegram bot token' -AsSecureString
    $botToken = [System.Net.NetworkCredential]::new('', $secureBotToken).Password
    if ([string]::IsNullOrWhiteSpace($botToken)) {
        throw 'Telegram bot token cannot be empty.'
    }

    $chatId = Read-Host 'Telegram private chat ID'
    [long]$parsedChatId = 0
    if (-not [long]::TryParse($chatId, [ref]$parsedChatId) -or $parsedChatId -eq 0) {
        throw 'Telegram chat ID must be a non-zero integer.'
    }

    Set-WorkerUserSecret -Name 'Telegram:BotToken' -Value $botToken
    Set-WorkerUserSecret -Name 'Telegram:ChatId' -Value $chatId
    Write-Host 'Telegram credentials were saved to .NET User Secrets for the current OS user.'
}
finally {
    $botToken = $null
    $secureBotToken = $null
}
