# Autonomous AI Incident Triage and Runbook Intelligence Platform

AegisOps is a compact ASP.NET Core incident-triage service for backend and platform teams. It ingests incidents, scores severity, finds similar failures and matching runbooks, and produces a concise AI-assisted triage brief.

## Highlights

- ASP.NET Core minimal API with C#
- SQLite persistence for incidents and triage runs
- Retrieval over prior incidents and runbooks
- Optional Azure Blob Storage export for triage briefs and incident snapshots
- Optional OpenAI Responses API summarization using `OPENAI_API_KEY`
- Deterministic fallback logic so the app still works offline

## Project structure

```text
AegisOps.Api/
  Program.cs
  AegisOps.Api.csproj
  appsettings.json
  Models/
  Services/
  Data/runbooks.json
```

## Quick start

```bash
dotnet restore
dotnet run --project AegisOps.Api
```

Optional environment variables:

```bash
export OPENAI_API_KEY=sk-...
export OPENAI_MODEL=gpt-4.1-mini
export Archive__Provider=azure-blob
export Archive__AzureBlobConnectionString="UseDevelopmentStorage=true"
export Archive__AzureBlobContainer=aegisops-triage-archives
export Archive__AzureBlobPrefix=triage-exports
```

## API endpoints

- `GET /health`
- `GET /api/incidents`
- `GET /api/incidents/{id}`
- `GET /api/runbooks`
- `POST /api/incidents`
- `POST /api/triage`

When `Archive__Provider=azure-blob` is configured, `/api/triage` also writes the assessment payload to Azure Blob Storage and returns the archive location in the response.

## Example request

```bash
curl -X POST http://localhost:5099/api/triage   -H "Content-Type: application/json"   -d '{
    "title": "Inference service latency spike",
    "description": "p99 latency climbed from 280ms to 1.4s after deploy.",
    "logs": [
      "cache miss ratio rose to 43%",
      "gpu-utilization fell to 48%",
      "request queue depth exceeded 200"
    ],
    "service": "inference-gateway",
    "environment": "prod"
  }'
```

## What recruiters will notice

This project looks good for backend and AI roles because it combines:

- API design in C# / .NET
- cloud-oriented export workflows with Azure Blob Storage
- applied AI for summarization and triage
- retrieval over historical operational data
- persistence and auditability
- practical incident-management workflows
