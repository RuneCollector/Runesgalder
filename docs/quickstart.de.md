# Schnellstart

Diese Anleitung baut das Repository, führt die Demos aus, startet das lokale Sandbox-Image und verbindet einen MCP-Client. Auf einem Rechner mit funktionierendem .NET SDK und Docker dauert das etwa zehn Minuten.

> Runesgalder ist Pre-Alpha. Die Sandbox ist eine lokale Container-Grenze für einen einzelnen Mandanten, kein gehärteter Mehrbenutzerdienst. Führe keinen Code aus, den du nicht auch auf deinem eigenen Rechner ausführen würdest.

## Voraussetzungen

- .NET SDK 10.0.401 oder neuer im 10.0-Feature-Band (`dotnet --version`).
- Git.
- Docker mit Linux-Containern. Unter Windows Docker Desktop mit WSL2-Backend; unter macOS und Linux genügt Docker Desktop oder Docker Engine.
- Für MCP-Clients: Claude Code, Cursor oder ein beliebiger Client, der einen stdio-Server starten kann.

Prüfe Docker in derselben Shell, die du unten verwendest:

```bash
docker version --format '{{.Server.Os}}'
```

Die Ausgabe muss `linux` sein. Bei `windows` schalte Docker Desktop auf Linux-Container um.

## 1. Bauen und testen

```bash
git clone https://github.com/Runesgalder/Runesgalder.git
cd Runesgalder
dotnet build Runesgalder.sln -c Release
dotnet test Runesgalder.sln -c Release
```

Tests, die Docker und das Worker-Image benötigen, werden auf einem Host ohne beides übersprungen. Auf Linux mit gebautem Image laufen sie tatsächlich.

## 2. CLI-Demos ausführen

```bash
dotnet run --project src/Runesgalder.Cli -c Release -- demo
dotnet run --project src/Runesgalder.Cli -c Release -- doctor
```

`demo` führt einen typisierten Flow mit drei Schritten aus, gibt das Journal aus und zeigt die Standard-Sandbox-Richtlinie (Netzwerk verweigert, Speicher und Laufzeit begrenzt). `doctor` gibt Laufzeit, Plattform, Container-Erkennung und die Fähigkeit des Hosts aus, die Linux-Sandbox auszuführen.

`resume` zeigt die Wiederverwendung eines Checkpoints:

```bash
dotnet run --project src/Runesgalder.Cli -c Release -- resume
```

## 3. Sandbox-Image mit einem Befehl starten

Im Repository-Stamm:

```bash
bash scripts/sandbox-up.sh
```

Unter Windows PowerShell:

```powershell
.\scripts\sandbox-up.ps1
```

Die Skripte veröffentlichen den Worker, bauen das Image `runesgalder-worker:dev` und prüfen, ob das Tag existiert. Der Container läuft ohne Netzwerk, mit schreibgeschütztem Root-Dateisystem, ohne Linux-Capabilities und ohne neue Privilegien.

Manuell:

```bash
dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker
docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker
```

## 4. MCP-Server bauen und verbinden

```bash
dotnet publish src/Runesgalder.Mcp/Runesgalder.Mcp.csproj -c Release -o artifacts/mcp
```

Füge den Server mit absolutem Pfad zur Client-Konfiguration hinzu:

```json
{
  "mcpServers": {
    "runesgalder": {
      "command": "/absolute/path/to/artifacts/mcp/runesgalder-mcp"
    }
  }
}
```

Unter Windows lautet der Befehl `runesgalder-mcp.exe`. In Claude Code:

```bash
claude mcp add runesgalder -- /absolute/path/to/artifacts/mcp/runesgalder-mcp
```

Optionale Umgebungsvariablen:

- `RUNESGALDER_MCP_WORKER_IMAGE` — Worker-Image, Standard `runesgalder-worker:dev`.
- `RUNESGALDER_MCP_MAX_WALL_CLOCK_SECONDS` — Obergrenze des Betreibers, Standard und Maximum 120.

## 5. Ein Tool aufrufen

Lass den Client `compile_csharp` mit einer Klasse aufrufen, die den Analysevertrag nicht implementiert:

```json
{"code": "public sealed class Analysis { }"}
```

Das Ergebnis ist ein Tool-Fehler mit dem Code `contract_invalid`. Das belegt Handshake, Werkzeugliste und Kompilierpfad. Ein gültiges Skript implementiert `Runesgalder.Sandbox.IAnalysis`:

```csharp
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        context.Log("rows: " + table.Rows.Count);
        return Task.FromResult(new AnalysisResult("ok", []));
    }
}
```

`run_csharp` nimmt denselben `code` und inline-CSV als `data`. `get_run` fragt einen Lauf über `run_id` ab. `cancel_run` ist kooperativ: Es liefert den aktuellen Umschlag zurück, der noch `running` sein kann; frage `get_run` bis zum Endzustand ab.

## Fehlersuche

**`doctor` meldet, der Sandbox-Host sei nicht Linux.** Der Container-Pfad braucht einen Linux-Docker-Daemon. Aktiviere unter Windows das WSL2-Backend in Docker Desktop und führe die Befehle in WSL2 oder PowerShell aus.

**Docker-Tests werden übersprungen.** Docker läuft nicht, liefert Windows-Container oder das Image `runesgalder-worker:dev` fehlt. Führe `scripts/sandbox-up.sh` aus und versuche es erneut.

**`sandbox_unavailable` von `run_csharp`.** Der Server konnte den Container nicht starten. Prüfe `docker image inspect runesgalder-worker:dev`, ob der startende Prozess den Docker-Daemon erreicht und ob der Image-Name zu `RUNESGALDER_MCP_WORKER_IMAGE` passt.

**Der Server beendet sich sofort.** Starte `dotnet artifacts/mcp/runesgalder-mcp.dll` direkt im Terminal. Protokolle gehen nach stderr, stdout ist für Protokollrahmen reserviert. Ein fehlendes Image verhindert den Start nicht, aber die Ausführung.

**Claude Code meldet ein nicht verfügbares Modell.** Das ist die Modellkonfiguration des Clients, nicht der Server. Wähle ein verfügbares Modell und versuche es erneut.

## Noch nicht fertig

- Linux-Daemon, Remote-Transport und stärkere Isolationsstufen fehlen.
- Native Windows-Isolation ist verschoben; nutze WSL2 oder Docker.
- v0.1 überträgt keinen prozessübergreifenden Trace-Kontext über stdio.
- Es sind keine NuGet-Pakete veröffentlicht. Baue aus dem Quellcode.

Umfang im [Projektplan](PROJECT_PLAN.md), Restrisiko im [Bedrohungsmodell](../security/threat-model.md). Sicherheitsmeldungen nur an `3070687583@qq.com`, siehe [SECURITY.md](../SECURITY.md).

Andere Sprachen: [English](quickstart.md) · [中文](quickstart.zh-CN.md)