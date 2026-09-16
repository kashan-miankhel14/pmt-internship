# Testing

## .NET

```bash
dotnet restore api/PMT.sln
dotnet build api/PMT.sln -c Release
dotnet test api/tests/PMT.UnitTests/PMT.UnitTests.csproj -c Release --no-build
```

The integration project contains SQL contract tests that create a throw-away LocalDB database and run migrations `0009` through `0011`. Run them on a Windows host with LocalDB available:

```bash
dotnet test api/tests/PMT.IntegrationTests/PMT.IntegrationTests.csproj -c Release --no-build
```

API controller integration tests are explicitly skipped until `PMT_TEST_CONNECTION_STRING` points at an isolated SQL Server database. Do not point them at a shared or production database.

## Web

```bash
cd web
npm ci
npm run lint
npm run build
```

`npm ci` runs the icon bundling postinstall hook. The generated icon files are intentionally ignored and regenerated during installation/build.

## CI

`.github/workflows/ci.yml` runs:

- backend restore, Release build, and unit tests on Ubuntu
- SQL contract tests on Windows with LocalDB
- frontend dependency install, lint, and production build

CI does not receive application secrets. Configuration-validation tests exercise secure and insecure JWT/CORS combinations without using real credentials.
