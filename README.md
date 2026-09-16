# PMT Internship Monorepo

Public internship reference implementation for the Project Management Tool (PMT). It contains a .NET Clean Architecture API and a Next.js web client in one repository.

## Repository layout

```text
.
├── api/                 # .NET 10 API, Dapper/SQL Server, auth, AI integrations
│   ├── src/
│   ├── tests/
│   ├── database/
│   └── requests/
├── web/                 # Next.js 16 React client
│   ├── src/
│   └── public/
├── docs/                # architecture, setup, testing, deployment, internship guide
├── .env.example         # non-secret environment variable template
└── .github/workflows/   # public CI checks
```

## Quick start

Prerequisites:

- .NET 10 SDK
- Node.js 20 or newer and npm
- SQL Server (LocalDB is used by the SQL contract tests)
- Ollama for local AI features; Gemini is optional and requires an out-of-band API key

From the repository root:

```bash
dotnet restore api/PMT.sln
dotnet build api/PMT.sln -c Release
```

Configure the API through environment variables or `dotnet user-secrets`; tracked `appsettings*.json` files contain no connection strings, JWT signing keys, or provider API keys. Copy `.env.example` to a local `.env` for reference, then export the values in the shell or configure your host.

```bash
cd api
dotnet run --project src/PMT.Api
```

In another shell:

```bash
cd web
npm ci
NEXT_PUBLIC_API_URL=http://localhost:5133/api/v1 \
NEXT_PUBLIC_HUB_URL=http://localhost:5133/hubs/notifications \
npm run dev
```

Run the canonical database migrations `0009`, `0010`, and `0011` before first use, then bootstrap one administrator as described in [docs/setup.md](docs/setup.md).

## Documentation

- [Architecture](docs/architecture.md)
- [Setup](docs/setup.md)
- [Testing](docs/testing.md)
- [Deployment](docs/deployment.md)
- [Internship positioning](docs/internship.md)
- [Security policy](SECURITY.md)

## Internship positioning

This repository is intended to demonstrate production-minded engineering habits: layered ownership, explicit configuration boundaries, secure authentication defaults, database migration discipline, test coverage, observable failures, and a reproducible public build. Suggested internship contributions include adding focused unit tests, improving accessibility, documenting API contracts, hardening configuration validation, and extending the existing audit and AI boundaries without bypassing authorization.

Do not commit credentials, local database backups, DataProtection key rings, build output, or environment files. Rotate any credential that is accidentally exposed and report it through the process in `SECURITY.md`.
