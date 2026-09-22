#!/usr/bin/env bash
# Starts Job Hunter in its normal continuous-search mode on macOS.
# Telegram credentials remain in .NET User Secrets, never in this script.

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "${script_dir}/.." && pwd)"
worker_project="${repository_root}/src/JobHunter.Worker/JobHunter.Worker.csproj"
jobspy_directory="${repository_root}/services/jobspy-api"
data_directory="${JOB_HUNTER_DATA_DIR:-${HOME}/Library/Application Support/JobHunter}"
profile_path="${data_directory}/profile.yaml"
jobspy_python="${jobspy_directory}/.venv/bin/python"
jobspy_log="${data_directory}/jobspy.log"
jobspy_pid_file="${data_directory}/jobspy.pid"

require_command() {
    local command_name="$1"
    local display_name="$2"

    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "${display_name} is required but was not found on PATH." >&2
        exit 1
    fi
}

has_telegram_credentials() {
    local secret_names

    if ! secret_names="$(dotnet user-secrets list --project "${worker_project}" 2>/dev/null | awk -F ' = ' '{ print $1 }')"; then
        echo "Unable to read .NET User Secrets." >&2
        exit 1
    fi

    grep -Fqx 'Telegram:BotToken' <<<"${secret_names}" \
        && grep -Fqx 'Telegram:ChatId' <<<"${secret_names}"
}

prepare_profile() {
    mkdir -p "${data_directory}"

    if [[ ! -f "${profile_path}" ]]; then
        cp "${repository_root}/deploy/examples/profile.yaml" "${profile_path}"
        cp "${repository_root}/deploy/examples/cv.md" "${data_directory}/cv.md"
        echo "Created ${profile_path} and ${data_directory}/cv.md from the examples."
        echo "Update them with your details before relying on the search results."
    elif [[ ! -f "${data_directory}/cv.md" ]]; then
        cp "${repository_root}/deploy/examples/cv.md" "${data_directory}/cv.md"
        echo "Created the missing ${data_directory}/cv.md from the example."
    fi
}

ensure_telegram_setup() {
    if has_telegram_credentials; then
        return
    fi

    echo "Telegram credentials are not configured for this macOS user."
    "${repository_root}/scripts/set-user-secrets.sh"

    echo "Validating Telegram and sending its one-time test message..."
    dotnet run --project "${worker_project}" -- setup-telegram \
        --Storage:DataDirectory "${data_directory}" \
        --Profile:FilePath "${profile_path}" \
        --Telegram:Enabled=true
}

start_jobspy() {
    if [[ ! -x "${jobspy_python}" ]]; then
        require_command python3.11 "Python 3.11"
        python3.11 -m venv "${jobspy_directory}/.venv"
        "${jobspy_python}" -m pip install --require-hashes \
            -r "${jobspy_directory}/requirements-dev.lock"
    fi

    if [[ -f "${jobspy_pid_file}" ]] \
        && kill -0 "$(<"${jobspy_pid_file}")" 2>/dev/null; then
        echo "JobSpy is already running."
        return
    fi

    rm -f "${jobspy_pid_file}"
    (
        cd "${jobspy_directory}"
        exec "${jobspy_python}" -m uvicorn app.main:app \
            --host 127.0.0.1 --port 8080 --no-server-header
    ) >>"${jobspy_log}" 2>&1 &
    echo "$!" >"${jobspy_pid_file}"
    echo "Started JobSpy (log: ${jobspy_log})."
}

require_command dotnet ".NET SDK"
prepare_profile
ensure_telegram_setup
start_jobspy

echo "Starting Job Hunter. Press Ctrl+C to stop the Worker; JobSpy remains available for the next launch."
dotnet run --project "${worker_project}" -- run \
    --Storage:DataDirectory "${data_directory}" \
    --Profile:FilePath "${profile_path}" \
    --Telegram:Enabled=true
