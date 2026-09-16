/*
    0014_JiraFlowSchema.sql

    Jira-style project flow schema: roles, teams, membership and the stored
    procedures that drive them.

    Tables:
        1. ProjectRoles    - named roles within a project (Admin / Member / Viewer)
        2. Teams           - reusable groups of users (keyed by an uppercase code)
        3. TeamMembers     - user membership of a team (composite PK)
        4. ProjectMembers  - direct user membership of a project (composite PK)
        5. ProjectTeams    - team grants on a project (composite PK)
        6. ProjectCounters - per-project issue counter

    Plus:
        - New columns on dbo.Project (LeadUserId, TypeCode, AccessLevel,
          DefaultAssigneeMode, AvatarUrl, IsArchived)
        - Seed data for ProjectRoles
        - Stored procedures: SP_PROJECT_ROLE, SP_TEAM, SP_TEAM_MEMBER,
          SP_PROJECT_MEMBER, SP_PROJECT_TEAM, usp_Project_EffectiveRole,
          usp_Project_CreateFromTemplate

    Conventions (aligned with 0009-0013):
        - Idempotent: IF NOT EXISTS guards for DDL, CREATE OR ALTER for procedures.
        - Each object is created in its own GO batch so CREATE PROCEDURE is the
          first statement of its batch.
        - SET QUOTED_IDENTIFIER / ANSI_NULLS ON; NOCOUNT ON; XACT_ABORT ON.
        - BIGINT IDENTITY(1,1) PKs, dbo schema.
        - Full audit tail on every new table:
          Active, InsertDate, InsertedBy, UpdateDate, UpdatedBy,
          IsDeleted, DeletedDate, DeletedBy.
        - Foreign keys target the existing dbo.[User] / dbo.Project tables.
        - Records this migration in dbo.SchemaMigration.

    Must run after 0009_DeployCompleteSchema.sql (owns [User] / Project).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. ProjectRoles
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'ProjectRoles')
BEGIN
    CREATE TABLE dbo.ProjectRoles(
        Id bigint IDENTITY(1,1) NOT NULL,
        Name nvarchar(60) NOT NULL,
        SortOrder int NOT NULL,
        ColorKey varchar(20) NULL,
        IsSystem bit NOT NULL DEFAULT 0,
        Active bit NOT NULL DEFAULT 1,
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NOT NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL DEFAULT 0,
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_projectroles PRIMARY KEY CLUSTERED (Id ASC),
        CONSTRAINT UQ_projectroles_name UNIQUE (Name)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 2. Teams
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Teams')
BEGIN
    CREATE TABLE dbo.Teams(
        Id bigint IDENTITY(1,1) NOT NULL,
        [Key] varchar(10) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Description nvarchar(1000) NULL,
        IsActive bit NOT NULL DEFAULT 1,
        LeadUserId bigint NOT NULL,
        CreatedBy bigint NOT NULL,
        Active bit NOT NULL DEFAULT 1,
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NOT NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL DEFAULT 0,
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_teams PRIMARY KEY CLUSTERED (Id ASC),
        CONSTRAINT CK_teams_key CHECK ([Key] NOT LIKE '%[^A-Z0-9]%' AND LEN([Key]) >= 2)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 3. TeamMembers
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'TeamMembers')
BEGIN
    CREATE TABLE dbo.TeamMembers(
        TeamId bigint NOT NULL,
        UserId bigint NOT NULL,
        TeamRole varchar(20) NOT NULL DEFAULT 'Member',
        JoinedAtUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        Active bit NOT NULL DEFAULT 1,
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NOT NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL DEFAULT 0,
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_teammembers PRIMARY KEY CLUSTERED (TeamId ASC, UserId ASC),
        CONSTRAINT CK_teammembers_role CHECK (TeamRole IN ('Lead', 'Member', 'Guest'))
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 4. ProjectMembers
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'ProjectMembers')
BEGIN
    CREATE TABLE dbo.ProjectMembers(
        ProjectId bigint NOT NULL,
        UserId bigint NOT NULL,
        ProjectRoleId bigint NOT NULL,
        AddedBy bigint NOT NULL,
        Active bit NOT NULL DEFAULT 1,
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NOT NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL DEFAULT 0,
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_projectmembers PRIMARY KEY CLUSTERED (ProjectId ASC, UserId ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 5. ProjectTeams
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'ProjectTeams')
BEGIN
    CREATE TABLE dbo.ProjectTeams(
        ProjectId bigint NOT NULL,
        TeamId bigint NOT NULL,
        ProjectRoleId bigint NOT NULL,
        AddedBy bigint NOT NULL,
        Active bit NOT NULL DEFAULT 1,
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NOT NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL DEFAULT 0,
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_projectteams PRIMARY KEY CLUSTERED (ProjectId ASC, TeamId ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 6. ProjectCounters
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'ProjectCounters')
BEGIN
    CREATE TABLE dbo.ProjectCounters(
        ProjectId bigint NOT NULL,
        LastIssueNumber int NOT NULL DEFAULT 0,
        CONSTRAINT PK_projectcounters PRIMARY KEY CLUSTERED (ProjectId ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 7. ALTER dbo.Project (new columns)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'LeadUserId')
    ALTER TABLE dbo.Project ADD LeadUserId bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'TypeCode')
    ALTER TABLE dbo.Project ADD TypeCode varchar(20) NOT NULL DEFAULT 'SCRUM';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'AccessLevel')
    ALTER TABLE dbo.Project ADD AccessLevel varchar(20) NOT NULL DEFAULT 'RESTRICTED';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'DefaultAssigneeMode')
    ALTER TABLE dbo.Project ADD DefaultAssigneeMode varchar(20) NOT NULL DEFAULT 'UNASSIGNED';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'AvatarUrl')
    ALTER TABLE dbo.Project ADD AvatarUrl nvarchar(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'IsArchived')
    ALTER TABLE dbo.Project ADD IsArchived bit NOT NULL DEFAULT 0;
GO

-- ============================================================================
-- 8. Project check constraints
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_project_typecode')
    ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_typecode CHECK (TypeCode IN ('SCRUM', 'KANBAN', 'BASIC'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_project_accesslevel')
    ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_accesslevel CHECK (AccessLevel IN ('OPEN', 'RESTRICTED', 'PRIVATE'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_project_defaultassigneemode')
    ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_defaultassigneemode CHECK (DefaultAssigneeMode IN ('UNASSIGNED', 'PROJECT_LEAD', 'COMPONENT_LEAD'));
GO

-- Bind a DEFAULT for Project.Active (when missing) so the template-creation
-- procedure below can omit it and remain executable on existing databases.
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Project') AND c.name = 'Active'
)
    ALTER TABLE dbo.Project ADD CONSTRAINT DF_project_active DEFAULT ((1)) FOR Active;
GO

-- ============================================================================
-- 9. Indexes
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_teams_key' AND object_id = OBJECT_ID('dbo.Teams'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_teams_key ON dbo.Teams([Key] ASC) WHERE IsDeleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_teammembers_userid' AND object_id = OBJECT_ID('dbo.TeamMembers'))
    CREATE NONCLUSTERED INDEX IX_teammembers_userid ON dbo.TeamMembers(UserId ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_projectmembers_userid' AND object_id = OBJECT_ID('dbo.ProjectMembers'))
    CREATE NONCLUSTERED INDEX IX_projectmembers_userid ON dbo.ProjectMembers(UserId ASC);
GO

-- ============================================================================
-- 10. Foreign keys
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_teams_leaduser')
    ALTER TABLE dbo.Teams ADD CONSTRAINT FK_teams_leaduser FOREIGN KEY (LeadUserId) REFERENCES dbo.[User](Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_teammembers_team')
    ALTER TABLE dbo.TeamMembers ADD CONSTRAINT FK_teammembers_team FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_teammembers_user')
    ALTER TABLE dbo.TeamMembers ADD CONSTRAINT FK_teammembers_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectmembers_project')
    ALTER TABLE dbo.ProjectMembers ADD CONSTRAINT FK_projectmembers_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectmembers_user')
    ALTER TABLE dbo.ProjectMembers ADD CONSTRAINT FK_projectmembers_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectmembers_role')
    ALTER TABLE dbo.ProjectMembers ADD CONSTRAINT FK_projectmembers_role FOREIGN KEY (ProjectRoleId) REFERENCES dbo.ProjectRoles(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectteams_project')
    ALTER TABLE dbo.ProjectTeams ADD CONSTRAINT FK_projectteams_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectteams_team')
    ALTER TABLE dbo.ProjectTeams ADD CONSTRAINT FK_projectteams_team FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectteams_role')
    ALTER TABLE dbo.ProjectTeams ADD CONSTRAINT FK_projectteams_role FOREIGN KEY (ProjectRoleId) REFERENCES dbo.ProjectRoles(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_projectcounters_project')
    ALTER TABLE dbo.ProjectCounters ADD CONSTRAINT FK_projectcounters_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
GO

-- ============================================================================
-- 11. Seed data
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM dbo.ProjectRoles WHERE Name = 'Project Admin')
BEGIN
    INSERT INTO dbo.ProjectRoles (Name, SortOrder, ColorKey, IsSystem, InsertedBy)
    VALUES ('Project Admin', 1, '#primary', 1, 0),
           ('Member', 2, '#info', 1, 0),
           ('Viewer', 3, '#secondary', 1, 0);
END
GO

-- ============================================================================
-- 12. Stored procedures
-- ============================================================================

-- SP_PROJECT_ROLE
CREATE OR ALTER PROCEDURE dbo.SP_PROJECT_ROLE
    @Id bigint = NULL,
    @Name nvarchar(60) = NULL,
    @SortOrder int = NULL,
    @ColorKey varchar(20) = NULL,
    @UserId bigint = NULL,
    @Search nvarchar(60) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;
    IF @Action = 'FETCH'
        SELECT Id, Name, SortOrder, IsSystem
        FROM dbo.ProjectRoles
        WHERE Id = @Id AND IsDeleted = 0;
    ELSE IF @Action = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1)
        FROM dbo.ProjectRoles
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Name LIKE '%' + @Search + '%');

        SELECT Id, Name, SortOrder, IsSystem
        FROM dbo.ProjectRoles
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Name LIKE '%' + @Search + '%')
        ORDER BY SortOrder, Name
        OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @Action = 'INSERT'
    BEGIN
        IF NULLIF(LTRIM(RTRIM(ISNULL(@Name, N''))), N'') IS NULL
            THROW 50000, 'SP_PROJECT_ROLE INSERT requires @Name.', 1;
        IF EXISTS (SELECT 1 FROM dbo.ProjectRoles WHERE Name = @Name AND IsDeleted = 0)
            THROW 50000, 'Project role name already exists.', 1;

        INSERT dbo.ProjectRoles (Name, SortOrder, ColorKey, IsSystem, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@Name, ISNULL(@SortOrder, 0), @ColorKey, 0, 1, 0, SYSUTCDATETIME(), @UserId);

        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @Action = 'UPDATE'
    BEGIN
        UPDATE dbo.ProjectRoles
        SET Name = ISNULL(@Name, Name),
            SortOrder = ISNULL(@SortOrder, SortOrder),
            ColorKey = @ColorKey,
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'DELETE'
    BEGIN
        UPDATE dbo.ProjectRoles
        SET IsDeleted = 1,
            Active = 0,
            DeletedDate = SYSUTCDATETIME(),
            DeletedBy = @UserId,
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- SP_TEAM
CREATE OR ALTER PROCEDURE dbo.SP_TEAM
    @Id bigint = NULL,
    @Key varchar(10) = NULL,
    @Name nvarchar(150) = NULL,
    @Description nvarchar(1000) = NULL,
    @IsActive bit = NULL,
    @LeadUserId bigint = NULL,
    @CreatedBy bigint = NULL,
    @UserId bigint = NULL,
    @Search nvarchar(150) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;
    IF @Action = 'FETCH'
        SELECT t.Id,
               t.[Key],
               t.Name,
               t.Description,
               t.IsActive,
               t.LeadUserId,
               u.FullName AS LeadName,
               ISNULL(mc.MemberCount, 0) AS MemberCount,
               t.InsertDate
        FROM dbo.Teams t
        JOIN dbo.[User] u ON u.Id = t.LeadUserId
        LEFT JOIN (
            SELECT tm.TeamId, COUNT(*) AS MemberCount
            FROM dbo.TeamMembers tm
            WHERE tm.IsDeleted = 0
            GROUP BY tm.TeamId
        ) mc ON mc.TeamId = t.Id
        WHERE t.Id = @Id AND t.IsDeleted = 0;
    ELSE IF @Action = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1)
        FROM dbo.Teams t
        WHERE t.IsDeleted = 0
          AND (@Search IS NULL OR t.Name LIKE '%' + @Search + '%' OR t.[Key] LIKE '%' + @Search + '%');

        SELECT t.Id,
               t.[Key],
               t.Name,
               t.Description,
               t.IsActive,
               t.LeadUserId,
               u.FullName AS LeadName,
               ISNULL(mc.MemberCount, 0) AS MemberCount,
               t.InsertDate
        FROM dbo.Teams t
        JOIN dbo.[User] u ON u.Id = t.LeadUserId
        LEFT JOIN (
            SELECT tm.TeamId, COUNT(*) AS MemberCount
            FROM dbo.TeamMembers tm
            WHERE tm.IsDeleted = 0
            GROUP BY tm.TeamId
        ) mc ON mc.TeamId = t.Id
        WHERE t.IsDeleted = 0
          AND (@Search IS NULL OR t.Name LIKE '%' + @Search + '%' OR t.[Key] LIKE '%' + @Search + '%')
        ORDER BY t.Id DESC
        OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @Action = 'INSERT'
    BEGIN
        IF NULLIF(LTRIM(RTRIM(ISNULL(@Key, N''))), N'') IS NULL
            THROW 50000, 'SP_TEAM INSERT requires @Key.', 1;
        IF LEN(@Key) < 2 OR @Key LIKE '%[^A-Z0-9]%'
            THROW 50000, 'Team key must be at least 2 characters and contain only A-Z and 0-9.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE Id = @LeadUserId AND IsDeleted = 0)
            THROW 50000, 'SP_TEAM INSERT: LeadUserId does not reference a valid user.', 1;
        IF EXISTS (SELECT 1 FROM dbo.Teams WHERE [Key] = @Key AND IsDeleted = 0)
            THROW 50000, 'Team key already exists.', 1;

        INSERT dbo.Teams ([Key], Name, Description, IsActive, LeadUserId, CreatedBy, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@Key, @Name, @Description, ISNULL(@IsActive, 1), @LeadUserId, @CreatedBy, 1, 0, SYSUTCDATETIME(), @CreatedBy);

        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @Action = 'UPDATE'
    BEGIN
        UPDATE dbo.Teams
        SET Name = ISNULL(@Name, Name),
            Description = ISNULL(@Description, Description),
            IsActive = ISNULL(@IsActive, IsActive),
            LeadUserId = ISNULL(@LeadUserId, LeadUserId),
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @Action = 'DELETE'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Teams WHERE Id = @Id AND IsDeleted = 0)
            THROW 50000, 'SP_TEAM DELETE: team not found or already deleted.', 1;
        IF EXISTS (SELECT 1 FROM dbo.ProjectTeams pt WHERE pt.TeamId = @Id AND pt.IsDeleted = 0)
            THROW 50000, 'Cannot delete a team that is referenced by an active project grant.', 1;

        UPDATE dbo.Teams
        SET IsDeleted = 1,
            Active = 0,
            DeletedDate = SYSUTCDATETIME(),
            DeletedBy = @UserId,
            UpdateDate = SYSUTCDATETIME(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- SP_TEAM_MEMBER
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

        SELECT tm.TeamId, tm.UserId, tm.TeamRole, tm.JoinedAtUtc, u.FullName, u.Email
        FROM dbo.TeamMembers tm
        JOIN dbo.[User] u ON u.Id = tm.UserId
        WHERE tm.TeamId = @TeamId AND tm.IsDeleted = 0
        ORDER BY tm.TeamRole, u.FullName
        OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
END
GO

-- SP_PROJECT_MEMBER
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

        SELECT pm.ProjectId, pm.UserId, pm.ProjectRoleId, u.FullName, u.Email, pr.Name AS RoleName
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

-- SP_PROJECT_TEAM
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

        SELECT pt.ProjectId, pt.TeamId, pt.ProjectRoleId, t.Name, t.[Key],
               ISNULL(mc.MemberCount, 0) AS MemberCount
        FROM dbo.ProjectTeams pt
        JOIN dbo.Teams t ON t.Id = pt.TeamId
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

-- usp_Project_EffectiveRole
CREATE OR ALTER PROCEDURE dbo.usp_Project_EffectiveRole
    @ProjectId bigint,
    @UserId bigint
AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 pr.Id, pr.Name, pr.SortOrder
    FROM (
        SELECT ProjectRoleId FROM dbo.ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId AND IsDeleted = 0
        UNION ALL
        SELECT pt.ProjectRoleId FROM dbo.ProjectTeams pt
        JOIN dbo.TeamMembers tm ON tm.TeamId = pt.TeamId AND tm.IsDeleted = 0
        WHERE pt.ProjectId = @ProjectId AND tm.UserId = @UserId AND pt.IsDeleted = 0
    ) g
    JOIN dbo.ProjectRoles pr ON pr.Id = g.ProjectRoleId
    ORDER BY pr.SortOrder ASC;
END
GO

-- usp_Project_CreateFromTemplate
CREATE OR ALTER PROCEDURE dbo.usp_Project_CreateFromTemplate
    @Key VARCHAR(10), @Name NVARCHAR(200), @Description NVARCHAR(MAX)=NULL,
    @TypeCode VARCHAR(20), @AccessLevel VARCHAR(20)='RESTRICTED',
    @LeadUserId BIGINT, @CreatedBy BIGINT,
    @NewProjectId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT INTO dbo.Project ([Key], Name, Description, TypeCode, AccessLevel, LeadUserId, OwnerUserId, Status, InsertedBy)
        VALUES (@Key, @Name, @Description, @TypeCode, @AccessLevel, @LeadUserId, @CreatedBy, 'Planning', @CreatedBy);
        SET @NewProjectId = SCOPE_IDENTITY();
        INSERT INTO dbo.ProjectCounters (ProjectId, LastIssueNumber) VALUES (@NewProjectId, 0);
        -- Add lead as Project Admin
        INSERT INTO dbo.ProjectMembers (ProjectId, UserId, ProjectRoleId, AddedBy, InsertedBy)
        VALUES (@NewProjectId, @LeadUserId, 1, @CreatedBy, @CreatedBy);
        IF @CreatedBy != @LeadUserId
            INSERT INTO dbo.ProjectMembers (ProjectId, UserId, ProjectRoleId, AddedBy, InsertedBy)
            VALUES (@NewProjectId, @CreatedBy, 1, @CreatedBy, @CreatedBy);
        INSERT INTO dbo.AuditLog (EntityName, EntityId, [Action], NewValue, UserId, InsertedBy)
        VALUES ('Project', @NewProjectId, 'INSERT', @Name, @CreatedBy, @CreatedBy);
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION; THROW;
    END CATCH;
END
GO

-- ============================================================================
-- 13. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0014' AND Name = 'JiraFlowSchema' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0014', 'JiraFlowSchema', SYSUTCDATETIME(), 1);
END
GO
