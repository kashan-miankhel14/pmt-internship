# Setup

## Prerequisites

- .NET 10 SDK matching `api/global.json`
- Node.js 20 or newer with npm
- SQL Server for the API and migrations
- Ollama for local AI chat/embeddings; Gemini is optional

## Clone and restore

```bash
git clone https://github.com/kashan-miankhel14/pmt-internship.git
cd pmt-internship
dotnet restore api/PMT.sln
```

## Configure the API

Tracked `api/src/PMT.Api/appsettings*.json` files intentionally omit secrets. Use environment variables, `dotnet user-secrets`, or a host secret store. `.env.example` documents the names but is not automatically loaded by ASP.NET Core.

Minimum local values:

```bash
export ConnectionStrings__DefaultConnection='Server=localhost;Database=PMT;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True'
export Jwt__Issuer='PMT.Api'
export Jwt__Audience='PMT.Web'
export Jwt__SigningKey='generate-a-unique-random-value-of-at-least-32-bytes'
export Cors__AllowedOrigins__0='http://localhost:3000'
export Authentication__AllowHttpMetadata='true'
```

For a persistent development token key, set `Jwt__SigningKey`. If it is absent in Development, the API creates an ephemeral key at startup; restarting invalidates existing tokens.

Optional Gemini configuration:

```bash
export Ai__ChatProvider='Gemini'
export Gemini__ApiKey='supplied-out-of-band'
```

Never place the Gemini key in `appsettings*.json`, a URL query string, logs, or source control.

## Database

Create the configured database and run the canonical migration chain in order:

```text
api/database/migrations/0009_DeployCompleteSchema.sql
api/database/migrations/0010_CriticalFixes.sql
api/database/migrations/0011_RestoredBackupCompatibility.sql
```

Do not run the historical `0001`-`0008` chain for a fresh deployment. Keep database credentials out of shell history and command lines.

Bootstrap the first administrator once:

```bash
export BootstrapAdmin__Email='admin@example.com'
export BootstrapAdmin__DisplayName='System Administrator'
export BootstrapAdmin__Password='use-a-strong-password'
dotnet run --project api/src/PMT.Api -- --bootstrap-admin
```

The bootstrap command refuses to create an administrator after an active user exists.

## Run

API:

```bash
dotnet run --project api/src/PMT.Api
```

Web, from a second shell:

```bash
cd web
npm ci
export NEXT_PUBLIC_API_URL='http://localhost:5133/api/v1'
export NEXT_PUBLIC_HUB_URL='http://localhost:5133/hubs/notifications'
npm run dev
```

The API exposes Swagger at `https://localhost:7133/swagger` when using the HTTPS launch profile, health at `/health`, and the SignalR hub at `/hubs/notifications`.
