# Shadowrun Rulebook AI

Shadowrun Rulebook AI lets you upload a Shadowrun 5E rulebook PDF (or use a server-side default) and chat with an in-universe expert—GEIST-5—powered by Google Gemini 2.5. The backend is a C# Azure Functions (isolated) HTTP API that handles PDF ingestion, caching, and chat streaming; the frontend is a Vite-powered single-page app.

## Project Structure

```
.
├── ShadowrunAi.Core/        # .NET class library: AI, storage, and data services
├── ShadowrunAi.Functions/   # Azure Functions (HTTP) backend
├── frontend/                # Vite frontend (vanilla HTML/CSS/JS)
├── ShadowrunAi.sln          # .NET solution
├── start.(sh|ps1|bat)       # Legacy Node scripts (no longer used for backend)
└── README.md
```

## Prerequisites

- .NET SDK 9.0+
- Azure Functions Core Tools v4
- Node.js 18+ and npm 9+ (frontend)
- Azurite (for local Azure Storage emulator) or an Azure Storage account
- Optional: Azure Cosmos DB Emulator or an Azure Cosmos account
- Google Gemini API access and key

## Configuration

Local configuration is managed via `ShadowrunAi.Functions/local.settings.json` (not used in production). Key sections:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",

    "OpenId:Authority": "https://your-idp",
    "OpenId:ClientId": "shadowrunai-backend",
    "OpenId:ClientSecret": "<secret>",
    "OpenId:Audience": "api",

    "Storage:ConnectionString": "<connection-string>",
    "Storage:ServiceUri": "https://<account>.blob.core.windows.net",
    "Storage:ContainerName": "session-files",
    "Storage:DefaultRulebookBlobName": "defaults/SR5e.pdf",

    "Cosmos:ConnectionString": "AccountEndpoint=...;AccountKey=...;",
    "Cosmos:AccountEndpoint": "https://<cosmos-account>.documents.azure.com:443/",
    "Cosmos:DatabaseId": "ShadowrunAi",
    "Cosmos:ContainerId": "Sessions",

    "Gemini:ApiKey": "<google-gemini-api-key>",
    "Gemini:Model": "gemini-2.5-flash-lite",
    "Gemini:BaseUri": "https://generativelanguage.googleapis.com",
    "Gemini:UseCaching": "true",
    "Gemini:RetryCount": "3",
    "Gemini:CacheTtl": "01:00:00",
    "Gemini:SystemInstruction": "",
    "Gemini:DefaultSystemInstructionPath": "systemInstruction.txt",
    "Gemini:HistoryTurnLimit": "50",
    "Gemini:FileProcessingTimeoutSeconds": "60",
    "Gemini:FileProcessingPollSeconds": "2"
  },
  "Host": {
    "LocalHttpPort": 5149,
    "LocalHttpsPort": 7149,
    "CORS": "*",
    "CorsSupportCredentials": true
  },
  "ASPNETCORE_URLS": "https://localhost:7149;http://localhost:5149"
}
```

Notes:
- Storage can be configured with either `Storage:ConnectionString` or `Storage:ServiceUri` (the latter uses DefaultAzureCredential).
- Cosmos can be configured with `Cosmos:ConnectionString` or `Cosmos:AccountEndpoint` (with DefaultAzureCredential).
- `Storage:DefaultRulebookBlobName` is used by the default session endpoint.
- `Gemini:DefaultSystemInstructionPath` defaults to `ShadowrunAi.Functions/systemInstruction.txt`.
- Do not commit real secrets. Use user-secrets or environment variables in production.

## Install and Run (Local)

1) Backend (Azure Functions)

```
cd ShadowrunAi.Functions
func start
```

This launches the functions host at `http://localhost:5149` (HTTP) and `https://localhost:7149` (HTTPS) by default.

2) Frontend (Vite)

```
cd frontend
npm install
npm run dev
```

Open the app at `http://localhost:5173` (default Vite port). Ensure the frontend is configured to call the backend base URL (e.g., `http://localhost:5149/api`).

## Authentication

JWT bearer auth is configured via OpenID Connect settings in `local.settings.json`. Most endpoints require a valid access token. Update:

- `OpenId:Authority`, `OpenId:ClientId`, `OpenId:Audience`, and `OpenId:ClientSecret`

Some endpoints (e.g., `upload-pdf`) are anonymous to allow bootstrapping; others use `[Authorize]` and will return 401/403 without a token.

## HTTP API (Azure Functions)

All routes below are prefixed with `/api` by the Functions host.

- POST `/api/upload-pdf` (multipart/form-data)
  - fields: `file` (PDF), optional `sessionId` (GUID), optional `systemInstruction` (string)
  - response: `{ success, sessionId, providerId, cacheName, fileUri }`
  - auth: anonymous

- POST `/api/sessions/start-with-default`
  - creates a user-scoped session referencing the default rulebook blob
  - auth: bearer token required

- GET `/api/sessions`
  - lists sessions for the current account; optional `?current=<guid>`
  - auth: bearer token required

- GET `/api/sessions/{id}/info`
  - returns session metadata (file/cache info, counts, etc.)
  - auth: bearer token required

- DELETE `/api/sessions/{id}`
  - deletes a session
  - auth: bearer token required

- GET `/api/chat-history/{id}`
  - returns `{ history: [{ user, ai, timestamp }, ...] }`
  - auth: bearer token required

- POST `/api/chat`
  - body: `{ sessionId: <guid>, message: <string> }`
  - response: `{ response, sessionId, cacheId }`
  - auth: bearer token required

- POST `/api/chat/stream`
  - body: `{ sessionId: <guid>, message: <string> }`
  - response: Server-Sent Events stream of `{ type: "chunk" | "done", ... }`
  - auth: bearer token required

- POST `/api/chat/rerun/stream`
  - body: `{ sessionId: <guid>, turnIndex?: <number>, userMessage?: <string> }`
  - response: SSE stream similar to `/chat/stream`
  - auth: bearer token required

- DELETE `/api/chat-message`
  - body: `{ sessionId: <guid>, turnIndex: <number>, role: "user" | "ai" }`
  - removes a specific message; deletes the turn if empty
  - auth: bearer token required

## Usage Flow

1. Start a session by uploading a PDF via `/api/upload-pdf` or by calling `/api/sessions/start-with-default`.
2. Send prompts with `/api/chat` or stream with `/api/chat/stream`.
3. Manage sessions and history with the session endpoints.

## Frontend Build

For a production build of the frontend:

```
cd frontend
npm run build
```

Serve the contents of `frontend/dist/` behind your preferred host or proxy.

## Deployment Notes

- The backend is an Azure Functions app (isolated). Use your CI/CD or `func azure functionapp publish`.
- Configure `APPLICATIONINSIGHTS_CONNECTION_STRING` for telemetry.
- Ensure the Storage container exists and is writable; the Cosmos container will be initialized on startup.
- Configure CORS on the Function App as appropriate.

## Contributing

Issues and pull requests are welcome.

## License

MIT
