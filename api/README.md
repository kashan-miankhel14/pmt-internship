# PMT Backend

The backend is a .NET 10 Clean Architecture API using Dapper and SQL Server. See the repository [README](../README.md) for the monorepo quick start and [docs/setup.md](../docs/setup.md) for configuration, migrations, and bootstrap instructions.

## Projects

- `src/PMT.Domain`: domain entities and rules
- `src/PMT.Application`: use cases, DTOs, validators, and contracts
- `src/PMT.Infrastructure`: Dapper, security, caching, SignalR, email, Git, and AI adapters
- `src/PMT.Api`: HTTP composition root, controllers, middleware, Swagger, and health checks

Tracked `appsettings*.json` files contain no connection strings, JWT signing keys, or provider API keys. Supply persistent secrets through environment variables, `dotnet user-secrets`, CI secrets, or a host secret store. Development generates an ephemeral JWT signing key when none is configured.

Useful local references are in `requests/PMT.Api.http`. Database migration guidance is in `database/migrations/README.md`.
