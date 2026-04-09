#!/bin/bash

# Usage:
#   ./build-deploy-launch.sh [AdHoc|Release]                        — full build + install + launch
#   ./build-deploy-launch.sh [AdHoc|Release] --launch-only          — skip build/install, just launch last build
#   ./build-deploy-launch.sh [AdHoc|Release] --diagnostics          — build with diagnostic port baked in, start dsrouter, launch
#   ./build-deploy-launch.sh [AdHoc|Release] --diagnostics --launch-only  — skip build, start dsrouter, launch last diagnostic build
#   ./build-deploy-launch.sh [AdHoc|Release] --diagnostics --fast-build   — same as --diagnostics but skips LLVM optimizer (faster AOT)
#
# --diagnostics: bakes DOTNET_DiagnosticPorts into the app at build time (required for AdHoc/Release on physical devices,
#   which don't have get-task-allow and cannot accept env var injection at launch). Starts dsrouter automatically.
#   After launch, run: dotnet-gcdump ps  (note the dsrouter PID printed below)
#   Then:             ./collect_gcdump.sh -p <dsrouter_pid>

BUILD_CONFIG="AdHoc"
LAUNCH_ONLY=false
DIAGNOSTICS=false
FAST_BUILD=false

for arg in "$@"; do
    case "$arg" in
        --launch-only) LAUNCH_ONLY=true ;;
        --diagnostics) DIAGNOSTICS=true ;;
        --fast-build) FAST_BUILD=true ;;
        AdHoc|Release|Debug) BUILD_CONFIG="$arg" ;;
        *) echo "Unknown argument: $arg"; exit 1 ;;
    esac
done

# Resolve paths relative to this script's location (works regardless of cwd)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_FILE="$PROJECT_DIR/SyncfusionPickerMemoryLeak.csproj"
TARGET_FRAMEWORK=$(sed -n 's|.*<iOSTargetFramework>\([^<]*\)</iOSTargetFramework>.*|\1|p' "$PROJECT_FILE")

# =============== iOS CODE SIGNING ===============
# IMPORTANT: Update these to match your iOS provisioning profile and signing identity.
#
# To find your signing identity:
#   security find-identity -v -p codesigning /Library/Keychains/System.keychain
#
# To find your provisioning profile:
#   ls ~/Library/MobileDevice/Provisioning\ Profiles/*.mobileprovision
#   or use Xcode: Window > Organizer > Devices & Simulators > [Device] > Provisioning Profiles
#
# Common patterns:
#   CODESIGNKEY="iPhone Distribution: Your Name (TEAM_ID)"
#   CODESIGNPROVISION="Your Profile Name"
#
# For local development/testing, use:
#   CODESIGNKEY="iPhone Developer: Your Name (TEAM_ID)"
#   CODESIGNPROVISION="iOS Team Provisioning Profile: com.companyname.syncfusionpickermemoryleak"
#
CODESIGNKEY=""
CODESIGNPROVISION=""

# =============== END CODE SIGNING CONFIG ===============

# Check for connected iOS devices (state may be "connected" or "available (paired)")
if ! xcrun devicectl list devices | grep -qiE "connected|available"; then
    echo "Warning: No connected iOS devices detected."
    echo "Make sure your device is connected and trusted."
    exit 1
fi

get_device_id() {
    local force_refresh=${1:-false}
    local MLAUNCH_DEVID_CACHE_FILE="$HOME/.cache/syncfusionpickermemoryleak_mlaunch_devid"

    # Use cache unless force_refresh is true
    if [ "$force_refresh" = false ] && [ -f "$MLAUNCH_DEVID_CACHE_FILE" ]; then
        cat "$MLAUNCH_DEVID_CACHE_FILE"
        return 0
    fi

    # Query mlaunch for current devices
    echo "Getting device list from mlaunch (this may take 10+ seconds)..." >&2
    local DEVNAME=$("$MLAUNCH_PATH" --listdev 2>/dev/null | grep -oE '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{16}' | head -1)

    if [ -z "$DEVNAME" ]; then
        echo "Warning: mlaunch could not find a connected device." >&2
        return 1
    fi

    # Cache the device ID for next time
    mkdir -p "$HOME/.cache"
    echo "$DEVNAME" > "$MLAUNCH_DEVID_CACHE_FILE"
    echo "$DEVNAME"
    return 0
}

