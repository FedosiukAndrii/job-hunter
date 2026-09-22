#!/bin/zsh
# Reads the GitHub Copilot token from the login keychain without writing it to
# the LaunchAgent plist, command-line arguments, or application logs.
set -euo pipefail

token="$(/usr/bin/security find-generic-password -s 'gh-github-pat' -w 2>/dev/null)" || {
  print -u2 'Unable to read the gh-github-pat item from the login keychain.'
  exit 78
}

if [[ -z "$token" ]]; then
  print -u2 'The gh-github-pat Keychain item is empty.'
  exit 78
fi

export AI__Copilot__GitHubToken="$token"
exec "/Users/andrii.fedosiuk/Documents/job-hunter/src/JobHunter.Worker/bin/Debug/net10.0/JobHunter.Worker" \
  run \
  --Storage:DataDirectory "/Users/andrii.fedosiuk/Documents/job-hunter/User data" \
  --Profile:FilePath "/Users/andrii.fedosiuk/Documents/job-hunter/User data/profile.yaml" \
  --Telegram:Enabled=true \
  --Search:LookbackHours=24 \
  --Sources:LinkedInJobSpy:RequestTimeoutSeconds=150 \
  --Worker:QuietHours:StartLocalTime=23:00 \
  --Logging:LogLevel:Microsoft.EntityFrameworkCore=Warning
