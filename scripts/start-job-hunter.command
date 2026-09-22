#!/usr/bin/env bash
# macOS Finder launcher. This extension opens in Terminal when double-clicked.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "${script_dir}/start-job-hunter.sh" "$@"
