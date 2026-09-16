/*
    0002_SeedRolesPermissions.sql

    Seeds the department, roles, permissions and role->permission grants a fresh database
    needs before anyone can log in.

    HISTORY: an earlier version of this file seeded the snake_case schema that the old 0001
    created ([ROLE_PERMISSION], [PERMISSION].[KEY], eight roles) and was never deployed. It
    has been rewritten against the real PascalCase schema and the four roles that actually
    exist.

    The permission strings below are the contract with
    PMT.Application.Common.Security.PermissionRequirement. SP_USER @Action='PERMISSIONS'
    returns Permission.Name, AuthService copies it into the JWT's "permission" claims, and the
    policies registered in ServiceCollectionExtensions match on it with RequireClaim. If these
    drift from PermissionRequirement.All, every [Authorize(Policy=...)] endpoint returns 403.
    Keep the two lists in step.

    Idempotent and re-runnable. Creates no users: bootstrap the first administrator through
    the API (dotnet run --bootstrap-admin) so the password is Argon2-hashed.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------------------------------------------------------------------------
   Department. User.DepartmentId is NOT NULL, so at least one row must exist
   before any user can be created.
--------------------------------------------------------------------------- */
INSERT INTO dbo.Department (Name, Active, IsDeleted, InsertDate)
SELECT 'IT', 1, 0, GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM dbo.Department WHERE Name = 'IT' AND IsDeleted = 0);

/* ---------------------------------------------------------------------------
   Roles
--------------------------------------------------------------------------- */
DECLARE @ROLES TABLE (Name VARCHAR(100) PRIMARY KEY);
INSERT INTO @ROLES (Name) VALUES ('Administrator'), ('ProjectLead'), ('Developer'), ('Viewer');

INSERT INTO dbo.Role (Name, Active, IsDeleted, InsertDate)
SELECT R.Name, 1, 0, GETDATE()
FROM @ROLES R
WHERE NOT EXISTS (SELECT 1 FROM dbo.Role X WHERE X.Name = R.Name AND X.IsDeleted = 0);

/* ---------------------------------------------------------------------------
   Permissions -- mirrors PermissionRequirement.All exactly.
--------------------------------------------------------------------------- */
DECLARE @PERMISSIONS TABLE (Name VARCHAR(100) PRIMARY KEY);
INSERT INTO @PERMISSIONS (Name) VALUES
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

INSERT INTO dbo.Permission (Name, Active, IsDeleted, InsertDate)
SELECT P.Name, 1, 0, GETDATE()
FROM @PERMISSIONS P
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permission X WHERE X.Name = P.Name AND X.IsDeleted = 0);

/* ---------------------------------------------------------------------------
   Role -> permission grants.

     Administrator -> everything
     ProjectLead   -> everything except the two administrative manage keys
     Developer     -> the delivery subset
     Viewer        -> read-only
--------------------------------------------------------------------------- */
DECLARE @GRANTS TABLE (RoleName VARCHAR(100), PermissionName VARCHAR(100));

INSERT INTO @GRANTS (RoleName, PermissionName)
SELECT 'Administrator', Name FROM @PERMISSIONS
UNION ALL
SELECT 'ProjectLead', Name FROM @PERMISSIONS
WHERE Name NOT IN ('departments.manage', 'users.manage')
UNION ALL
SELECT 'Developer', Name FROM @PERMISSIONS
WHERE Name IN ('projects.view', 'stories.view', 'stories.manage', 'tasks.view', 'tasks.manage',
               'issues.view', 'issues.manage', 'comments.manage', 'attachments.manage',
               'git.manage', 'notifications.manage', 'reports.view')
UNION ALL
SELECT 'Viewer', Name FROM @PERMISSIONS
WHERE Name IN ('departments.view', 'users.view', 'projects.view', 'stories.view',
               'tasks.view', 'issues.view', 'reports.view');

INSERT INTO dbo.RolePermission (RoleId, PermissionId, Active, IsDeleted, InsertDate)
SELECT R.Id, P.Id, 1, 0, GETDATE()
FROM @GRANTS G
INNER JOIN dbo.Role R       ON R.Name = G.RoleName       AND R.IsDeleted = 0
INNER JOIN dbo.Permission P ON P.Name = G.PermissionName AND P.IsDeleted = 0
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermission RP
    WHERE RP.RoleId = R.Id AND RP.PermissionId = P.Id AND RP.IsDeleted = 0);

COMMIT;
GO
