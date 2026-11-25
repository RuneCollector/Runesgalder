param()

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker
docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker
docker image inspect runesgalder-worker:dev | Out-Null

Write-Host "Worker image 'runesgalder-worker:dev' is ready."
Write-Host "Next: dotnet publish src/Runesgalder.Mcp/Runesgalder.Mcp.csproj -c Release -o artifacts/mcp"