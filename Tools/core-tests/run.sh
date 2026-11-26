#!/usr/bin/env bash
# Compiles the UnityEngine-free core with Mono and runs the tests.
set -euo pipefail
cd "$(dirname "$0")/../.."
out="$(mktemp -d)"
mcs -langversion:latest -out:"$out/core-tests.exe" Assets/Mammoth/Runtime/Core/*.cs Tools/core-tests/CoreTests.cs
mono "$out/core-tests.exe"
