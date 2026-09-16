/*
    0006_RepairIsDeletedDefaults.sql

    Repairs login failure (and any plain INSERT) on legacy databases whose dbo tables carry a
    NOT NULL IsDeleted column but no bound default constraint. On such databases a plain INSERT
    that omits IsDeleted fails because the column is NOT NULL and has nothing to fall back on,
    which is what broke refresh-token issuance / login on the old LocalDB build.

    The corrective half for the stored procedures lives in 0003_StoredProcedures.sql: every
    dbo.SP_* insert path now writes IsDeleted explicitly (literal 0), so a fresh build no longer
    depends on the default. This script is the schema-repair half: it binds a DEFAULT ((0)) to
    IsDeleted on every eligible dbo user table that is missing one.

    Eligible tables are discovered dynamically from sys.tables / sys.schemas / sys.columns /
    sys.default_constraints rather than hardcoded, so it covers any dbo user table that follows
    the audit-tail convention. A table is eligible when, within schema dbo:
      - it is a user table (sys.tables, not a system table),
      - it has a non-nullable column named IsDeleted (BIT NOT NULL in this schema), and
      - no default constraint is currently bound to that column.

    For each eligible table the script binds a default named DF_<table>_isdeleted. The default
    constraint name is built from the table name so it is stable and idempotent; the NOT EXISTS
    guard on sys.default_constraints guarantees a table that already has a default (whatever its
    name) is left untouched.

    This script only adds missing default constraints. It never changes, deactivates or deletes
    any row, never touches an existing default (including one with a nonstandard name), and never
    modifies tables that already have a usable default on IsDeleted. Forward-only, idempotent and
    safe to re-run. Uses safe dynamic SQL (QUOTENAME for identifiers) inside a single transaction
    with XACT_ABORT so a failure rolls everything back.

    Run order: 0001 -> 0002 -> 0003 -> 0004 -> 0005 -> 0006.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------------------------------------------------------------------------
   Discover every dbo user table that has a non-nullable IsDeleted column with
   no bound default constraint, then bind a DEFAULT ((0)) to each. The eligible
   set is computed at runtime from the catalog views, so no table name is
   hardcoded. Identifiers are quoted with QUOTENAME to be safe, and the per-table
   NOT EXISTS check makes the whole operation idempotent.
--------------------------------------------------------------------------- */
DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql + N'
IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints DC
        INNER JOIN sys.columns C
            ON C.object_id = DC.parent_object_id
           AND C.column_id = DC.parent_column_id
        WHERE DC.parent_object_id = OBJECT_ID(''dbo.' + QUOTENAME(T.name, ']') + ''', ''U'')
          AND C.name = ''IsDeleted'')
    ALTER TABLE dbo.' + QUOTENAME(T.name) + N'
        ADD CONSTRAINT ' + QUOTENAME(N'DF_' + T.name + N'_isdeleted') + N' DEFAULT ((0)) FOR IsDeleted;
'
FROM sys.tables T
INNER JOIN sys.schemas S
    ON S.schema_id = T.schema_id
INNER JOIN sys.columns C
    ON C.object_id = T.object_id
WHERE S.name = 'dbo'
  AND T.is_ms_shipped = 0
  AND C.name = 'IsDeleted'
  AND C.is_nullable = 0;

IF @sql <> N''
    EXEC sp_executesql @sql;

COMMIT;
GO
