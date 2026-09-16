# Architecture

PMT is a two-tier monorepo with explicit ownership boundaries.

## API

`api/` is a .NET 10 Clean Architecture solution:

- `PMT.Domain`: entities, enums, result types, and domain rules; no infrastructure dependencies.
- `PMT.Application`: use cases, DTOs, validators, authorization contracts, and repository interfaces.
- `PMT.Infrastructure`: Dapper repositories, SQL migrations integration, JWT and password services, caching, SignalR, email, Git providers, and AI clients.
- `PMT.Api`: HTTP composition root, controllers, middleware, Swagger, health checks, CORS, rate limiting, and configuration validation.

The API uses SQL Server stored procedures through Dapper. The canonical fresh-database path is migrations `0009_DeployCompleteSchema.sql`, `0010_CriticalFixes.sql`, and `0011_RestoredBackupCompatibility.sql`. Older migrations remain for historical reference.

Authentication uses JWT access tokens, rotating hashed refresh tokens, Argon2 password hashing, claim-based permission policies, login rate limiting, and explicit CORS origins. Development generates an ephemeral JWT signing key when none is configured; persistent environments must provide `Jwt__SigningKey` out of band.

AI chat is provider-selected through `Ai:ChatProvider`. Ollama is the safe local default for chat and embeddings. Gemini is an optional chat provider and receives its key only through environment/configuration binding; the key is never placed in a URL or tracked settings file.

## Web

`web/` is a Next.js 16 React application. It owns presentation, client-side session storage, API service wrappers, SignalR connectivity, route guards, and feature views. `NEXT_PUBLIC_API_URL` and `NEXT_PUBLIC_HUB_URL` are build/runtime configuration values. The client never receives server-only credentials.

## Runtime boundaries

- Source control contains code, migrations, tests, and non-secret configuration only.
- Environment variables, user secrets, CI secrets, or a host secret store supply credentials.
- DataProtection keys and uploads are writable runtime state outside the repository.
- The API health endpoint is `/health`; the SignalR hub is `/hubs/notifications`.
- Public CI builds both tiers and runs unit tests; SQL contract tests require a Windows/LocalDB runner.
