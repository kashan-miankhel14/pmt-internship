/*
    0005_RepairUserIsDeletedDefault.sql

    Repairs `dotnet run --project src/PMT.Api -- --bootstrap-admin` failing on databases
    whose dbo.[User].IsDeleted column is NOT NULL but carries no usable default constraint.

    0003_StoredProcedures.sql now writes IsDeleted explicitly in both user insert paths
    (SP_USER @Action='INSERT' and SP_ADMIN @Action='BOOTSTRAP'), so a fresh build no longer
    depends on the default. This script is the corrective half: databases deployed before that
    fix may have dbo.[User].IsDeleted with no bound default (a plain INSERT that omits the
    column then fails because the NOT NULL column has nothing to fall back on).

    0001_InitialSchema.sql defines the column as
        IsDeleted BIT NOT NULL CONSTRAINT DF_user_isdeleted DEFAULT((0))
    so on a database built from 0001 this script is a no-op: a default constraint is already
    bound to the column and it is left untouched, whatever its name.

    This script only adds a missing default constraint. It never changes, deactivates or
    deletes any row, and never touches an existing default (including one with a nonstandard
    name). Forward-only, idempotent and safe to re-run.

    Run order: 0001 -> 0002 -> 0003 -> 0004 -> 0005.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------------------------------------------------------------------------
   Add a default of 0 to dbo.[User].IsDeleted only when no default constraint is
   currently bound to that column. The bound constraint is discovered by joining
   sys.default_constraints to sys.columns rather than assuming a name, so a default
   under a nonstandard name is detected and this script does nothing.
--------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.[User]', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.default_constraints DC
       INNER JOIN sys.columns C
           ON C.object_id = DC.parent_object_id
          AND C.column_id = DC.parent_column_id
       WHERE DC.parent_object_id = OBJECT_ID('dbo.[User]', 'U')
         AND C.name = 'IsDeleted')
BEGIN
    ALTER TABLE dbo.[User]
        ADD CONSTRAINT DF_user_isdeleted DEFAULT ((0)) FOR IsDeleted;
END

COMMIT;
GO
