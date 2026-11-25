#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker
docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker
docker image inspect runesgalder-worker:dev > /dev/null

echo "Worker image 'runesgalder-worker:dev' is ready."
echo "Next: dotnet publish src/Runesgalder.Mcp/Runesgalder.Mcp.csproj -c Release -o artifacts/mcp"