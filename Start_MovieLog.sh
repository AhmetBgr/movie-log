#!/usr/bin/env bash

set -u

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$script_dir" || exit 1

profile="https"
app_url="https://localhost:7008"
open_browser=true
restart_on_exit=true

show_help() {
    cat <<'EOF'
Usage: ./Start_MovieLog.sh [options]

Options:
  --http        Use http://localhost:5017 instead of HTTPS.
  --no-browser  Do not open a browser automatically.
  --once        Exit when the server stops instead of offering to restart it.
  -h, --help    Show this help.
EOF
}

while (($# > 0)); do
    case "$1" in
        --http)
            profile="http"
            app_url="http://localhost:5017"
            ;;
        --no-browser)
            open_browser=false
            ;;
        --once)
            restart_on_exit=false
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            printf 'Unknown option: %s\n\n' "$1" >&2
            show_help >&2
            exit 2
            ;;
    esac
    shift
done

if ! command -v dotnet >/dev/null 2>&1; then
    cat >&2 <<'EOF'
Movie Log requires the .NET 10 SDK, but the dotnet command was not found.
Install the .NET 10 SDK for your Linux distribution, then run this script again.
Download page: https://dotnet.microsoft.com/download/dotnet/10.0
EOF
    exit 1
fi

dotnet_major="$(dotnet --version | cut -d. -f1)"
if [[ ! "$dotnet_major" =~ ^[0-9]+$ ]] || ((dotnet_major < 10)); then
    printf 'Movie Log targets .NET 10. Installed SDK: %s\n' "$(dotnet --version)" >&2
    printf 'Install the .NET 10 SDK, then run this script again.\n' >&2
    exit 1
fi

open_app() {
    $open_browser || return 0

    (
        sleep 2
        if command -v xdg-open >/dev/null 2>&1; then
            xdg-open "$app_url"
        elif command -v gio >/dev/null 2>&1; then
            gio open "$app_url"
        else
            exit 0
        fi
    ) >/dev/null 2>&1 &
}

first_run=true
while true; do
    printf 'Starting Movie Log at %s\n' "$app_url"

    if $first_run; then
        open_app
        first_run=false
    fi

    dotnet run --launch-profile "$profile"
    exit_code=$?

    if ! $restart_on_exit; then
        exit "$exit_code"
    fi

    printf '\nServer process ended with exit code %s.\n' "$exit_code"
    if ! read -r -p 'Press Enter to restart, or Ctrl+C to quit. '; then
        exit "$exit_code"
    fi
done
