/*
    0010_CriticalFixes.sql

    Aligns stored-procedure parameter types and column names with the C# entity
    and DTO definitions, fixes the SP_USER password-update data-loss bug, and
    adds job-queue retry support.

    Fixes applied:
    1.  Add RefreshToken.TokenHash index
    2.  Add migration tracking table (SchemaMigration)
    3.  Add expired token cleanup procedure (SP_CLEANUP_EXPIRED_TOKENS)
    4.  Enhance JobQueue with retry scheduling support (NextAvailableAt, SP_JOB_QUEUE)
    5.  Fix SP_JOB_QUEUE FAIL action to persist LastError and honor caller-supplied AvailableDate
    6.  Align column types with entity/DTO definitions (Priority int, StoryPoints decimal, CompletedDate)
    7.  Fix SP_REPORT VELOCITY (CompletedStoryPoints was always 0) and WORKLOAD (OpenIssues was always 0)
    8.  Align SP_USER UPDATE to use ISNULL for non-password fields (fixes password-update data loss)
    9.  Fix SP_TASK, SP_USER_STORY, SP_PROJECT, SP_ISSUE INSERT to include IsDeleted=0 and set UpdateDate
    10. Fix SP_TASK, SP_USER_STORY, SP_PROJECT, SP_ISSUE UPDATE to use ISNULL for nullable fields
    11. Keep SP_ATTACHMENT aligned with AttachmentRepository (@ContentType, FETCH_BY_ID)
    12. Record this migration in SchemaMigration

    IDEMPOTENT and re-runnable. Each section is in its own GO batch.
    No wrapping transaction: partial success is safe to re-run.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. Add RefreshToken.TokenHash index for performance
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshToken_TokenHash' AND object_id = OBJECT_ID('dbo.RefreshToken'))
    CREATE NONCLUSTERED INDEX IX_RefreshToken_TokenHash ON dbo.RefreshToken(TokenHash ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 2. Migration tracking table
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    CREATE TABLE dbo.SchemaMigration(
        Id bigint IDENTITY(1,1) NOT NULL,
        Version varchar(50) NOT NULL,
        Name varchar(255) NOT NULL,
        AppliedAt datetime NOT NULL DEFAULT GETDATE(),
        Success bit NOT NULL DEFAULT 1,
        ErrorMessage varchar(max) NULL,
        Active bit NOT NULL DEFAULT 1,
        IsDeleted bit NOT NULL DEFAULT 0,
        InsertDate datetime NULL DEFAULT GETDATE(),
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_schemamigration PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];

    CREATE UNIQUE NONCLUSTERED INDEX UQ_schemamigration_version_name ON dbo.SchemaMigration(Version ASC, Name ASC) WHERE IsDeleted = 0;
END
GO

-- ============================================================================
-- 3. Expired token cleanup procedure (called by background worker)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = 'SP_CLEANUP_EXPIRED_TOKENS' AND type = 'P')
BEGIN
    EXEC(N'CREATE PROCEDURE [dbo].[SP_CLEANUP_EXPIRED_TOKENS] AS BEGIN SET NOCOUNT ON; END');
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_CLEANUP_EXPIRED_TOKENS @RetentionDays int = 30
AS BEGIN SET NOCOUNT ON;
    -- Clean up revoked/expired refresh tokens older than retention period
    DELETE FROM dbo.RefreshToken
    WHERE IsDeleted = 0
    AND IsRevoked = 1
    AND ExpiryDate < DATEADD(day, -@RetentionDays, GETDATE());

    -- Also clean up very old expired (but not revoked) tokens
    DELETE FROM dbo.RefreshToken
    WHERE IsDeleted = 0
    AND IsRevoked = 0
    AND ExpiryDate < DATEADD(day, -@RetentionDays, GETDATE());

    SELECT @@ROWCOUNT AS DeletedCount;
END
GO

-- ============================================================================
-- 4. Enhance JobQueue with retry scheduling support
-- ============================================================================

-- Add computed column for next available time if not exists
IF NOT EXISTS (SELECT 1 FROM sys.computed_columns WHERE name = 'NextAvailableAt' AND object_id = OBJECT_ID('dbo.JobQueue'))
BEGIN
    ALTER TABLE dbo.JobQueue ADD NextAvailableAt AS
        CASE
            WHEN Status = 'Failed' AND AvailableDate IS NOT NULL THEN AvailableDate
            WHEN Status = 'Pending' AND AvailableDate IS NOT NULL THEN AvailableDate
            ELSE InsertDate
        END;
END
GO

-- Update SP_JOB_QUEUE to use AvailableDate and MaxAttempts
CREATE OR ALTER PROCEDURE dbo.SP_JOB_QUEUE
    @Id bigint = NULL,
    @JobType varchar(50) = NULL,
    @Payload varchar(max) = NULL,
    @Status varchar(20) = NULL,
    @Attempts int = NULL,
    @MaxAttempts int = NULL,
    @AvailableDate datetime = NULL,
    @LastError varchar(max) = NULL,
    @User bigint = NULL,
    @Action varchar(20)
AS BEGIN SET NOCOUNT ON;

    IF @Action = 'INSERT'
    BEGIN
        INSERT dbo.JobQueue(JobType, Payload, Status, Attempts, MaxAttempts, AvailableDate, LastError, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@JobType, @Payload, ISNULL(@Status, 'Pending'), ISNULL(@Attempts, 0), ISNULL(@MaxAttempts, 3), ISNULL(@AvailableDate, GETDATE()), @LastError, 1, 0, GETDATE(), @User);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @Action = 'FETCH'
    BEGIN
        SELECT TOP (1) *
        FROM dbo.JobQueue
        WHERE Id = @Id AND IsDeleted = 0;
    END
    ELSE IF @Action = 'CLAIM'
    BEGIN
        ;WITH N AS(
            SELECT TOP(1)*
            FROM dbo.JobQueue WITH(UPDLOCK, READPAST, ROWLOCK)
            WHERE IsDeleted = 0
            AND Status IN ('Pending', 'Failed')
            AND (AvailableDate IS NULL OR AvailableDate <= GETDATE())
            AND Attempts < ISNULL(MaxAttempts, 3)
            ORDER BY Id
        )
        UPDATE N
        SET Status = 'Processing',
            Attempts = Attempts + 1,
            StartDate = GETDATE(),
            UpdateDate = GETDATE()
        OUTPUT INSERTED.*;
    END
    ELSE IF @Action = 'COMPLETE'
        UPDATE dbo.JobQueue
        SET Status = 'Completed',
            ProcessedDate = GETDATE(),
            CompletionDate = GETDATE(),
            UpdateDate = GETDATE()
        WHERE Id = @Id;
    ELSE IF @Action = 'FAIL'
    BEGIN
        -- Use provided AvailableDate if given; otherwise compute exponential backoff from Attempts
        DECLARE @NextAvailable datetime = ISNULL(@AvailableDate, DATEADD(minute, POWER(2, ISNULL(@Attempts, 0)), GETDATE()));
        UPDATE dbo.JobQueue
        SET Status = 'Failed',
            AvailableDate = @NextAvailable,
            LastError = @LastError,
            UpdateDate = GETDATE()
        WHERE Id = @Id;
    END
    ELSE IF @Action = 'RETRY'
    BEGIN
        UPDATE dbo.JobQueue
        SET Status = 'Pending',
            AvailableDate = DATEADD(minute, POWER(2, ISNULL(Attempts, 0)), GETDATE()),
            UpdateDate = GETDATE()
        WHERE Id = @Id AND Status = 'Failed';
    END
END
GO

-- ============================================================================
-- 5. Align column types with entity/DTO definitions (existing databases)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Attachment') AND name = 'ProjectId')
    ALTER TABLE dbo.Attachment ADD ProjectId bigint NULL;
GO

-- 5a. Rename Task.CompletionDate -> CompletedDate to match entity property
IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'CompletionDate' AND Object_ID = OBJECT_ID('dbo.Task'))
    AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'CompletedDate' AND Object_ID = OBJECT_ID('dbo.Task'))
BEGIN
    EXEC sp_rename 'dbo.Task.CompletionDate', 'CompletedDate', 'COLUMN';
END
GO

-- 5b. Task.Priority: VARCHAR(20) -> INT (entity uses int Priority with default 3)
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Task' AND COLUMN_NAME = 'Priority' AND DATA_TYPE = 'varchar')
BEGIN
    -- Drop existing default constraint silently
    DECLARE @TaskPriorityDrop nvarchar(500);
    SELECT @TaskPriorityDrop = N'ALTER TABLE dbo.Task DROP CONSTRAINT [' + dc.name + N']'
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Task') AND c.name = 'Priority';
    IF @TaskPriorityDrop IS NOT NULL
        EXEC sp_executesql @TaskPriorityDrop;

    -- Convert varchar values to int (numeric strings -> int; non-numeric -> 3 = Medium default)
    UPDATE dbo.Task SET Priority = ISNULL(TRY_CAST(Priority AS int), 3) WHERE Priority IS NOT NULL;

    ALTER TABLE dbo.Task ALTER COLUMN Priority int NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id WHERE dc.parent_object_id = OBJECT_ID('dbo.Task') AND c.name = 'Priority')
        ALTER TABLE dbo.Task ADD CONSTRAINT DF_task_priority DEFAULT ((3)) FOR Priority;
END
GO

-- 5c. UserStory.Priority: VARCHAR(20) -> INT (entity uses int Priority with default 3)
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'UserStory' AND COLUMN_NAME = 'Priority' AND DATA_TYPE = 'varchar')
BEGIN
    -- Drop existing default constraint silently
    DECLARE @USPriorityDrop nvarchar(500);
    SELECT @USPriorityDrop = N'ALTER TABLE dbo.UserStory DROP CONSTRAINT [' + dc.name + N']'
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.UserStory') AND c.name = 'Priority';
    IF @USPriorityDrop IS NOT NULL
        EXEC sp_executesql @USPriorityDrop;

    -- Convert varchar values to int (numeric strings -> int; non-numeric -> 3 = Medium default)
    UPDATE dbo.UserStory SET Priority = ISNULL(TRY_CAST(Priority AS int), 3) WHERE Priority IS NOT NULL;

    ALTER TABLE dbo.UserStory ALTER COLUMN Priority int NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id WHERE dc.parent_object_id = OBJECT_ID('dbo.UserStory') AND c.name = 'Priority')
        ALTER TABLE dbo.UserStory ADD CONSTRAINT DF_userstory_priority DEFAULT ((3)) FOR Priority;
END
GO

-- 5d. UserStory.StoryPoints: INT -> DECIMAL(18,2) (entity uses decimal?)
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'UserStory' AND COLUMN_NAME = 'StoryPoints' AND DATA_TYPE = 'int')
BEGIN
    ALTER TABLE dbo.UserStory ALTER COLUMN StoryPoints decimal(18,2) NULL;
END
GO

-- ============================================================================
-- 6. Fix SP_REPORT VELOCITY (CompletedStoryPoints was always 0) and WORKLOAD (OpenIssues was always 0)
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_REPORT @ProjectId bigint=NULL,@From datetime=NULL,@To datetime=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='VELOCITY' BEGIN
    ;WITH CompletedRows AS (
        SELECT
            CONVERT(char(7),T.UpdateDate,120) AS Period,
            T.Id AS TaskId,
            S.Id AS StoryId,
            S.StoryPoints,
            ROW_NUMBER() OVER (PARTITION BY CONVERT(char(7),T.UpdateDate,120), S.Id ORDER BY T.Id) AS rn
        FROM dbo.Task T
        JOIN dbo.UserStory S ON S.Id=T.StoryId AND S.IsDeleted=0
        WHERE T.IsDeleted=0 AND T.Status='Done'
            AND(@From IS NULL OR T.UpdateDate>=@From)
            AND(@To IS NULL OR T.UpdateDate<@To)
            AND(@ProjectId IS NULL OR S.ProjectId=@ProjectId)
    )
    SELECT
        Period,
        COUNT(DISTINCT StoryId) AS CompletedStories,
        SUM(CASE WHEN rn=1 THEN ISNULL(StoryPoints, 0) ELSE 0 END) AS CompletedStoryPoints,
        COUNT(TaskId) AS CompletedTasks
    FROM CompletedRows
    GROUP BY Period
    ORDER BY Period;
  END
  ELSE IF @Action='WORKLOAD' BEGIN
    SELECT U.Id UserId,
        U.FullName UserName,
        COALESCE(T.OpenTasks,0) OpenTasks,
        COALESCE(I.OpenIssues,0) OpenIssues,
        COALESCE(T.EstimatedHours,0) EstimatedHours
    FROM dbo.[User] U
    OUTER APPLY(
        SELECT COUNT(1) OpenTasks,
            COALESCE(SUM(T.EstimateHours),0) EstimatedHours
        FROM dbo.Task T
        JOIN dbo.UserStory S ON S.Id=T.StoryId
        WHERE T.AssigneeUserId=U.Id
        AND T.IsDeleted=0
        AND T.Status NOT IN('Done','Cancelled')
        AND(@ProjectId IS NULL OR S.ProjectId=@ProjectId)
    ) T
    OUTER APPLY(
        SELECT COUNT(1) OpenIssues
        FROM dbo.Issue I
        WHERE I.AssigneeUserId=U.Id
        AND I.IsDeleted=0
        AND I.Status NOT IN('Resolved','Closed','Rejected')
        AND(@ProjectId IS NULL OR I.ProjectId=@ProjectId)
    ) I
    WHERE U.IsDeleted=0 AND U.Active=1
    ORDER BY U.FullName;
  END
END
GO

-- ============================================================================
-- 7. Fix SP_USER UPDATE to preserve non-password fields during password-only updates
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_USER
  @Id bigint=NULL,@Value varchar(200)=NULL,@FullName varchar(150)=NULL,@Email varchar(200)=NULL,@PasswordHash varchar(500)=NULL,@RoleId bigint=NULL,@DepartmentId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(200)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.[User] WHERE IsDeleted=0 AND ((@Id IS NOT NULL AND Id=@Id) OR (@Id IS NULL AND @Value IS NOT NULL AND (Email=@Value OR FullName=@Value)));
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.[User] WHERE IsDeleted=0 AND (@Search IS NULL OR FullName LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%'); SELECT * FROM dbo.[User] WHERE IsDeleted=0 AND (@Search IS NULL OR FullName LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.[User](FullName,Email,PasswordHash,RoleId,DepartmentId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@FullName,@Email,@PasswordHash,@RoleId,@DepartmentId,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.[User] SET FullName=ISNULL(@FullName,FullName),Email=ISNULL(@Email,Email),PasswordHash=ISNULL(@PasswordHash,PasswordHash),RoleId=ISNULL(@RoleId,RoleId),DepartmentId=ISNULL(@DepartmentId,DepartmentId),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.[User] SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='ROLES' BEGIN IF @Id IS NULL SELECT * FROM dbo.Role WHERE Active=1 AND IsDeleted=0 ORDER BY Name; ELSE SELECT R.* FROM dbo.[User] U JOIN dbo.Role R ON R.Id=U.RoleId WHERE U.Id=@Id AND U.IsDeleted=0 AND R.IsDeleted=0; END
  ELSE IF @Action='PERMISSIONS' SELECT DISTINCT P.Name FROM dbo.[User] U JOIN dbo.RolePermission RP ON RP.RoleId=U.RoleId JOIN dbo.Permission P ON P.Id=RP.PermissionId WHERE U.Id=@Id AND U.IsDeleted=0 AND RP.IsDeleted=0 AND RP.Active=1 AND P.IsDeleted=0 AND P.Active=1;
  ELSE IF @Action='SETROLE' BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.Role WHERE Id=@RoleId AND Active=1 AND IsDeleted=0) BEGIN SELECT CONVERT(bigint,0); RETURN; END UPDATE dbo.[User] SET RoleId=@RoleId,UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 8. Fix SP_TASK / SP_USER_STORY / SP_PROJECT / SP_ISSUE
--     - INSERT: add IsDeleted=0 (prevents NOT NULL violation) and set UpdateDate
--     - UPDATE: use ISNULL for nullable fields (prevents unintended nulling)
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_TASK
  @Id bigint=NULL,@StoryId bigint=NULL,@ProjectId bigint=NULL,@AssigneeUserId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@EstimateHours decimal(18,2)=NULL,@ActualHours decimal(18,2)=NULL,@Status varchar(30)=NULL,@Priority int=NULL,@DueDate date=NULL,@CompletedDate datetime=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Task WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Task WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Task WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Task(StoryId,ProjectId,AssigneeUserId,Title,Description,EstimateHours,ActualHours,Status,Priority,DueDate,CompletedDate,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@StoryId,@ProjectId,@AssigneeUserId,@Title,@Description,@EstimateHours,@ActualHours,@Status,@Priority,@DueDate,@CompletedDate,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Task SET StoryId=ISNULL(@StoryId,StoryId),ProjectId=ISNULL(@ProjectId,ProjectId),AssigneeUserId=ISNULL(@AssigneeUserId,AssigneeUserId),Title=ISNULL(@Title,Title),Description=ISNULL(@Description,Description),EstimateHours=ISNULL(@EstimateHours,EstimateHours),ActualHours=ISNULL(@ActualHours,ActualHours),Status=ISNULL(@Status,Status),Priority=ISNULL(@Priority,Priority),DueDate=ISNULL(@DueDate,DueDate),CompletedDate=ISNULL(@CompletedDate,CompletedDate),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Task SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_USER_STORY
  @Id bigint=NULL,@ProjectId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@AcceptanceCriteria varchar(max)=NULL,@Status varchar(30)=NULL,@Priority int=NULL,@StoryPoints decimal(18,2)=NULL,@AssigneeUserId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.UserStory WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.UserStory(ProjectId,Title,Description,AcceptanceCriteria,Status,Priority,StoryPoints,AssigneeUserId,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@ProjectId,@Title,@Description,@AcceptanceCriteria,@Status,@Priority,@StoryPoints,@AssigneeUserId,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.UserStory SET ProjectId=ISNULL(@ProjectId,ProjectId),Title=ISNULL(@Title,Title),Description=ISNULL(@Description,Description),AcceptanceCriteria=ISNULL(@AcceptanceCriteria,AcceptanceCriteria),Status=ISNULL(@Status,Status),Priority=ISNULL(@Priority,Priority),StoryPoints=ISNULL(@StoryPoints,StoryPoints),AssigneeUserId=ISNULL(@AssigneeUserId,AssigneeUserId),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.UserStory SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 8b. Fix SP_PROJECT INSERT to include IsDeleted = 0 (prevents NOT NULL violation)
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_PROJECT
  @Id bigint=NULL,@Name varchar(200)=NULL,@Description varchar(max)=NULL,@Key varchar(50)=NULL,@Status varchar(30)=NULL,@DepartmentId bigint=NULL,@OwnerUserId bigint=NULL,@StartDate date=NULL,@TargetDate date=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(200)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Project WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Project(Name,Description,[Key],Status,DepartmentId,OwnerUserId,StartDate,TargetDate,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@Name,@Description,@Key,@Status,@DepartmentId,@OwnerUserId,@StartDate,@TargetDate,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Project SET Name=ISNULL(@Name,Name),Description=ISNULL(@Description,Description),[Key]=ISNULL(@Key,[Key]),Status=ISNULL(@Status,Status),DepartmentId=ISNULL(@DepartmentId,DepartmentId),OwnerUserId=ISNULL(@OwnerUserId,OwnerUserId),StartDate=ISNULL(@StartDate,StartDate),TargetDate=ISNULL(@TargetDate,TargetDate),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Project SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 8c. Fix SP_ISSUE INSERT to include IsDeleted = 0 (prevents NOT NULL violation)
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_ISSUE
  @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@Severity varchar(20)=NULL,@Status varchar(30)=NULL,@ReportedByUserId bigint=NULL,@AssigneeUserId bigint=NULL,@ResolvedDate datetime=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Issue WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Issue WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Issue WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Issue(ProjectId,TaskId,Title,Description,Severity,Status,ReportedByUserId,AssigneeUserId,ResolvedDate,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@ProjectId,@TaskId,@Title,@Description,@Severity,@Status,@ReportedByUserId,@AssigneeUserId,@ResolvedDate,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Issue SET ProjectId=ISNULL(@ProjectId,ProjectId),TaskId=ISNULL(@TaskId,TaskId),Title=ISNULL(@Title,Title),Description=ISNULL(@Description,Description),Severity=ISNULL(@Severity,Severity),Status=ISNULL(@Status,Status),ReportedByUserId=ISNULL(@ReportedByUserId,ReportedByUserId),AssigneeUserId=ISNULL(@AssigneeUserId,AssigneeUserId),ResolvedDate=ISNULL(@ResolvedDate,ResolvedDate),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Issue SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 9. SP_ATTACHMENT: add FETCH_BY_ID and keep the ContentType contract
--     (0009 is the canonical definition; repeated here so 0010 never downgrades
--     a database that already has the ContentType-aware procedure)
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_ATTACHMENT
  @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@FileName varchar(255)=NULL,@FilePath varchar(500)=NULL,@ContentType varchar(255)=NULL,@FileSizeKb int=NULL,@UploadedByUserId bigint=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Attachment WHERE IsDeleted=0 AND ((@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate DESC;
  ELSE IF @Action='FETCH_BY_ID' SELECT TOP (1) * FROM dbo.Attachment WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Attachment(ProjectId,TaskId,IssueId,FileName,FilePath,ContentType,FileSizeKb,UploadedByUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@IssueId,@FileName,@FilePath,@ContentType,@FileSizeKb,@UploadedByUserId,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Attachment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 10. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0010' AND Name = 'CriticalFixes.sql' AND IsDeleted = 0)
        INSERT dbo.SchemaMigration(Version, Name, AppliedAt, Success) VALUES('0010', 'CriticalFixes.sql', GETDATE(), 1);
END
GO
