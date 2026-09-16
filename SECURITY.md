# Security Policy

## Reporting

Report a suspected vulnerability privately to the repository maintainer through GitHub Security advisories or the project's private contact channel. Do not open a public issue containing credentials, tokens, personal data, or reproduction details that could expose users.

Include the affected component, a concise impact description, reproducible steps that use non-production data, and the earliest affected version when known. Do not include live secrets in the report.

## Supported scope

The maintained security boundary includes:

- JWT issuance and validation, refresh-token rotation, password hashing, and authorization policies
- CORS, forwarded headers, rate limiting, request-size limits, and security response headers
- SQL migration and repository behavior
- Upload handling and DataProtection key persistence
- AI provider configuration and credential handling
- Dependency and build configuration

## Configuration rules

- Never commit `.env`, `appsettings.Local.json`, `App_Data/Keys`, database backups, provider keys, connection strings, JWT signing keys, or SMTP credentials.
- `Jwt__SigningKey` must be a unique random value of at least 32 bytes in every persistent environment. Development can generate an ephemeral key when the setting is absent; tokens then become invalid after a restart.
- `Gemini__ApiKey` is optional and must be supplied through the host environment or a secret store. The API sends it only in the Gemini request header and returns a controlled `503` when it is absent.
- Production requires explicit CORS origins, HTTPS, a real JWT signing key, and a real database connection string.
- DataProtection keys and uploads are runtime state and must be stored outside source control with restricted permissions.

## Response expectations

A report should receive an acknowledgment and triage decision. Maintainers will prioritize credential exposure, authentication bypass, authorization failures, unsafe file handling, and remotely exploitable crashes. After a fix, rotate affected credentials and remove secret-bearing artifacts from history where appropriate.
