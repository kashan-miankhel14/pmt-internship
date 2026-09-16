# Deployment

## Configuration

Deploy the API with environment variables or a host secret store. Required persistent settings include:

- `ConnectionStrings__DefaultConnection`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__SigningKey` (unique, random, at least 32 bytes)
- `Cors__AllowedOrigins__0` and any additional exact origins
- `Authentication__AllowHttpMetadata=false` outside Development
- `HttpsRedirection__Enabled=true` in production

Optional provider settings include `Ai__ChatProvider`, `Gemini__ApiKey`, and `Ollama__BaseUrl`. Never commit populated environment files.

The web client needs `NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_HUB_URL`, and optionally `NEXT_PUBLIC_APP_URL`/`BASEPATH`. `NEXT_PUBLIC_*` values are intentionally public endpoint configuration, not credentials.

## Build

```bash
dotnet restore api/PMT.sln
dotnet publish api/src/PMT.Api/PMT.Api.csproj -c Release -o artifacts/api

cd web
npm ci
NEXT_PUBLIC_API_URL='https://api.example.com/api/v1' \
NEXT_PUBLIC_HUB_URL='https://api.example.com/hubs/notifications' \
npm run build
```

Keep `artifacts/`, `.next/`, `node_modules/`, DataProtection keys, and uploads out of source control.

## Runtime

Run the API behind a trusted reverse proxy, apply the canonical migrations before serving traffic, and bootstrap the first administrator through the controlled command. Persist DataProtection keys in a restricted runtime directory; losing them invalidates protected tokens/cookies. Preserve uploads separately during releases.

Use exact CORS origins, HTTPS, least-privilege database/service accounts, restricted file permissions, and health checks. Rotate credentials after any suspected exposure and follow `SECURITY.md` for reporting.
