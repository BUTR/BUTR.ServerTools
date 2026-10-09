#!/usr/bin/env bash
# changelog.sh <ModuleId>:
# Extracts the module's latest version and changelog description from src/<ModuleId>/changelog.txt,
# and checks remote tags to determine if the version is already released (<ModuleId>-v<version>).
set -euo pipefail

module="$1"
changelog="$PWD/src/$module/changelog.txt"

version="$(bannerlord_changelog_parser latestversion -f "$changelog")"
description="$(bannerlord_changelog_parser fulldescription -f "$changelog")"

if git ls-remote --exit-code --tags origin "refs/tags/$module-v$version" > /dev/null; then
  released=true
  echo "$module v$version is released already, skipping it"
else
  released=false
  echo "$module v$version will be released"
fi

{
  echo "version=$version"
  echo "released=$released"
  echo "description<<CHANGELOG_EOF"
  echo "$description"
  echo "CHANGELOG_EOF"
} >> "$GITHUB_OUTPUT"