launch_app() {
    # Find the .app bundle — prefer ios-arm64 direct output over xcarchive
    APP_BUNDLE=$(find "$PROJECT_DIR/bin/$BUILD_CONFIG/$TARGET_FRAMEWORK/ios-arm64" -maxdepth 1 -name "*.app" -type d 2>/dev/null | head -1)
    if [ -z "$APP_BUNDLE" ]; then
        APP_BUNDLE=$(find "$PROJECT_DIR/bin/$BUILD_CONFIG/$TARGET_FRAMEWORK" -name "*.app" -type d | head -1)
    fi

    # Try to use cached mlaunch path
    MLAUNCH_CACHE_FILE="$HOME/.cache/syncfusionpickermemoryleak_mlaunch_path"
    MLAUNCH_PATH=""

    if [ -f "$MLAUNCH_CACHE_FILE" ]; then
        CACHED_PATH=$(cat "$MLAUNCH_CACHE_FILE")
        if [ -f "$CACHED_PATH" ] && [ -x "$CACHED_PATH" ]; then
            MLAUNCH_PATH="$CACHED_PATH"
        fi
    fi

    # If no valid cache, query dotnet for mlaunch path
    if [ -z "$MLAUNCH_PATH" ]; then
        echo "Locating mlaunch..."
        MLAUNCH_PATH=$(dotnet build "$PROJECT_FILE" -getProperty:MlaunchPath -f "$TARGET_FRAMEWORK" -p:TargetFrameworks="$TARGET_FRAMEWORK" --no-restore 2>&1 | tail -1 | tr -d '[:space:]')

        # Cache the path for next time
        if [ -f "$MLAUNCH_PATH" ] && [ -x "$MLAUNCH_PATH" ]; then
            mkdir -p "$HOME/.cache"
            echo "$MLAUNCH_PATH" > "$MLAUNCH_CACHE_FILE"
        fi
    fi

    if [ -z "$MLAUNCH_PATH" ] || [ ! -f "$MLAUNCH_PATH" ]; then
        echo "Warning: Could not locate mlaunch. Skipping diagnostic launch."
        echo "App is installed and ready for manual launch."
        return
    fi

    if [ -z "$APP_BUNDLE" ]; then
        echo "Warning: Could not find .app bundle for mlaunch. Skipping diagnostic launch."
        echo "App is installed and ready for manual launch."
        return
    fi

    # Get device ID (try cached first)
    MLAUNCH_DEVNAME=$(get_device_id false)
    if [ -z "$MLAUNCH_DEVNAME" ]; then
        echo "App is installed. Launch manually or re-run with device connected."
        return
    fi

    echo "mlaunch device identifier: $MLAUNCH_DEVNAME"

    DSROUTER_PID=""
    if [ "$DIAGNOSTICS" = true ]; then
        DSROUTER_LOG="$PROJECT_DIR/dsrouter.log"
        echo "Starting dsrouter for iOS device... (log: $DSROUTER_LOG)"
        dotnet-dsrouter ios >"$DSROUTER_LOG" 2>&1 &
        DSROUTER_PID=$!
        sleep 2

        echo
        echo "Launching app via mlaunch (diagnostic port baked in at build time)."
        echo "  dsrouter PID : $DSROUTER_PID"
        echo "  To collect a gcdump, open another terminal and run:"
        echo "    ./collect_gcdump.sh -p $DSROUTER_PID"
        echo
    fi

    MLAUNCH_LOG="$PROJECT_DIR/mlaunch.log"

    do_launch() {
        local devname="$1"
        "$MLAUNCH_PATH" --launchdev="$APP_BUNDLE" \
            --devname="$devname" \
            --wait-for-exit \
            --stdout=/dev/null \
            --stderr=/dev/null \
            --argument --connection-mode \
            --argument none \
            >"$MLAUNCH_LOG" 2>"$MLAUNCH_LOG.stderr"
    }

    # Try to launch
    do_launch "$MLAUNCH_DEVNAME" || {
        echo ""
        echo "Launch failed (exit $?). mlaunch log:"
        cat "$MLAUNCH_LOG" 2>/dev/null
        # Filter out Xcode extension noise from stderr
        grep -v "extension point" "$MLAUNCH_LOG.stderr" 2>/dev/null
        echo ""
        echo "Clearing cache and retrying..."
        rm -f "$HOME/.cache/syncfusionpickermemoryleak_mlaunch_devid"
        MLAUNCH_DEVNAME=$(get_device_id true)
        if [ -z "$MLAUNCH_DEVNAME" ]; then
            echo "Failed to find device. Launch aborted."
            kill "$DSROUTER_PID" 2>/dev/null || true
            return 1
        fi
        echo "Retrying launch with new device ID: $MLAUNCH_DEVNAME"
        do_launch "$MLAUNCH_DEVNAME" || {
            RETRY_EXIT=$?
            kill "$DSROUTER_PID" 2>/dev/null || true
            return $RETRY_EXIT
        }
    }

    kill "$DSROUTER_PID" 2>/dev/null || true
}

