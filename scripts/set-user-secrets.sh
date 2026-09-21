#!/usr/bin/env bash
# Stores Telegram credentials in the current OS user's .NET User Secrets store.
# The values are prompted for and never printed or saved in this repository.

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "${script_dir}/.." && pwd)"
project_file="${repository_root}/src/JobHunter.Worker/JobHunter.Worker.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK is required but was not found on PATH." >&2
    exit 1
fi

read -r -s -p "Telegram bot token: " telegram_bot_token
printf '\n'
if [[ -z "${telegram_bot_token}" ]]; then
    echo "Telegram bot token cannot be empty." >&2
    exit 1
fi

read -r -p "Telegram private chat ID: " telegram_chat_id
if [[ ! "${telegram_chat_id}" =~ ^-?[1-9][0-9]*$ ]]; then
    echo "Telegram chat ID must be a non-zero integer." >&2
    exit 1
fi

dotnet user-secrets set "Telegram:BotToken" "${telegram_bot_token}" --project "${project_file}"
dotnet user-secrets set "Telegram:ChatId" "${telegram_chat_id}" --project "${project_file}"
unset telegram_bot_token telegram_chat_id

echo "Telegram credentials were saved to .NET User Secrets for the current OS user."
