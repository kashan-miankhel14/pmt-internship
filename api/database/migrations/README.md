# SQL migrations

For a fresh PMT database, run these scripts in order:

```text
0009_DeployCompleteSchema.sql
0010_CriticalFixes.sql
0011_RestoredBackupCompatibility.sql
```

The `0001`-`0008` files are retained for historical compatibility and are not the fresh-deployment path. Migration scripts are forward-only deployment contracts; add a new numbered script for each schema or reference-data change.

Run migrations with a credential source that does not expose passwords on the command line. See [docs/setup.md](../../docs/setup.md) and [docs/deployment.md](../../docs/deployment.md) for the monorepo paths and operational guidance.

The first administrator is created through the API bootstrap command, which hashes the password with Argon2 before persistence.
