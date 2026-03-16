#!/bin/bash

# Collect a managed gcdump given the managed PID (as printed by dotnet-gcdump ps).
#
# Usage:
#   collect_gcdump.sh -p <managed_pid>
#   collect_gcdump.sh -p <managed_pid> -o /tmp/gcdumps --report
#
# Notes:
# - Requires dotnet-gcdump installed: dotnet tool install --global dotnet-gcdump
# - Run dotnet-gcdump ps to find the managed PID after launching the app via build-deploy-launch.sh
# - dsrouter (started by build-deploy-launch.sh) must still be running when you collect

set -euo pipefail

MANAGED_PID=""
OUT_DIR="$(pwd)/gcdumps"
REPORT=false

usage() {
    echo "Usage: $0 -p <managed_pid> [-o <output_dir>] [--report]"
    echo ""
    echo "  -p, --pid       Managed PID (from: dotnet-gcdump ps)"
    echo "  -o, --out-dir   Output directory (default: ./gcdumps)"
    echo "  --report        Also generate a text report alongside the .gcdump"
    exit 1
}

while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--pid)
            MANAGED_PID="$2"
            shift 2
            ;;
        -o|--out-dir)
            OUT_DIR="$2"
            shift 2
            ;;
        --report)
            REPORT=true
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            echo "Unknown argument: $1"
            usage
            ;;
    esac
done

if [ -z "$MANAGED_PID" ]; then
    echo "Error: managed PID is required."
    usage
fi

if ! command -v dotnet-gcdump &>/dev/null; then
    echo "Error: dotnet-gcdump not found in PATH."
    echo "Install with: dotnet tool install --global dotnet-gcdump"
    exit 1
fi

mkdir -p "$OUT_DIR"

TIMESTAMP=$(date +"%Y%m%d-%H%M%S")
GCDUMP_FILE="$OUT_DIR/gcdump-$TIMESTAMP-$MANAGED_PID.gcdump"

echo "Collecting gcdump for managed PID $MANAGED_PID -> $GCDUMP_FILE"
dotnet-gcdump collect -p "$MANAGED_PID" -o "$GCDUMP_FILE"
echo "gcdump collected: $GCDUMP_FILE"

if [ "$REPORT" = true ]; then
    REPORT_FILE="${GCDUMP_FILE%.gcdump}.txt"
    echo "Generating text report: $REPORT_FILE"
    dotnet-gcdump report "$GCDUMP_FILE" > "$REPORT_FILE"
    echo "Report written: $REPORT_FILE"
fi
