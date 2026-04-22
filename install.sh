#!/usr/bin/env bash
# pw-vault — build self-contained single-file binary and install it as `pwvault`.
#
# Usage:
#   ./install.sh                   # install to ~/.local/bin (no sudo)
#   ./install.sh --system          # install to /usr/local/bin (uses sudo)
#   ./install.sh --prefix <DIR>    # custom prefix (binary goes to <DIR>)
#   ./install.sh --build-only      # publish the binary to ./artifacts, don't install

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARTIFACT_DIR="${SCRIPT_DIR}/artifacts/pwvault"
RUNTIME="linux-x64"
DEST_DIR="${HOME}/.local/bin"
BUILD_ONLY=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --system)     DEST_DIR="/usr/local/bin"; shift ;;
        --prefix)     DEST_DIR="$2"; shift 2 ;;
        --build-only) BUILD_ONLY=1; shift ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$(uname -s)" in
    Linux*)   RUNTIME="linux-x64" ;;
    Darwin*)  RUNTIME="$(uname -m | grep -q arm64 && echo osx-arm64 || echo osx-x64)" ;;
    *)        echo "Unsupported OS: $(uname -s). Edit RUNTIME manually." >&2; exit 1 ;;
esac

echo ">>> Publishing self-contained single-file binary ($RUNTIME)..."
dotnet publish "${SCRIPT_DIR}/src/PwVault.Cli/PwVault.Cli.csproj" \
    -c Release \
    -r "${RUNTIME}" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "${ARTIFACT_DIR}"

BIN="${ARTIFACT_DIR}/PwVault.Cli"
if [[ ! -x "${BIN}" ]]; then
    echo "Publish succeeded but binary not found at ${BIN}" >&2
    exit 1
fi

if [[ "${BUILD_ONLY}" -eq 1 ]]; then
    echo ">>> Built: ${BIN} ($(du -h "${BIN}" | cut -f1))"
    exit 0
fi

TARGET="${DEST_DIR}/pwvault"
if [[ "${DEST_DIR}" == "/usr/local/bin" || ! -w "${DEST_DIR}" ]]; then
    echo ">>> Installing to ${TARGET} (sudo)..."
    sudo install -m 755 "${BIN}" "${TARGET}"
else
    mkdir -p "${DEST_DIR}"
    echo ">>> Installing to ${TARGET}..."
    install -m 755 "${BIN}" "${TARGET}"
fi

echo ">>> Installed: $(command -v pwvault || echo "${TARGET}") ($(du -h "${TARGET}" | cut -f1))"
if ! command -v pwvault >/dev/null 2>&1; then
    echo ">>> NOTE: ${DEST_DIR} is not in your PATH. Add it:"
    echo "    echo 'export PATH=\"${DEST_DIR}:\$PATH\"' >> ~/.bashrc && source ~/.bashrc"
fi
