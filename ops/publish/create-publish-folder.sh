#!/usr/bin/env bash
set -euo pipefail

# SECTION: Environment validation
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet CLI is required to publish the application." >&2
  exit 1
fi

# SECTION: Restore dependencies
if [ ! -d "node_modules" ]; then
  echo "Restoring Node.js dependencies to ensure bundled assets are present..."
  npm install --ignore-scripts
fi

# SECTION: Publish output
PUBLISH_ROOT="$(pwd)/publish"
echo "Creating publish output under ${PUBLISH_ROOT}..."
dotnet publish ProjectManagement.csproj \
  --configuration Release \
  --output "${PUBLISH_ROOT}" \
  /p:UseAppHost=false

echo "Publish folder created at ${PUBLISH_ROOT}."
