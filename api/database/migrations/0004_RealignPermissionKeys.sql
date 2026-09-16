/*
    0004_RealignPermissionKeys.sql

    Fixes 403 responses on every [Authorize(Policy=...)] endpoint.

    The API builds its authorization policies from PMT.Application.Common.Security.PermissionRequirement
    as plain claim checks: RequireClaim("permission", "projects.view"), etc. Login copies whatever
    SP_USER @Action='PERMISSIONS' returns (Permission.Name) straight into the token's "permission" claims.

    The rows in dbo.Permission used a different vocabulary ('Project.Create', 'Task.Edit', ...) that
    overlaps the policy keys in zero places, so every issued token failed every policy -> 403, even for
    Administrator. This script realigns dbo.Permission.Name to the 17 canonical keys the code requires
    and re-grants them per role.

    Targets the live PascalCase schema (dbo.Permission / dbo.RolePermission / dbo.Role), which is what
    0003_StoredProcedures.sql expects. NOTE: 0001 and 0002 build a different, snake_case schema and were
    never applied to UDL_PMT; do not run this after them without reconciling that split first.

    Idempotent and re-runnable.
*/

/* QUOTED_IDENTIFIER must be ON for the writes below; sqlcmd defaults it OFF
   (SSMS defaults it ON). Set as its own batch so it applies at parse time. */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------------------------------------------------------------------------
   1. The canonical keys, mirroring PermissionRequirement.All exactly.
--------------------------------------------------------------------------- */
DECLARE @CANONICAL TABLE ([NAME] VARCHAR(100) PRIMARY KEY);
INSERT INTO @CANONICAL ([NAME]) VALUES
    ('departments.view'), ('departments.manage'),
    ('users.view'),       ('users.manage'),
    ('projects.view'),    ('projects.manage'),
    ('stories.view'),     ('stories.manage'),
    ('tasks.view'),       ('tasks.manage'),
    ('issues.view'),      ('issues.manage'),
    ('comments.manage'),
    ('attachments.manage'),
    ('git.manage'),
    ('notifications.manage'),
    ('reports.view');

/* ---------------------------------------------------------------------------
   2. Add any canonical permission that is missing; revive it if it was
      previously soft-deleted.
--------------------------------------------------------------------------- */
INSERT INTO dbo.Permission ([NAME], Active, IsDeleted, InsertDate)
SELECT C.[NAME], 1, 0, GETDATE()
FROM @CANONICAL C
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permission P WHERE P.[NAME] = C.[NAME]);

UPDATE P
SET P.IsDeleted = 0, P.Active = 1, P.UpdateDate = GETDATE()
FROM dbo.Permission P
INNER JOIN @CANONICAL C ON C.[NAME] = P.[NAME]
WHERE P.IsDeleted = 1 OR P.Active = 0;

/* ---------------------------------------------------------------------------
   3. Retire the legacy vocabulary ('Project.Create', 'User.Manage', ...).
      Soft-deleted rather than hard-deleted so existing RolePermission rows keep
      their FK targets and the change stays reversible. SP_USER filters on
      IsDeleted = 0 / Active = 1, so retired rows stop reaching the token.
--------------------------------------------------------------------------- */
UPDATE RP
SET RP.IsDeleted = 1, RP.Active = 0, RP.UpdateDate = GETDATE()
FROM dbo.RolePermission RP
INNER JOIN dbo.Permission P ON P.Id = RP.PermissionId
WHERE P.[NAME] NOT IN (SELECT [NAME] FROM @CANONICAL)
  AND RP.IsDeleted = 0;

UPDATE P
SET P.IsDeleted = 1, P.Active = 0, P.DeletedDate = GETDATE(), P.UpdateDate = GETDATE()
FROM dbo.Permission P
WHERE P.[NAME] NOT IN (SELECT [NAME] FROM @CANONICAL)
  AND P.IsDeleted = 0;

/* ---------------------------------------------------------------------------
   4. Role -> permission grants.

      Administrator gets all 17. The other three mirror the intent of the
      0002 seed, remapped onto this database's role names:
        ProjectLead ~ 'Team Lead'  -> everything except departments.manage / users.manage
        Developer                  -> the delivery subset
        Viewer                     -> read-only
      Adjust the three non-admin roles to taste; Administrator is the part that
      unblocks the 403.
--------------------------------------------------------------------------- */
DECLARE @GRANTS TABLE (ROLE_NAME VARCHAR(100), PERMISSION_NAME VARCHAR(100));

INSERT INTO @GRANTS (ROLE_NAME, PERMISSION_NAME)
SELECT 'Administrator', [NAME] FROM @CANONICAL
UNION ALL
SELECT 'ProjectLead', [NAME] FROM @CANONICAL
WHERE [NAME] NOT IN ('departments.manage', 'users.manage')
UNION ALL
SELECT 'Developer', [NAME] FROM @CANONICAL
WHERE [NAME] IN ('projects.view', 'stories.view', 'stories.manage', 'tasks.view', 'tasks.manage',
                 'issues.view', 'issues.manage', 'comments.manage', 'attachments.manage',
                 'git.manage', 'notifications.manage', 'reports.view')
UNION ALL
SELECT 'Viewer', [NAME] FROM @CANONICAL
WHERE [NAME] IN ('departments.view', 'users.view', 'projects.view', 'stories.view',
                 'tasks.view', 'issues.view', 'reports.view');

/* Insert grants that do not exist at all. */
INSERT INTO dbo.RolePermission (RoleId, PermissionId, Active, IsDeleted, InsertDate)
SELECT R.Id, P.Id, 1, 0, GETDATE()
FROM @GRANTS G
INNER JOIN dbo.Role R       ON R.[NAME] = G.ROLE_NAME       AND R.IsDeleted = 0
INNER JOIN dbo.Permission P ON P.[NAME] = G.PERMISSION_NAME AND P.IsDeleted = 0
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermission RP
    WHERE RP.RoleId = R.Id AND RP.PermissionId = P.Id);

/* Re-activate grants that exist but were soft-deleted or deactivated. */
UPDATE RP
SET RP.IsDeleted = 0, RP.Active = 1, RP.UpdateDate = GETDATE()
FROM dbo.RolePermission RP
INNER JOIN dbo.Role R       ON R.Id = RP.RoleId
INNER JOIN dbo.Permission P ON P.Id = RP.PermissionId
INNER JOIN @GRANTS G ON G.ROLE_NAME = R.[NAME] AND G.PERMISSION_NAME = P.[NAME]
WHERE RP.IsDeleted = 1 OR RP.Active = 0;

COMMIT;
GO

/* ---------------------------------------------------------------------------
   Verification: every row below should be a canonical key, and Administrator
   should return 17.
--------------------------------------------------------------------------- */
SELECT R.[NAME] AS RoleName, COUNT(*) AS GrantedPermissions
FROM dbo.RolePermission RP
INNER JOIN dbo.Role R       ON R.Id = RP.RoleId
INNER JOIN dbo.Permission P ON P.Id = RP.PermissionId
WHERE RP.IsDeleted = 0 AND RP.Active = 1 AND P.IsDeleted = 0 AND P.Active = 1
GROUP BY R.[NAME]
ORDER BY R.[NAME];
GO
