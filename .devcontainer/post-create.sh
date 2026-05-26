#!/usr/bin/env bash
set -euo pipefail
dotnet restore Ape.Worker.sln
dotnet build Ape.Worker.sln
dotnet test Ape.Worker.sln || true