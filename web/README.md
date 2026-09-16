# PMT Web

Next.js 16 React client for PMT. See the repository [README](../README.md) and [docs/setup.md](../docs/setup.md) for the monorepo quick start.

## Local development

```bash
npm ci
export NEXT_PUBLIC_API_URL='http://localhost:5133/api/v1'
export NEXT_PUBLIC_HUB_URL='http://localhost:5133/hubs/notifications'
npm run dev
```

`npm ci` runs the icon bundling postinstall hook. Generated icon files are ignored and recreated during installation.

## Validation

```bash
npm run lint
npm run build
```

The client keeps endpoint configuration in `NEXT_PUBLIC_*` environment variables and keeps credentials out of source control.
