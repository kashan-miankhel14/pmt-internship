/*
    0018_ProjectCreationFixes.sql

    Makes both project-creation paths produce a *complete* project row.

    Background
    ----------
    The wizard (PMT-WEB ProjectWizard.jsx) posts to POST /api/v1/projects/from-template,
    which is now wired to dbo.usp_Project_CreateFromTemplate. Two defects blocked that
    path and the plain SP_PROJECT INSERT path:

      1. dbo.usp_Project_CreateFromTemplate (0014_JiraFlowSchema.sql) inserts into
         dbo.Project WITHOUT DepartmentId. dbo.Project.DepartmentId is NOT NULL and
         carries FK_project_department, and no DEFAULT is bound to it, so *every*
         execution of that procedure failed with a NOT NULL violation. The wizard does
         not collect a department, so the procedure now derives one.
      2. The plain SP_PROJECT INSERT path (0010_CriticalFixes.sql) never seeded
         dbo.ProjectCounters and never populated the Jira-flow columns added in 0014
         (TypeCode / AccessLevel / LeadUserId). A project created that way had no
         counter row, so issue numbering had to lazily MERGE one in, and the project
         had no lead.

    Changes applied
    ---------------
        1. Safety net for databases that predate 0014: create dbo.ProjectCounters and
           add dbo.Project.TypeCode / AccessLevel / LeadUserId (plus the CHECK
           constraints) when missing, so section 3/4 can reference them.
        2. Backfill dbo.ProjectCounters for every project that has no counter row.
           Raise-only and insert-missing: an existing counter is never lowered, so a
           number already handed out by SP_ISSUE can never be reused. When
           dbo.Issue.Number exists (0017_IssueNumbering.sql) the seed value is that
           project's highest live issue number, otherwise 0. Largely a no-op after
           0017_IssueNumbering; kept so this migration is correct standalone.
        3. CREATE OR ALTER dbo.SP_PROJECT:
             - INSERT now captures the new id into a variable, seeds
               dbo.ProjectCounters(ProjectId, 0) when absent, and defaults
               TypeCode='SCRUM', AccessLevel='RESTRICTED', LeadUserId=@OwnerUserId.
               It still returns exactly one bigint result set (the new id), so
               ProjectRepository.CreateAsync (ExecuteScalarAsync<long>) is unaffected.
             - Three new trailing parameters (@TypeCode, @AccessLevel, @LeadUserId)
               all default to NULL, so every existing caller that does not pass them
               keeps working unchanged.
             - UPDATE uses ISNULL for the three new columns, so a partial update
               cannot null-out an existing lead or downgrade a type.
             - FETCH / PAGED / DELETE are byte-for-byte the 0010 behaviour.
        4. CREATE OR ALTER dbo.usp_Project_CreateFromTemplate:
             - Resolves @DepartmentId from the creator's own department, falling back
               to the first active department, and throws a clear error when neither
               exists (fixes the NOT NULL / FK violation described above).
             - Normalises @Key / @TypeCode / @AccessLevel to upper case and rejects a
               duplicate live key with a readable message instead of a raw index error.
             - Validates @LeadUserId and @CreatedBy against dbo.[User].
             - Resolves the Project Admin role by NAME instead of the hard-coded id 1.
             - Writes Active / IsDeleted / InsertDate explicitly on every insert rather
               than relying on bound defaults, matching the convention documented in
               0006_RepairIsDeletedDefaults.sql (the 0009 lineage has no DEFAULT on
               dbo.AuditLog.Active, which would otherwise fail the audit insert).
             - CATCH now guards ROLLBACK with XACT_STATE() <> 0; the 0014 version
               rolled back unconditionally, which itself errored when the transaction
               had already been doomed and aborted.
        5. Record this migration in dbo.SchemaMigration.

    Numbering note: this file is 0018 and not 0017 because 0017 is already taken twice
    (0017_IssueNumbering.sql and 0017_SprintsAndBoards.sql).

    IDEMPOTENT and re-runnable. Each object is created in its own GO batch so
    CREATE OR ALTER PROCEDURE is the first statement of its batch. No wrapping
    transaction: partial success is safe to re-run.

    Must run after 0014_JiraFlowSchema.sql (owns dbo.ProjectCounters and the Project
    Jira-flow columns) and after 0017_IssueNumbering.sql (owns dbo.Issue.Number).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. Safety net for databases that predate 0014 (0014 owns the canonical shapes)
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

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'LeadUserId')
    ALTER TABLE dbo.Project ADD LeadUserId bigint NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'TypeCode')
    ALTER TABLE dbo.Project ADD TypeCode varchar(20) NOT NULL CONSTRAINT DF_project_typecode DEFAULT 'SCRUM';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Project') AND name = 'AccessLevel')
    ALTER TABLE dbo.Project ADD AccessLevel varchar(20) NOT NULL CONSTRAINT DF_project_accesslevel DEFAULT 'RESTRICTED';
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_project_typecode')
    ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_typecode CHECK (TypeCode IN ('SCRUM', 'KANBAN', 'BASIC'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_project_accesslevel')
    ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_accesslevel CHECK (AccessLevel IN ('OPEN', 'RESTRICTED', 'PRIVATE'));
GO

-- ============================================================================
-- 2. Backfill dbo.ProjectCounters for existing projects with no counter row
--
--    Insert-missing only; never touches an existing counter, so SP_ISSUE can
--    never hand out a number twice. Seeded from the highest live issue number
--    when dbo.Issue.Number exists (0017_IssueNumbering.sql), otherwise 0.
--    Soft-deleted projects are included so a restored project keeps a counter.
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Issue') AND name = 'Number')
BEGIN
    EXEC sp_executesql N'
        INSERT dbo.ProjectCounters (ProjectId, LastIssueNumber)
        SELECT p.Id, ISNULL(m.MaxNumber, 0)
        FROM dbo.Project p
        LEFT JOIN (
            SELECT ProjectId, MAX(Number) AS MaxNumber
            FROM dbo.Issue
            WHERE IsDeleted = 0
            GROUP BY ProjectId
        ) m ON m.ProjectId = p.Id
        WHERE NOT EXISTS (SELECT 1 FROM dbo.ProjectCounters pc WHERE pc.ProjectId = p.Id);';
END
ELSE
BEGIN
    INSERT dbo.ProjectCounters (ProjectId, LastIssueNumber)
    SELECT p.Id, 0
    FROM dbo.Project p
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ProjectCounters pc WHERE pc.ProjectId = p.Id);
END
GO

-- ============================================================================
-- 3. SP_PROJECT: seed ProjectCounters on INSERT and populate the 0014 columns
--
--    @TypeCode / @AccessLevel / @LeadUserId are appended with NULL defaults, so
--    ProjectRepository's existing anonymous-parameter call is unaffected.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_PROJECT
  @Id bigint=NULL,@Name varchar(200)=NULL,@Description varchar(max)=NULL,@Key varchar(50)=NULL,@Status varchar(30)=NULL,@DepartmentId bigint=NULL,@OwnerUserId bigint=NULL,@StartDate date=NULL,@TargetDate date=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(200)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20),
  @TypeCode varchar(20)=NULL,@AccessLevel varchar(20)=NULL,@LeadUserId bigint=NULL
AS BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  IF @Action='FETCH' SELECT * FROM dbo.Project WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT'
  BEGIN
    DECLARE @NewProjectId bigint;

    BEGIN TRY
      BEGIN TRANSACTION;

      INSERT dbo.Project(Name,Description,[Key],Status,DepartmentId,OwnerUserId,StartDate,TargetDate,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy,TypeCode,AccessLevel,LeadUserId)
      VALUES(@Name,@Description,@Key,ISNULL(@Status,'Planning'),@DepartmentId,@OwnerUserId,@StartDate,@TargetDate,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId,
             UPPER(ISNULL(@TypeCode,'SCRUM')),UPPER(ISNULL(@AccessLevel,'RESTRICTED')),ISNULL(@LeadUserId,@OwnerUserId));

      -- SCOPE_IDENTITY() is captured before any further insert. dbo.ProjectCounters has
      -- no IDENTITY column, but reading it into a variable first keeps the contract
      -- explicit and immune to a future column change.
      SET @NewProjectId = CONVERT(bigint,SCOPE_IDENTITY());

      IF NOT EXISTS (SELECT 1 FROM dbo.ProjectCounters WHERE ProjectId=@NewProjectId)
        INSERT dbo.ProjectCounters(ProjectId,LastIssueNumber) VALUES(@NewProjectId,0);

      COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
      IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
      THROW;
    END CATCH

    SELECT @NewProjectId;
  END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Project SET Name=ISNULL(@Name,Name),Description=ISNULL(@Description,Description),[Key]=ISNULL(@Key,[Key]),Status=ISNULL(@Status,Status),DepartmentId=ISNULL(@DepartmentId,DepartmentId),OwnerUserId=ISNULL(@OwnerUserId,OwnerUserId),StartDate=ISNULL(@StartDate,StartDate),TargetDate=ISNULL(@TargetDate,TargetDate),Active=ISNULL(@Active,Active),TypeCode=UPPER(ISNULL(@TypeCode,TypeCode)),AccessLevel=UPPER(ISNULL(@AccessLevel,AccessLevel)),LeadUserId=ISNULL(@LeadUserId,LeadUserId),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Project SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 4. usp_Project_CreateFromTemplate: supply the mandatory DepartmentId
--
--    Signature is unchanged from 0014 (same parameters, same OUTPUT), so the
--    Dapper call in ProjectRepository.CreateFromTemplateAsync binds identically.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_Project_CreateFromTemplate
    @Key VARCHAR(10), @Name NVARCHAR(200), @Description NVARCHAR(MAX)=NULL,
    @TypeCode VARCHAR(20), @AccessLevel VARCHAR(20)='RESTRICTED',
    @LeadUserId BIGINT, @CreatedBy BIGINT,
    @NewProjectId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;

    SET @Key = UPPER(LTRIM(RTRIM(@Key)));
    SET @Name = LTRIM(RTRIM(@Name));
    SET @TypeCode = UPPER(LTRIM(RTRIM(ISNULL(@TypeCode, 'SCRUM'))));
    SET @AccessLevel = UPPER(LTRIM(RTRIM(ISNULL(@AccessLevel, 'RESTRICTED'))));

    IF NULLIF(@Key, '') IS NULL
        THROW 50000, 'usp_Project_CreateFromTemplate requires @Key.', 1;
    IF NULLIF(@Name, N'') IS NULL
        THROW 50000, 'usp_Project_CreateFromTemplate requires @Name.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Project WHERE [Key] = @Key AND IsDeleted = 0)
        THROW 50000, 'Another project already uses this key.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE Id = @LeadUserId AND IsDeleted = 0)
        THROW 50000, 'usp_Project_CreateFromTemplate: @LeadUserId does not reference a valid user.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE Id = @CreatedBy AND IsDeleted = 0)
        THROW 50000, 'usp_Project_CreateFromTemplate: @CreatedBy does not reference a valid user.', 1;

    -- dbo.Project.DepartmentId is NOT NULL with FK_project_department and has no
    -- DEFAULT. The wizard never collects a department, so inherit the creator's and
    -- fall back to the first active department.
    DECLARE @DepartmentId bigint =
        (SELECT TOP (1) DepartmentId FROM dbo.[User] WHERE Id = @CreatedBy AND IsDeleted = 0);

    IF @DepartmentId IS NULL
        SET @DepartmentId = (SELECT TOP (1) Id FROM dbo.Department WHERE Active = 1 AND IsDeleted = 0 ORDER BY Id);

    IF @DepartmentId IS NULL
        THROW 50000, 'usp_Project_CreateFromTemplate: no active department is available to own the project.', 1;

    -- Resolve the seeded role by name; the 0014 version hard-coded ProjectRoleId = 1.
    DECLARE @AdminRoleId bigint =
        (SELECT TOP (1) Id FROM dbo.ProjectRoles WHERE Name = 'Project Admin' AND IsDeleted = 0 ORDER BY SortOrder, Id);

    IF @AdminRoleId IS NULL
        THROW 50000, 'usp_Project_CreateFromTemplate: the seeded "Project Admin" role is missing.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.Project ([Key], Name, Description, TypeCode, AccessLevel, LeadUserId, DepartmentId, OwnerUserId, Status, Active, IsDeleted, InsertDate, InsertedBy, UpdateDate, UpdatedBy)
        VALUES (@Key, @Name, @Description, @TypeCode, @AccessLevel, @LeadUserId, @DepartmentId, @CreatedBy, 'Planning', 1, 0, GETDATE(), @CreatedBy, GETDATE(), @CreatedBy);

        SET @NewProjectId = CONVERT(bigint, SCOPE_IDENTITY());

        IF NOT EXISTS (SELECT 1 FROM dbo.ProjectCounters WHERE ProjectId = @NewProjectId)
            INSERT INTO dbo.ProjectCounters (ProjectId, LastIssueNumber) VALUES (@NewProjectId, 0);

        -- The lead is always a Project Admin; the creator is added too when different.
        INSERT INTO dbo.ProjectMembers (ProjectId, UserId, ProjectRoleId, AddedBy, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@NewProjectId, @LeadUserId, @AdminRoleId, @CreatedBy, 1, 0, SYSUTCDATETIME(), @CreatedBy);

        IF @CreatedBy <> @LeadUserId
            INSERT INTO dbo.ProjectMembers (ProjectId, UserId, ProjectRoleId, AddedBy, Active, IsDeleted, InsertDate, InsertedBy)
            VALUES (@NewProjectId, @CreatedBy, @AdminRoleId, @CreatedBy, 1, 0, SYSUTCDATETIME(), @CreatedBy);

        INSERT INTO dbo.AuditLog (EntityName, EntityId, [Action], NewValue, UserId, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES ('Project', @NewProjectId, 'INSERT', @Name, @CreatedBy, 1, 0, GETDATE(), @CreatedBy);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

-- ============================================================================
-- 5. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0018' AND Name = 'ProjectCreationFixes' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0018', 'ProjectCreationFixes', SYSUTCDATETIME(), 1);
END
GO
