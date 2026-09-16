# Internship Positioning

## Purpose

This repository is a portfolio-ready internship project that shows how a full-stack product can be organized, secured, tested, and documented without publishing operational secrets.

## Skills demonstrated

- Clean Architecture and dependency boundaries in .NET
- Dapper repositories and idempotent SQL Server migrations
- JWT authentication, refresh-token rotation, authorization policies, and configuration validation
- Next.js client architecture, API service isolation, session handling, and SignalR
- Optional AI provider abstraction with safe credential handling
- Unit, configuration, and database contract tests
- Public CI, reproducible builds, deployment documentation, and security policy design

## Good internship contributions

- Add focused tests around existing use cases and configuration edge cases.
- Improve accessibility and responsive behavior in the web views.
- Document API request/response contracts in the existing request examples.
- Add safe validation for new configuration or provider settings.
- Improve observability with structured logs and actionable health-check details.
- Refactor duplicated client or repository code without changing authorization behavior.

## Contribution boundaries

Do not bypass authentication, weaken CORS, log credentials, commit runtime state, or add provider keys to configuration files. Keep product behavior changes small and test-backed. Treat database migrations as forward-only deployment contracts and preserve existing permission keys unless a coordinated migration updates both database and application code.
