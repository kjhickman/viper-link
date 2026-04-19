#!/bin/zsh

set -euo pipefail

VERSION="${1:-0.1.0}"
SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h}"
APP_NAME="PowerGauge"
SOURCE_DIR="${SOURCE_DIR:-${REPO_ROOT}/artifacts/publish/macos-arm64}"
OUTPUT_DIR="${OUTPUT_DIR:-${REPO_ROOT}/artifacts/package/macos/${APP_NAME}.app}"
INFO_PLIST_TEMPLATE="${REPO_ROOT}/src/PowerGauge/Properties/MacOS/Info.plist"
CONTENTS_DIR="${OUTPUT_DIR}/Contents"
MACOS_DIR="${CONTENTS_DIR}/MacOS"
RESOURCES_DIR="${CONTENTS_DIR}/Resources"
EXECUTABLE_PATH="${SOURCE_DIR}/${APP_NAME}"
INFO_PLIST_PATH="${CONTENTS_DIR}/Info.plist"

if [[ ! -d "${SOURCE_DIR}" ]]; then
  print -u2 "Source directory not found: ${SOURCE_DIR}"
  exit 1
fi

if [[ ! -f "${EXECUTABLE_PATH}" ]]; then
  print -u2 "Executable not found at ${EXECUTABLE_PATH}. Build or publish the app first."
  exit 1
fi

if [[ ! -f "${INFO_PLIST_TEMPLATE}" ]]; then
  print -u2 "Info.plist template not found: ${INFO_PLIST_TEMPLATE}"
  exit 1
fi

if ! print -r -- "${VERSION}" | grep -Eq '^[0-9]+(\.[0-9]+){0,3}'; then
  print -u2 "Version '${VERSION}' must start with a numeric version like 0.1.0"
  exit 1
fi

BUILD_VERSION="$(print -r -- "${VERSION}" | sed -E 's/^([0-9]+(\.[0-9]+){0,3}).*/\1/')"

rm -rf "${OUTPUT_DIR}"
mkdir -p "${MACOS_DIR}" "${RESOURCES_DIR}"

print "Copying app files from ${SOURCE_DIR}..."
rsync -a --exclude '*.pdb' "${SOURCE_DIR}/" "${MACOS_DIR}/"

perl -0pe "s/__VERSION__/${VERSION}/g; s/__BUILD_VERSION__/${BUILD_VERSION}/g" "${INFO_PLIST_TEMPLATE}" > "${INFO_PLIST_PATH}"

chmod +x "${MACOS_DIR}/${APP_NAME}"

print "Created macOS app bundle at ${OUTPUT_DIR}"