if [ "$LAUNCH_ONLY" = true ]; then
    echo "Skipping build — launching last $BUILD_CONFIG build..."
    launch_app
    exit 0
fi

# Set Keychain to not lock during the build (optional password via env var)
if [ -n "$KEYCHAIN_PASSWORD" ]; then
    security unlock-keychain -p "$KEYCHAIN_PASSWORD" ~/Library/Keychains/login.keychain-db
else
    security set-keychain-settings -t 3600 ~/Library/Keychains/login.keychain-db
fi

echo "Cleaning previous builds..."
# For multi-targeted projects, manually clean iOS output instead of dotnet clean -f
rm -rf "$PROJECT_DIR/bin/$BUILD_CONFIG/$TARGET_FRAMEWORK"
rm -rf "$PROJECT_DIR/obj"

echo
DIAG_PROPS=""
if [ "$DIAGNOSTICS" = true ]; then
    DIAG_PROPS="-p:DiagnosticAddress=127.0.0.1 -p:DiagnosticPort=9000 -p:DiagnosticSuspend=false -p:DiagnosticListenMode=listen"
    echo "Diagnostics enabled: baking diagnostic port into app (127.0.0.1:9000, listen, nosuspend)."
fi

FAST_BUILD_PROPS=""
if [ "$FAST_BUILD" = true ]; then
    FAST_BUILD_PROPS="-p:MtouchUseLlvm=false"
    echo "Fast build enabled: LLVM optimizer disabled (faster AOT compile, slightly less optimized code)."
    echo
fi

echo "Building and publishing $BUILD_CONFIG configuration..."
# Override TargetFrameworks so MSBuild never evaluates the Android TFM (which requires a different SDK)
# shellcheck disable=SC2086
if ! dotnet publish "$PROJECT_FILE" -c "$BUILD_CONFIG" -f "$TARGET_FRAMEWORK" \
        -p:TargetFrameworks="$TARGET_FRAMEWORK" \
        -p:RuntimeIdentifier=ios-arm64 \
        -p:CodesignKey="$CODESIGNKEY" \
        -p:CodesignProvision="$CODESIGNPROVISION" \
        -p:ArchiveOnBuild=true \
        -p:_BundlerDebug=true \
        $DIAG_PROPS $FAST_BUILD_PROPS; then
    echo; echo "Build failed!"; exit 1
fi

echo
echo "Build successful. Looking for IPA file..."
IPA_PATH=$(find "$PROJECT_DIR/bin/$BUILD_CONFIG/$TARGET_FRAMEWORK" -name "*.ipa" | head -1)
if [ -z "$IPA_PATH" ]; then
    echo; echo "Could not find .ipa file!"; exit 1
fi

echo "Found IPA: $IPA_PATH"
echo "Getting device identifier..."
DEVICE_UDID=$(xcrun devicectl list devices | grep -iE "connected|available" | head -1 | grep -oiE '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}')
if [ -z "$DEVICE_UDID" ]; then
    echo "Could not find device UDID!"
    echo "Debug: Device list output:"
    xcrun devicectl list devices | grep -iE "connected|available"
    exit 1
fi

echo "Installing IPA to device: $DEVICE_UDID"
if ! xcrun devicectl device install app --device "$DEVICE_UDID" "$IPA_PATH"; then
    echo; echo "IPA installation failed! Device may not be connected."; exit 1
fi

echo
echo "IPA deployment complete."
launch_app
