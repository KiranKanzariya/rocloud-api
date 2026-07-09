#!/usr/bin/env bash
# CI build + security gate (guide §16). Fails on warnings, test failures, or any
# High/Critical vulnerable package (direct or transitive).
set -euo pipefail

SLN="ROCloud.sln"

echo "==> Build (warnings as errors)"
dotnet build "$SLN" -warnaserror --nologo

echo "==> Tests"
dotnet test "$SLN" --nologo

echo "==> Dependency vulnerability scan"
SCAN="$(dotnet list "$SLN" package --vulnerable --include-transitive 2>&1)"
echo "$SCAN"

if echo "$SCAN" | grep -Eiq '\b(High|Critical)\b'; then
  echo "::error:: High/Critical vulnerable package(s) found — failing build."
  exit 1
fi

echo "==> OK: no High/Critical vulnerabilities."
