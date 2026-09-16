/*
    0015_JiraFlowProcedureProjections.sql

    Aligns the PAGED result sets of the Jira-flow membership procedures with the DTO
    contracts the API materialises them into (Dapper maps by column name, so a projection
    that omits or misnames a column silently yields a default value or fails to bind):

        - SP_TEAM_MEMBER   : FullName is aliased to UserName.
        - SP_PROJECT_MEMBER: FullName -> UserName, RoleName -> ProjectRoleName, and the
                             AddedBy / InsertDate provenance columns are now returned.
        - SP_PROJECT_TEAM  : Name -> TeamName, [Key] -> TeamKey, the ProjectRoles join is
                             added so ProjectRoleName can be returned, plus InsertDate.

    Only the PAGED projections change. The INSERT and DELETE branches are reproduced
    verbatim from 0014_JiraFlowSchema.sql so CREATE OR ALTER replaces the whole object.

    Must run after 0014_JiraFlowSchema.sql.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- SP_TEAM_MEMBER
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_TEAM_MEMBER
    @TeamId bigint = NULL,
    @UserId bigint = NULL,
    @TeamRole varchar(20) = NULL,
    @Actor bigint = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;
    IF @Action = 'INSERT'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Teams WHERE Id = @TeamId AND IsDeleted = 0)
            THROW 50000, 'SP_TEAM_MEMBER INSERT: team not found or deleted.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE Id = @UserId AND IsDeleted = 0)
            THROW 50000, 'SP_TEAM_MEMBER INSERT: user not found or deleted.', 1;
        IF @TeamRole IS NULL OR @TeamRole NOT IN ('Lead', 'Member', 'Guest')
            SET @TeamRole = 'Member';
        IF EXISTS (SELECT 1 FROM dbo.TeamMembers WHERE TeamId = @TeamId AND UserId = @UserId AND IsDeleted = 0)
            THROW 50000, 'User is already a member of this team.', 1;

        INSERT dbo.TeamMembers (TeamId, UserId, TeamRole, JoinedAtUtc, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@TeamId, @UserId, @TeamRole, SYSUTCDATETIME(), 1, 0, SYSUTCDATETIME(), @Actor);

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'DELETE'
    BEGIN
        DECLARE @CurrentRole varchar(20) = (SELECT TeamRole FROM dbo.TeamMembers WHERE TeamId = @TeamId AND UserId = @UserId AND IsDeleted = 0);
        IF @CurrentRole IS NULL
            THROW 50000, 'SP_TEAM_MEMBER DELETE: team member not found.', 1;

        IF @CurrentRole = 'Lead'
        BEGIN
            IF (SELECT COUNT(*) FROM dbo.TeamMembers WHERE TeamId = @TeamId AND TeamRole = 'Lead' AND IsDeleted = 0 AND UserId <> @UserId) = 0
                THROW 50000, 'Cannot remove the last Lead of the team.', 1;
        END

        UPDATE dbo.TeamMembers
        SET IsDeleted = 1,
            Active = 0,
            DeletedDate = SYSUTCDATETIME(),
            DeletedBy = @Actor,
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @Actor
        WHERE TeamId = @TeamId AND UserId = @UserId AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1)
        FROM dbo.TeamMembers tm
        JOIN dbo.[User] u ON u.Id = tm.UserId
        WHERE tm.TeamId = @TeamId AND tm.IsDeleted = 0;

        SELECT tm.TeamId,
               tm.UserId,
               u.FullName AS UserName,
               u.Email,
               tm.TeamRole,
               tm.JoinedAtUtc
        FROM dbo.TeamMembers tm
        JOIN dbo.[User] u ON u.Id = tm.UserId
        WHERE tm.TeamId = @TeamId AND tm.IsDeleted = 0
        ORDER BY tm.TeamRole, u.FullName
        OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
END
GO

-- ============================================================================
-- SP_PROJECT_MEMBER
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_PROJECT_MEMBER
    @ProjectId bigint = NULL,
    @UserId bigint = NULL,
    @ProjectRoleId bigint = NULL,
    @AddedBy bigint = NULL,
    @Actor bigint = NULL,
    @Search nvarchar(200) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;
    IF @Action = 'INSERT'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Project WHERE Id = @ProjectId AND IsDeleted = 0)
            THROW 50000, 'SP_PROJECT_MEMBER INSERT: project not found or deleted.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE Id = @UserId AND IsDeleted = 0)
            THROW 50000, 'SP_PROJECT_MEMBER INSERT: user not found or deleted.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.ProjectRoles WHERE Id = @ProjectRoleId AND IsDeleted = 0)
            THROW 50000, 'SP_PROJECT_MEMBER INSERT: project role not found or deleted.', 1;
        IF EXISTS (SELECT 1 FROM dbo.ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId AND IsDeleted = 0)
            THROW 50000, 'User is already a member of this project.', 1;

        INSERT dbo.ProjectMembers (ProjectId, UserId, ProjectRoleId, AddedBy, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@ProjectId, @UserId, @ProjectRoleId, @AddedBy, 1, 0, SYSUTCDATETIME(), @AddedBy);

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'DELETE'
    BEGIN
        UPDATE dbo.ProjectMembers
        SET IsDeleted = 1,
            Active = 0,
            DeletedDate = SYSUTCDATETIME(),
            DeletedBy = @Actor,
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @Actor
        WHERE ProjectId = @ProjectId AND UserId = @UserId AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1)
        FROM dbo.ProjectMembers pm
        JOIN dbo.[User] u ON u.Id = pm.UserId
        JOIN dbo.ProjectRoles pr ON pr.Id = pm.ProjectRoleId
        WHERE pm.ProjectId = @ProjectId AND pm.IsDeleted = 0
          AND (@Search IS NULL OR u.FullName LIKE '%' + @Search + '%' OR u.Email LIKE '%' + @Search + '%');

        SELECT pm.ProjectId,
               pm.UserId,
               u.FullName AS UserName,
               u.Email,
               pm.ProjectRoleId,
               pr.Name AS ProjectRoleName,
               pm.AddedBy,
               pm.InsertDate
        FROM dbo.ProjectMembers pm
        JOIN dbo.[User] u ON u.Id = pm.UserId
        JOIN dbo.ProjectRoles pr ON pr.Id = pm.ProjectRoleId
        WHERE pm.ProjectId = @ProjectId AND pm.IsDeleted = 0
          AND (@Search IS NULL OR u.FullName LIKE '%' + @Search + '%' OR u.Email LIKE '%' + @Search + '%')
        ORDER BY pr.SortOrder, u.FullName
        OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
END
GO

-- ============================================================================
-- SP_PROJECT_TEAM
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_PROJECT_TEAM
    @ProjectId bigint = NULL,
    @TeamId bigint = NULL,
    @ProjectRoleId bigint = NULL,
    @AddedBy bigint = NULL,
    @Actor bigint = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;
    IF @Action = 'INSERT'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Project WHERE Id = @ProjectId AND IsDeleted = 0)
            THROW 50000, 'SP_PROJECT_TEAM INSERT: project not found or deleted.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Teams WHERE Id = @TeamId AND IsDeleted = 0)
            THROW 50000, 'SP_PROJECT_TEAM INSERT: team not found or deleted.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.ProjectRoles WHERE Id = @ProjectRoleId AND IsDeleted = 0)
            THROW 50000, 'SP_PROJECT_TEAM INSERT: project role not found or deleted.', 1;
        IF EXISTS (SELECT 1 FROM dbo.ProjectTeams WHERE ProjectId = @ProjectId AND TeamId = @TeamId AND IsDeleted = 0)
            THROW 50000, 'Team is already granted to this project.', 1;

        INSERT dbo.ProjectTeams (ProjectId, TeamId, ProjectRoleId, AddedBy, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@ProjectId, @TeamId, @ProjectRoleId, @AddedBy, 1, 0, SYSUTCDATETIME(), @AddedBy);

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'DELETE'
    BEGIN
        UPDATE dbo.ProjectTeams
        SET IsDeleted = 1,
            Active = 0,
            DeletedDate = SYSUTCDATETIME(),
            DeletedBy = @Actor,
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @Actor
        WHERE ProjectId = @ProjectId AND TeamId = @TeamId AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1)
        FROM dbo.ProjectTeams pt
        JOIN dbo.Teams t ON t.Id = pt.TeamId
        WHERE pt.ProjectId = @ProjectId AND pt.IsDeleted = 0;

        SELECT pt.ProjectId,
               pt.TeamId,
               t.Name AS TeamName,
               t.[Key] AS TeamKey,
               pt.ProjectRoleId,
               pr.Name AS ProjectRoleName,
               ISNULL(mc.MemberCount, 0) AS MemberCount,
               pt.InsertDate
        FROM dbo.ProjectTeams pt
        JOIN dbo.Teams t ON t.Id = pt.TeamId
        JOIN dbo.ProjectRoles pr ON pr.Id = pt.ProjectRoleId
        LEFT JOIN (
            SELECT tm.TeamId, COUNT(*) AS MemberCount
            FROM dbo.TeamMembers tm
            WHERE tm.IsDeleted = 0
            GROUP BY tm.TeamId
        ) mc ON mc.TeamId = t.Id
        WHERE pt.ProjectId = @ProjectId AND pt.IsDeleted = 0
        ORDER BY t.Name
        OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
END
GO

-- ============================================================================
-- Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0015' AND Name = 'JiraFlowProcedureProjections' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0015', 'JiraFlowProcedureProjections', SYSUTCDATETIME(), 1);
END
GO
