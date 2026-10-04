#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DEFAULT_PROJECTS=(
  PenguinTools.slnx
)

if [[ "${#@}" -gt 0 ]]; then
  projects=("$@")
else
  projects=("${DEFAULT_PROJECTS[@]}")
fi

for proj in "${projects[@]}"; do
  if [[ ! -f "$proj" ]]; then
    echo "error: project file not found: $proj" >&2
    exit 1
  fi
  echo "==> dotnet format $proj"
  dotnet format "$proj" --severity warn --exclude External --verbosity minimal
done

echo "format-all: finished (${#projects[@]} project(s))."
