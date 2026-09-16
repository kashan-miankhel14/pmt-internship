/*
    0009_DeployCompleteSchema.sql

    UNIFIED DEPLOYMENT SCRIPT
    ========================
    Combines the canonical PMT-DB-OBJECTS-main (team lead's production schema)
    with improvements from migrations 0005-0008.

    This script is IDEMPOTENT - safe to run on a fresh or existing database.
    It uses IF NOT EXISTS guards for all CREATE operations and CREATE OR ALTER
    for stored procedures.

    BATCH SEPARATION:
    - Each table, index, FK, check constraint, and stored procedure is in its
      own GO batch so that CREATE PROCEDURE is always the first statement in its
      batch (which SQL Server requires).
    - Columns added by PHASE 1b must be committed by a GO before any later batch
      references them. SQL Server resolves column names of an EXISTING table at
      batch-compile time, so an ALTER TABLE ... ADD CONSTRAINT that mentions a
      column added in the same batch fails the whole batch with
      "Invalid column name". PHASES 1b/2/3/4/5 are therefore separate batches.
    - No wrapping transaction: each phase is independently idempotent, allowing
      partial re-runs. Set XACT_ABORT ON so a run-time error fails the batch.

    Run order:
    1.  Database creation (if needed)
    2.  Tables (18)
    2b. Column alignment for pre-existing databases (adds any column that the
        constraints and procedures below reference)
    2c. IsDeleted default constraints (so plain INSERTs that omit IsDeleted work)
    3.  Indexes (26)
    4.  Foreign Keys (26)
    5.  Check Constraints (11)
    6.  Seed Data (Department, Roles, Permissions)
    7.  Stored Procedures (20)
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- No wrapping transaction: each phase is independently idempotent (IF NOT EXISTS / CREATE OR ALTER).
-- This avoids holding locks across all GO batches and allows partial re-runs.

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- PHASE 1: TABLES (18)
-- ============================================================================

-- 1. Department
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Department')
BEGIN
    CREATE TABLE dbo.Department(
        Id bigint IDENTITY(1,1) NOT NULL,
        Name varchar(150) NOT NULL,
        Code varchar(50) NULL,
        Description varchar(MAX) NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_department PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 2. User
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'User')
BEGIN
    CREATE TABLE dbo.[User](
        Id bigint IDENTITY(1,1) NOT NULL,
        FullName varchar(150) NOT NULL,
        Email varchar(200) NOT NULL,
        PasswordHash varchar(500) NOT NULL,
        RoleId bigint NOT NULL,
        DepartmentId bigint NOT NULL,
        Active bit NOT NULL,
        IsLocked bit NOT NULL CONSTRAINT DF_user_islocked DEFAULT(0),
        FailedLoginAttempts int NOT NULL CONSTRAINT DF_user_failedlogins DEFAULT(0),
        LockoutEnd datetime NULL,
        LastLoginDate datetime NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_user PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 3. Role
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Role')
BEGIN
    CREATE TABLE dbo.Role(
        Id bigint IDENTITY(1,1) NOT NULL,
        Name varchar(100) NOT NULL,
        Description varchar(500) NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_role_issystem DEFAULT(0),
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_role PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 4. Permission
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Permission')
BEGIN
    CREATE TABLE dbo.Permission(
        Id bigint IDENTITY(1,1) NOT NULL,
        Name varchar(100) NOT NULL,
        Module varchar(100) NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_permission PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 5. RolePermission
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'RolePermission')
BEGIN
    CREATE TABLE dbo.RolePermission(
        Id bigint IDENTITY(1,1) NOT NULL,
        RoleId bigint NOT NULL,
        PermissionId bigint NOT NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_rolepermission PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 6. Project
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Project')
BEGIN
    CREATE TABLE dbo.Project(
        Id bigint IDENTITY(1,1) NOT NULL,
        Name varchar(200) NOT NULL,
        Description varchar(MAX) NULL,
        [Key] varchar(50) NULL,
        Status varchar(30) NOT NULL,
        DepartmentId bigint NOT NULL,
        OwnerUserId bigint NOT NULL,
        StartDate date NULL,
        TargetDate date NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_project PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 7. UserStory
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'UserStory')
BEGIN
    CREATE TABLE dbo.UserStory(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NOT NULL,
        Title varchar(250) NOT NULL,
        Description varchar(MAX) NULL,
        AcceptanceCriteria varchar(MAX) NULL,
        Status varchar(30) NOT NULL,
        Priority int NOT NULL CONSTRAINT DF_userstory_priority DEFAULT((3)),
        StoryPoints decimal(18,2) NULL,
        AssigneeUserId bigint NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_userstory PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 8. Task
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Task')
BEGIN
    CREATE TABLE dbo.Task(
        Id bigint IDENTITY(1,1) NOT NULL,
        StoryId bigint NOT NULL,
        ProjectId bigint NULL,
        AssigneeUserId bigint NULL,
        Title varchar(250) NOT NULL,
        Description varchar(MAX) NULL,
        EstimateHours decimal(18,2) NULL,
        ActualHours decimal(18,2) NULL,
        Status varchar(30) NOT NULL,
        Priority int NOT NULL CONSTRAINT DF_task_priority DEFAULT((3)),
        DueDate date NULL,
        CompletedDate datetime NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_task PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 9. Issue
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Issue')
BEGIN
    CREATE TABLE dbo.Issue(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NOT NULL,
        TaskId bigint NULL,
        Title varchar(250) NOT NULL,
        Description varchar(MAX) NULL,
        Severity varchar(20) NOT NULL,
        Status varchar(30) NOT NULL,
        ReportedByUserId bigint NOT NULL,
        AssigneeUserId bigint NULL,
        ResolvedDate datetime NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_issue PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 10. Comment
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Comment')
BEGIN
    CREATE TABLE dbo.Comment(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NULL,
        UserStoryId bigint NULL,
        TaskId bigint NULL,
        IssueId bigint NULL,
        UserId bigint NOT NULL,
        Content varchar(MAX) NOT NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_comment PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 11. Attachment
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Attachment')
BEGIN
    CREATE TABLE dbo.Attachment(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NULL,
        TaskId bigint NULL,
        IssueId bigint NULL,
        FileName varchar(255) NOT NULL,
        FilePath varchar(500) NOT NULL,
        ContentType varchar(255) NULL,
        FileSizeKb int NULL,
        UploadedByUserId bigint NOT NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_attachment PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 12. GitLink
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'GitLink')
BEGIN
    CREATE TABLE dbo.GitLink(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NULL,
        TaskId bigint NULL,
        IssueId bigint NULL,
        Provider varchar(30) NOT NULL,
        CommitSha varchar(100) NULL,
        PullRequestUrl varchar(500) NULL,
        RepositoryUrl varchar(500) NULL,
        ReferenceType varchar(50) NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_gitlink PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 13. Notification
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Notification')
BEGIN
    CREATE TABLE dbo.Notification(
        Id bigint IDENTITY(1,1) NOT NULL,
        UserId bigint NOT NULL,
        EventType varchar(50) NOT NULL,
        Title varchar(250) NULL,
        Message varchar(500) NOT NULL,
        Link varchar(500) NULL,
        IsRead bit NOT NULL,
        ReadDate datetime NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_notification PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 14. NotificationPreference
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'NotificationPreference')
BEGIN
    CREATE TABLE dbo.NotificationPreference(
        Id bigint IDENTITY(1,1) NOT NULL,
        UserId bigint NOT NULL,
        EventType varchar(50) NOT NULL,
        EmailEnabled bit NOT NULL,
        InAppEnabled bit NOT NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_notificationpreference PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 15. RefreshToken
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'RefreshToken')
BEGIN
    CREATE TABLE dbo.RefreshToken(
        Id bigint IDENTITY(1,1) NOT NULL,
        UserId bigint NOT NULL,
        TokenHash varchar(500) NOT NULL,
        ExpiryDate datetime NOT NULL,
        IsRevoked bit NOT NULL,
        JwtId varchar(100) NULL,
        ReplacedByTokenHash varchar(500) NULL,
        CreatedByIp varchar(50) NULL,
        RevokedByIp varchar(50) NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_refreshtoken PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 16. AuditLog
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'AuditLog')
BEGIN
    CREATE TABLE dbo.AuditLog(
        Id bigint IDENTITY(1,1) NOT NULL,
        EntityName varchar(100) NOT NULL,
        EntityId bigint NOT NULL,
        Action varchar(20) NOT NULL,
        OldValue varchar(MAX) NULL,
        NewValue varchar(MAX) NULL,
        UserId bigint NOT NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_auditlog PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 17. JobQueue
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'JobQueue')
BEGIN
    CREATE TABLE dbo.JobQueue(
        Id bigint IDENTITY(1,1) NOT NULL,
        JobType varchar(50) NOT NULL,
        Payload varchar(MAX) NOT NULL,
        Status varchar(20) NOT NULL,
        Attempts int NOT NULL,
        MaxAttempts int NOT NULL CONSTRAINT DF_jobqueue_maxattempts DEFAULT(3),
        AvailableDate datetime NULL,
        StartDate datetime NULL,
        CompletionDate datetime NULL,
        LastError varchar(MAX) NULL,
        ProcessedDate datetime NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_jobqueue PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- 18. IPWhitelist
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'IPWhitelist')
BEGIN
    CREATE TABLE dbo.IPWhitelist(
        Id bigint IDENTITY(1,1) NOT NULL,
        CidrRange varchar(50) NOT NULL,
        Description varchar(200) NULL,
        Active bit NOT NULL,
        InsertDate datetime NULL,
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_ipwhitelist PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- PHASE 1b: COLUMN ALIGNMENT (pre-existing databases)
-- ============================================================================
-- The CREATE TABLE statements above only run on a fresh database. A database
-- created by an earlier migration (or restored from an older backup) can be
-- missing columns that the constraints and procedures below reference, and a
-- missing column fails the whole batch at compile time. Add them here, in a
-- batch of their own, so every later batch can rely on them.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Attachment') AND name = 'ProjectId')
    ALTER TABLE dbo.Attachment ADD ProjectId bigint NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Attachment') AND name = 'ContentType')
    ALTER TABLE dbo.Attachment ADD ContentType varchar(255) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GitLink') AND name = 'ProjectId')
    ALTER TABLE dbo.GitLink ADD ProjectId bigint NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GitLink') AND name = 'RepositoryUrl')
    ALTER TABLE dbo.GitLink ADD RepositoryUrl varchar(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GitLink') AND name = 'ReferenceType')
    ALTER TABLE dbo.GitLink ADD ReferenceType varchar(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Department') AND name = 'Code')
    ALTER TABLE dbo.Department ADD Code varchar(50) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Department') AND name = 'Description')
    ALTER TABLE dbo.Department ADD Description varchar(MAX) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.[User]') AND name = 'IsLocked')
    ALTER TABLE dbo.[User] ADD IsLocked bit NOT NULL CONSTRAINT DF_user_islocked DEFAULT(0);
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.[User]') AND name = 'FailedLoginAttempts')
    ALTER TABLE dbo.[User] ADD FailedLoginAttempts int NOT NULL CONSTRAINT DF_user_failedlogins DEFAULT(0);
GO

-- ============================================================================
-- PHASE 1c: ISDELETED DEFAULTS
-- ============================================================================
-- Every dbo table carries a NOT NULL IsDeleted column. Bind DEFAULT ((0)) where
-- one is missing so an INSERT that omits IsDeleted (including SP_ADMIN's
-- bootstrap insert) cannot fail with "cannot insert NULL". Discovered from the
-- catalog views, so no table name is hardcoded; identical in effect to 0006.

DECLARE @isDeletedDefaults nvarchar(max) = N'';

SELECT @isDeletedDefaults = @isDeletedDefaults + N'
ALTER TABLE dbo.' + QUOTENAME(T.name) + N' ADD CONSTRAINT ' + QUOTENAME(N'DF_' + T.name + N'_isdeleted') + N' DEFAULT ((0)) FOR IsDeleted;'
FROM sys.tables T
INNER JOIN sys.schemas S ON S.schema_id = T.schema_id
INNER JOIN sys.columns C ON C.object_id = T.object_id
WHERE S.name = 'dbo'
  AND T.is_ms_shipped = 0
  AND C.name = 'IsDeleted'
  AND C.is_nullable = 0
  AND NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints DC
        WHERE DC.parent_object_id = T.object_id
          AND DC.parent_column_id = C.column_id);

IF @isDeletedDefaults <> N''
    EXEC sp_executesql @isDeletedDefaults;
GO

-- ============================================================================
-- PHASE 2: INDEXES (26)
-- ============================================================================

-- Attachment indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_ProjectId' AND object_id = OBJECT_ID('dbo.Attachment'))
    CREATE NONCLUSTERED INDEX IX_Attachment_ProjectId ON dbo.Attachment(ProjectId ASC) WHERE ProjectId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_TaskId' AND object_id = OBJECT_ID('dbo.Attachment'))
    CREATE NONCLUSTERED INDEX IX_Attachment_TaskId ON dbo.Attachment(TaskId ASC) WHERE TaskId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Attachment_IssueId' AND object_id = OBJECT_ID('dbo.Attachment'))
    CREATE NONCLUSTERED INDEX IX_Attachment_IssueId ON dbo.Attachment(IssueId ASC) WHERE IssueId IS NOT NULL;

-- AuditLog indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_Entity' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_Entity ON dbo.AuditLog(EntityName ASC, EntityId ASC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_UserId' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_UserId ON dbo.AuditLog(UserId ASC);

-- Comment indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Comment_TaskId' AND object_id = OBJECT_ID('dbo.Comment'))
    CREATE NONCLUSTERED INDEX IX_Comment_TaskId ON dbo.Comment(TaskId ASC) WHERE TaskId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Comment_IssueId' AND object_id = OBJECT_ID('dbo.Comment'))
    CREATE NONCLUSTERED INDEX IX_Comment_IssueId ON dbo.Comment(IssueId ASC) WHERE IssueId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Comment_UserId' AND object_id = OBJECT_ID('dbo.Comment'))
    CREATE NONCLUSTERED INDEX IX_Comment_UserId ON dbo.Comment(UserId ASC);

-- GitLink indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GitLink_ProjectId' AND object_id = OBJECT_ID('dbo.GitLink'))
    CREATE NONCLUSTERED INDEX IX_GitLink_ProjectId ON dbo.GitLink(ProjectId ASC) WHERE ProjectId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GitLink_TaskId' AND object_id = OBJECT_ID('dbo.GitLink'))
    CREATE NONCLUSTERED INDEX IX_GitLink_TaskId ON dbo.GitLink(TaskId ASC) WHERE TaskId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GitLink_IssueId' AND object_id = OBJECT_ID('dbo.GitLink'))
    CREATE NONCLUSTERED INDEX IX_GitLink_IssueId ON dbo.GitLink(IssueId ASC) WHERE IssueId IS NOT NULL;

-- Issue indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Issue_ProjectId' AND object_id = OBJECT_ID('dbo.Issue'))
    CREATE NONCLUSTERED INDEX IX_Issue_ProjectId ON dbo.Issue(ProjectId ASC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Issue_TaskId' AND object_id = OBJECT_ID('dbo.Issue'))
    CREATE NONCLUSTERED INDEX IX_Issue_TaskId ON dbo.Issue(TaskId ASC) WHERE TaskId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Issue_ReportedByUserId' AND object_id = OBJECT_ID('dbo.Issue'))
    CREATE NONCLUSTERED INDEX IX_Issue_ReportedByUserId ON dbo.Issue(ReportedByUserId ASC);

-- JobQueue index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobQueue_Status' AND object_id = OBJECT_ID('dbo.JobQueue'))
    CREATE NONCLUSTERED INDEX IX_JobQueue_Status ON dbo.JobQueue(Status ASC);

-- Notification indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notification_UserId' AND object_id = OBJECT_ID('dbo.Notification'))
    CREATE NONCLUSTERED INDEX IX_Notification_UserId ON dbo.Notification(UserId ASC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notification_UserId_IsRead' AND object_id = OBJECT_ID('dbo.Notification'))
    CREATE NONCLUSTERED INDEX IX_Notification_UserId_IsRead ON dbo.Notification(UserId ASC, IsRead ASC);

-- Project indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Project_DepartmentId' AND object_id = OBJECT_ID('dbo.Project'))
    CREATE NONCLUSTERED INDEX IX_Project_DepartmentId ON dbo.Project(DepartmentId ASC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Project_OwnerUserId' AND object_id = OBJECT_ID('dbo.Project'))
    CREATE NONCLUSTERED INDEX IX_Project_OwnerUserId ON dbo.Project(OwnerUserId ASC);

-- RefreshToken index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshToken_UserId' AND object_id = OBJECT_ID('dbo.RefreshToken'))
    CREATE NONCLUSTERED INDEX IX_RefreshToken_UserId ON dbo.RefreshToken(UserId ASC);

-- Task indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Task_StoryId' AND object_id = OBJECT_ID('dbo.Task'))
    CREATE NONCLUSTERED INDEX IX_Task_StoryId ON dbo.Task(StoryId ASC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Task_AssigneeUserId' AND object_id = OBJECT_ID('dbo.Task'))
    CREATE NONCLUSTERED INDEX IX_Task_AssigneeUserId ON dbo.Task(AssigneeUserId ASC) WHERE AssigneeUserId IS NOT NULL;

-- User indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_User_DepartmentId' AND object_id = OBJECT_ID('dbo.[User]'))
    CREATE NONCLUSTERED INDEX IX_User_DepartmentId ON dbo.[User](DepartmentId ASC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_User_RoleId' AND object_id = OBJECT_ID('dbo.[User]'))
    CREATE NONCLUSTERED INDEX IX_User_RoleId ON dbo.[User](RoleId ASC);

-- UserStory index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserStory_ProjectId' AND object_id = OBJECT_ID('dbo.UserStory'))
    CREATE NONCLUSTERED INDEX IX_UserStory_ProjectId ON dbo.UserStory(ProjectId ASC);

-- Unique indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_department_name' AND object_id = OBJECT_ID('dbo.Department'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_department_name ON dbo.Department(Name ASC) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_user_email' AND object_id = OBJECT_ID('dbo.[User]'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_user_email ON dbo.[User](Email ASC) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_role_name' AND object_id = OBJECT_ID('dbo.Role'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_role_name ON dbo.Role(Name ASC) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_permission_name' AND object_id = OBJECT_ID('dbo.Permission'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_permission_name ON dbo.Permission(Name ASC) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_rolepermission_roleid_permissionid' AND object_id = OBJECT_ID('dbo.RolePermission'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_rolepermission_roleid_permissionid ON dbo.RolePermission(RoleId ASC, PermissionId ASC) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ipwhitelist_cidrrange' AND object_id = OBJECT_ID('dbo.IPWhitelist'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_ipwhitelist_cidrrange ON dbo.IPWhitelist(CidrRange ASC) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_notificationpreference_userid_eventtype' AND object_id = OBJECT_ID('dbo.NotificationPreference'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_notificationpreference_userid_eventtype ON dbo.NotificationPreference(UserId ASC, EventType ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- PHASE 3: FOREIGN KEYS (26)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_user_department')
    ALTER TABLE dbo.[User] ADD CONSTRAINT FK_user_department FOREIGN KEY (DepartmentId) REFERENCES dbo.Department(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_user_role')
    ALTER TABLE dbo.[User] ADD CONSTRAINT FK_user_role FOREIGN KEY (RoleId) REFERENCES dbo.Role(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_project_department')
    ALTER TABLE dbo.Project ADD CONSTRAINT FK_project_department FOREIGN KEY (DepartmentId) REFERENCES dbo.Department(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_project_user')
    ALTER TABLE dbo.Project ADD CONSTRAINT FK_project_user FOREIGN KEY (OwnerUserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_userstory_project')
    ALTER TABLE dbo.UserStory ADD CONSTRAINT FK_userstory_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_task_userstory')
    ALTER TABLE dbo.Task ADD CONSTRAINT FK_task_userstory FOREIGN KEY (StoryId) REFERENCES dbo.UserStory(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_task_user')
    ALTER TABLE dbo.Task ADD CONSTRAINT FK_task_user FOREIGN KEY (AssigneeUserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_issue_project')
    ALTER TABLE dbo.Issue ADD CONSTRAINT FK_issue_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_issue_task')
    ALTER TABLE dbo.Issue ADD CONSTRAINT FK_issue_task FOREIGN KEY (TaskId) REFERENCES dbo.Task(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_issue_user')
    ALTER TABLE dbo.Issue ADD CONSTRAINT FK_issue_user FOREIGN KEY (ReportedByUserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_comment_task')
    ALTER TABLE dbo.Comment ADD CONSTRAINT FK_comment_task FOREIGN KEY (TaskId) REFERENCES dbo.Task(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_comment_issue')
    ALTER TABLE dbo.Comment ADD CONSTRAINT FK_comment_issue FOREIGN KEY (IssueId) REFERENCES dbo.Issue(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_comment_user')
    ALTER TABLE dbo.Comment ADD CONSTRAINT FK_comment_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_attachment_task')
    ALTER TABLE dbo.Attachment ADD CONSTRAINT FK_attachment_task FOREIGN KEY (TaskId) REFERENCES dbo.Task(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_attachment_issue')
    ALTER TABLE dbo.Attachment ADD CONSTRAINT FK_attachment_issue FOREIGN KEY (IssueId) REFERENCES dbo.Issue(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_attachment_user')
    ALTER TABLE dbo.Attachment ADD CONSTRAINT FK_attachment_user FOREIGN KEY (UploadedByUserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_gitlink_task')
    ALTER TABLE dbo.GitLink ADD CONSTRAINT FK_gitlink_task FOREIGN KEY (TaskId) REFERENCES dbo.Task(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_gitlink_issue')
    ALTER TABLE dbo.GitLink ADD CONSTRAINT FK_gitlink_issue FOREIGN KEY (IssueId) REFERENCES dbo.Issue(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_refreshtoken_user')
    ALTER TABLE dbo.RefreshToken ADD CONSTRAINT FK_refreshtoken_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_rolepermission_role')
    ALTER TABLE dbo.RolePermission ADD CONSTRAINT FK_rolepermission_role FOREIGN KEY (RoleId) REFERENCES dbo.Role(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_rolepermission_permission')
    ALTER TABLE dbo.RolePermission ADD CONSTRAINT FK_rolepermission_permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permission(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_notification_user')
    ALTER TABLE dbo.Notification ADD CONSTRAINT FK_notification_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_notificationpreference_user')
    ALTER TABLE dbo.NotificationPreference ADD CONSTRAINT FK_notificationpreference_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_auditlog_user')
    ALTER TABLE dbo.AuditLog ADD CONSTRAINT FK_auditlog_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);

-- Project parents for Attachment / GitLink. Both columns are nullable, so the
-- constraint only has to hold for rows that actually reference a project. They
-- are added only when no orphan row would fail validation, which keeps the
-- migration safe on databases that already carry data.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_attachment_project')
   AND NOT EXISTS (SELECT 1 FROM dbo.Attachment A WHERE A.ProjectId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.Project P WHERE P.Id = A.ProjectId))
    ALTER TABLE dbo.Attachment ADD CONSTRAINT FK_attachment_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_gitlink_project')
   AND NOT EXISTS (SELECT 1 FROM dbo.GitLink G WHERE G.ProjectId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.Project P WHERE P.Id = G.ProjectId))
    ALTER TABLE dbo.GitLink ADD CONSTRAINT FK_gitlink_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
GO

-- ============================================================================
-- PHASE 4: CHECK CONSTRAINTS (11)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_attachment_parentrequired')
    ALTER TABLE dbo.Attachment ADD CONSTRAINT CK_attachment_parentrequired CHECK (ProjectId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_auditlog_action')
    ALTER TABLE dbo.AuditLog ADD CONSTRAINT CK_auditlog_action CHECK (Action IN ('INSERT', 'UPDATE', 'DELETE'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_comment_parentrequired')
    ALTER TABLE dbo.Comment ADD CONSTRAINT CK_comment_parentrequired CHECK (ProjectId IS NOT NULL OR UserStoryId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_gitlink_parentrequired')
    ALTER TABLE dbo.GitLink ADD CONSTRAINT CK_gitlink_parentrequired CHECK (ProjectId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_gitlink_provider')
    ALTER TABLE dbo.GitLink ADD CONSTRAINT CK_gitlink_provider CHECK (Provider IN ('GitHub', 'GitLab', 'Bitbucket', 'AzureDevOps', 'Other'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_issue_severity')
    ALTER TABLE dbo.Issue ADD CONSTRAINT CK_issue_severity CHECK (Severity IN ('Low', 'Medium', 'High', 'Critical'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_issue_status')
    ALTER TABLE dbo.Issue ADD CONSTRAINT CK_issue_status CHECK (Status IN ('Open', 'InProgress', 'Resolved', 'Closed', 'Reopened', 'Rejected'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_jobqueue_status')
    ALTER TABLE dbo.JobQueue ADD CONSTRAINT CK_jobqueue_status CHECK (Status IN ('Pending', 'Processing', 'Completed', 'Failed'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_project_status')
    ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_status CHECK (Status IN ('Planning', 'Active', 'OnHold', 'Completed', 'Cancelled', 'Archived'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_task_status')
    ALTER TABLE dbo.Task ADD CONSTRAINT CK_task_status CHECK (Status IN ('ToDo', 'InProgress', 'Review', 'Done', 'Cancelled', 'Blocked'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_userstory_status')
    ALTER TABLE dbo.UserStory ADD CONSTRAINT CK_userstory_status CHECK (Status IN ('Backlog', 'Ready', 'InProgress', 'Review', 'Done', 'Cancelled'));
GO

-- ============================================================================
-- PHASE 5: SEED DATA
-- ============================================================================

-- Department
IF NOT EXISTS (SELECT 1 FROM dbo.Department WHERE Name = 'IT')
    INSERT dbo.Department(Name, Code, Description, Active, IsDeleted) VALUES('IT', 'IT', 'Information Technology', 1, 0);

-- Roles
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = 'Administrator')
    INSERT dbo.Role(Name, Description, IsSystem, Active, IsDeleted) VALUES('Administrator', 'Full system access', 1, 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = 'ProjectLead')
    INSERT dbo.Role(Name, Description, IsSystem, Active, IsDeleted) VALUES('ProjectLead', 'Manages projects and team', 1, 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = 'Developer')
    INSERT dbo.Role(Name, Description, IsSystem, Active, IsDeleted) VALUES('Developer', 'Works on stories, tasks, and issues', 1, 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE Name = 'Viewer')
    INSERT dbo.Role(Name, Description, IsSystem, Active, IsDeleted) VALUES('Viewer', 'Read-only access', 1, 1, 0);

-- Permissions (17 canonical keys)
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'departments.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('departments.view', 'Departments', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'departments.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('departments.manage', 'Departments', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'users.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('users.view', 'Users', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'users.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('users.manage', 'Users', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'projects.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('projects.view', 'Projects', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'projects.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('projects.manage', 'Projects', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'stories.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('stories.view', 'Stories', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'stories.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('stories.manage', 'Stories', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'tasks.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('tasks.view', 'Tasks', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'tasks.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('tasks.manage', 'Tasks', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'issues.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('issues.view', 'Issues', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'issues.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('issues.manage', 'Issues', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'comments.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('comments.manage', 'Comments', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'attachments.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('attachments.manage', 'Attachments', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'git.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('git.manage', 'Git', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'notifications.manage')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('notifications.manage', 'Notifications', 1, 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Name = 'reports.view')
    INSERT dbo.Permission(Name, Module, Active, IsDeleted) VALUES('reports.view', 'Reports', 1, 0);

-- Role-Permission grants
-- Administrator: all 17 permissions
DECLARE @adminRoleId bigint = (SELECT Id FROM dbo.Role WHERE Name = 'Administrator');
DECLARE @projectLeadRoleId bigint = (SELECT Id FROM dbo.Role WHERE Name = 'ProjectLead');
DECLARE @developerRoleId bigint = (SELECT Id FROM dbo.Role WHERE Name = 'Developer');
DECLARE @viewerRoleId bigint = (SELECT Id FROM dbo.Role WHERE Name = 'Viewer');

-- Administrator gets all permissions
INSERT dbo.RolePermission(RoleId, PermissionId, Active, IsDeleted)
SELECT @adminRoleId, Id, 1, 0 FROM dbo.Permission P
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission RP WHERE RP.RoleId = @adminRoleId AND RP.PermissionId = P.Id);

-- ProjectLead: all except departments.manage and users.manage
INSERT dbo.RolePermission(RoleId, PermissionId, Active, IsDeleted)
SELECT @projectLeadRoleId, Id, 1, 0 FROM dbo.Permission P
WHERE P.Name NOT IN ('departments.manage', 'users.manage')
AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission RP WHERE RP.RoleId = @projectLeadRoleId AND RP.PermissionId = P.Id);

-- Developer: delivery permissions
INSERT dbo.RolePermission(RoleId, PermissionId, Active, IsDeleted)
SELECT @developerRoleId, Id, 1, 0 FROM dbo.Permission P
WHERE P.Name IN ('projects.view', 'stories.view', 'stories.manage', 'tasks.view', 'tasks.manage', 'issues.view', 'issues.manage', 'comments.manage', 'attachments.manage', 'git.manage', 'notifications.manage')
AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission RP WHERE RP.RoleId = @developerRoleId AND RP.PermissionId = P.Id);

-- Viewer: read-only permissions
INSERT dbo.RolePermission(RoleId, PermissionId, Active, IsDeleted)
SELECT @viewerRoleId, Id, 1, 0 FROM dbo.Permission P
WHERE P.Name IN ('departments.view', 'users.view', 'projects.view', 'stories.view', 'tasks.view', 'issues.view', 'reports.view')
AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission RP WHERE RP.RoleId = @viewerRoleId AND RP.PermissionId = P.Id);
GO

-- ============================================================================
-- PHASE 6: STORED PROCEDURES (20)
-- ============================================================================

-- SP_ADMIN
-- Bootstraps the first administrator. IsDeleted is NOT NULL, so the insert
-- writes it explicitly (PHASE 1c additionally binds a DEFAULT ((0)) for callers
-- that do not); Active/IsLocked/FailedLoginAttempts are written explicitly too
-- so the row is valid regardless of which defaults a restored database carries.
CREATE OR ALTER PROCEDURE dbo.SP_ADMIN @FullName varchar(150),@Email varchar(200),@PasswordHash varchar(500),@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action<>'BOOTSTRAP' THROW 50000,'INVALID ACTION',1;
 IF EXISTS(SELECT 1 FROM dbo.[User]WHERE IsDeleted=0)THROW 50000,'Bootstrap disabled because a user exists.',1;
 DECLARE @RoleId bigint=(SELECT TOP(1)Id FROM dbo.Role WHERE Name='Administrator'AND IsDeleted=0),@DepartmentId bigint=(SELECT TOP(1)Id FROM dbo.Department WHERE Active=1 AND IsDeleted=0 ORDER BY Id);
 IF @RoleId IS NULL OR @DepartmentId IS NULL THROW 50000,'Administrator role and active department are required.',1;
 INSERT dbo.[User](FullName,Email,PasswordHash,RoleId,DepartmentId,Active,IsLocked,FailedLoginAttempts,IsDeleted,InsertDate)
 VALUES(@FullName,@Email,@PasswordHash,@RoleId,@DepartmentId,1,0,0,0,GETDATE());
 SELECT CONVERT(bigint,SCOPE_IDENTITY());
END
GO

-- SP_ATTACHMENT
-- Parameter list mirrors AttachmentRepository: ProjectId/TaskId/IssueId parents,
-- ContentType, and FETCH_BY_ID.
CREATE OR ALTER PROCEDURE dbo.SP_ATTACHMENT
 @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@FileName varchar(255)=NULL,@FilePath varchar(500)=NULL,@ContentType varchar(255)=NULL,@FileSizeKb int=NULL,@UploadedByUserId bigint=NULL,@User bigint=NULL,@Action varchar(20)
 AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Attachment WHERE IsDeleted=0 AND ((@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate DESC;
 ELSE IF @Action='FETCH_BY_ID' SELECT TOP (1) * FROM dbo.Attachment WHERE Id=@Id AND IsDeleted=0;
   ELSE IF @Action='INSERT' BEGIN INSERT dbo.Attachment(ProjectId,TaskId,IssueId,FileName,FilePath,ContentType,FileSizeKb,UploadedByUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@IssueId,@FileName,@FilePath,@ContentType,@FileSizeKb,@UploadedByUserId,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Attachment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_AUDIT_LOG
-- @Action is the dispatch verb the repositories send (INSERT/UPDATE/DELETE/FETCH);
-- @EventAction is the audited verb persisted in AuditLog.Action. The previous
-- version only wrote when @Action='INSERT', so every UPDATE and DELETE audit was
-- silently discarded. Both are normalised to the values CK_auditlog_action
-- allows, and EntityName/EntityId/payload fall back to non-null values so a row
-- is never rejected for a missing route id or controller name.
CREATE OR ALTER PROCEDURE dbo.SP_AUDIT_LOG
 @EntityName varchar(100)=NULL,@EntityId bigint=NULL,@EventAction varchar(20)=NULL,@OldValue varchar(max)=NULL,@NewValue varchar(max)=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 DECLARE @Dispatch varchar(20)=UPPER(LTRIM(RTRIM(ISNULL(@Action,''))));
 DECLARE @Entity varchar(100)=NULLIF(LTRIM(RTRIM(ISNULL(@EntityName,''))),'');
 DECLARE @Id bigint=ISNULL(@EntityId,0);

 IF @Dispatch IN ('FETCH','GET','READ','SELECT')
 BEGIN
  SELECT TOP(200) * FROM dbo.AuditLog
  WHERE IsDeleted=0
    AND (@Entity IS NULL OR EntityName=@Entity)
    AND (@EntityId IS NULL OR EntityId=@EntityId)
    AND (@UserId IS NULL OR UserId=@UserId)
  ORDER BY Id DESC;
  RETURN;
 END

 -- Persisted verb: prefer the explicit @EventAction, fall back to the dispatch verb.
 DECLARE @Verb varchar(20)=UPPER(LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@EventAction,''))),''),@Dispatch))));
 SET @Verb=CASE
   WHEN @Verb IN ('INSERT','POST','CREATE','ADD') THEN 'INSERT'
   WHEN @Verb IN ('UPDATE','PUT','PATCH','MODIFY','EDIT') THEN 'UPDATE'
   WHEN @Verb IN ('DELETE','REMOVE') THEN 'DELETE'
   ELSE NULL END;
 IF @Verb IS NULL THROW 50000,'SP_AUDIT_LOG requires an action of INSERT, UPDATE, DELETE or FETCH.',1;

 INSERT dbo.AuditLog(EntityName,EntityId,Action,OldValue,NewValue,UserId,Active,IsDeleted,InsertDate,InsertedBy)
 VALUES(ISNULL(@Entity,'Unknown'),@Id,@Verb,@OldValue,@NewValue,@UserId,1,0,GETDATE(),@UserId);
 SELECT CONVERT(bigint,SCOPE_IDENTITY());
END
GO

-- SP_COMMENT
CREATE OR ALTER PROCEDURE dbo.SP_COMMENT
 @Id bigint=NULL,@ProjectId bigint=NULL,@UserStoryId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@UserId bigint=NULL,@Content varchar(max)=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH_BY_ID' SELECT TOP (1) * FROM dbo.Comment WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='FETCH' SELECT * FROM dbo.Comment WHERE IsDeleted=0 AND (@EntityType IS NULL OR (@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='UserStory' AND UserStoryId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Comment(ProjectId,UserStoryId,TaskId,IssueId,UserId,Content,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@UserStoryId,@TaskId,@IssueId,@UserId,@Content,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Comment SET Content=@Content,UpdateDate=GETDATE(),UpdatedBy=@User WHERE Id=@Id AND IsDeleted=0 AND UserId=@UserId; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Comment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_DEPARTMENT
-- Parameter list mirrors DepartmentRepository, which sends @InsertedBy on INSERT
-- and @UpdatedBy on UPDATE (Dapper passes every property of the argument object,
-- so a missing parameter fails the call with "is not a parameter for procedure").
-- Code and Description are persisted; they are part of UpsertDepartmentRequest.
CREATE OR ALTER PROCEDURE dbo.SP_DEPARTMENT
 @Id bigint=NULL,@Name varchar(150)=NULL,@Code varchar(50)=NULL,@Description varchar(MAX)=NULL,@Active bit=NULL,@UserId bigint=NULL,@InsertedBy bigint=NULL,@UpdatedBy bigint=NULL,@Search varchar(150)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Department WHERE Id=ISNULL(@Id,Id) AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Department WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR ISNULL(Code,'') LIKE '%'+@Search+'%'); SELECT * FROM dbo.Department WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR ISNULL(Code,'') LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Department(Name,Code,Description,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@Name,@Code,@Description,ISNULL(@Active,1),0,GETDATE(),ISNULL(@InsertedBy,@UserId)); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Department SET Name=ISNULL(@Name,Name),Code=@Code,Description=@Description,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=ISNULL(@UpdatedBy,@UserId) WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Department SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=ISNULL(@UserId,@UpdatedBy) WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_GITLINK
-- Parameter list mirrors GitLinkRepository: Project/Task/Issue parents plus
-- RepositoryUrl and ReferenceType.
CREATE OR ALTER PROCEDURE dbo.SP_GITLINK
 @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@Provider varchar(30)=NULL,@CommitSha varchar(100)=NULL,@PullRequestUrl varchar(500)=NULL,@RepositoryUrl varchar(500)=NULL,@ReferenceType varchar(50)=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.GitLink WHERE IsDeleted=0 AND ((@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY Id DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.GitLink(ProjectId,TaskId,IssueId,Provider,CommitSha,PullRequestUrl,RepositoryUrl,ReferenceType,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@IssueId,@Provider,@CommitSha,@PullRequestUrl,@RepositoryUrl,@ReferenceType,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.GitLink SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_IP_WHITELIST
CREATE OR ALTER PROCEDURE dbo.SP_IP_WHITELIST @Id bigint=NULL,@CidrRange varchar(50)=NULL,@Description varchar(200)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT CidrRange FROM dbo.IPWhitelist WHERE Active=1 AND IsDeleted=0;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.IPWhitelist(CidrRange,Description,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@CidrRange,@Description,ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.IPWhitelist SET CidrRange=@CidrRange,Description=@Description,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.IPWhitelist SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
END
GO

-- SP_ISSUE
CREATE OR ALTER PROCEDURE dbo.SP_ISSUE
 @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@Severity varchar(20)=NULL,@Status varchar(30)=NULL,@ReportedByUserId bigint=NULL,@AssigneeUserId bigint=NULL,@ResolvedDate datetime=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Issue WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Issue WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Issue WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
 ELSE IF @Action='INSERT' BEGIN INSERT dbo.Issue(ProjectId,TaskId,Title,Description,Severity,Status,ReportedByUserId,AssigneeUserId,ResolvedDate,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@Title,@Description,@Severity,@Status,@ReportedByUserId,@AssigneeUserId,@ResolvedDate,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Issue SET ProjectId=@ProjectId,TaskId=@TaskId,Title=@Title,Description=@Description,Severity=@Severity,Status=@Status,ReportedByUserId=@ReportedByUserId,AssigneeUserId=@AssigneeUserId,ResolvedDate=@ResolvedDate,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Issue SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_JOB_QUEUE
CREATE OR ALTER PROCEDURE dbo.SP_JOB_QUEUE @Id bigint=NULL,@JobType varchar(50)=NULL,@Payload varchar(max)=NULL,@Status varchar(20)=NULL,@Attempts int=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='INSERT' BEGIN INSERT dbo.JobQueue(JobType,Payload,Status,Attempts,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@JobType,@Payload,ISNULL(@Status,'Pending'),ISNULL(@Attempts,0),1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='CLAIM' BEGIN ;WITH N AS(SELECT TOP(1)* FROM dbo.JobQueue WITH(UPDLOCK,READPAST,ROWLOCK) WHERE IsDeleted=0 AND Status IN('Pending','Failed') ORDER BY Id) UPDATE N SET Status='Processing',Attempts=Attempts+1,UpdateDate=GETDATE() OUTPUT INSERTED.*; END
 ELSE IF @Action='COMPLETE' UPDATE dbo.JobQueue SET Status='Completed',ProcessedDate=GETDATE(),UpdateDate=GETDATE() WHERE Id=@Id;
 ELSE IF @Action='FAIL' UPDATE dbo.JobQueue SET Status='Failed',UpdateDate=GETDATE() WHERE Id=@Id;
END
GO

-- SP_NOTIFICATION
CREATE OR ALTER PROCEDURE dbo.SP_NOTIFICATION
 @Id bigint=NULL,@UserId bigint=NULL,@EventType varchar(50)=NULL,@Title varchar(250)=NULL,@Message varchar(500)=NULL,@Link varchar(500)=NULL,@IsRead bit=NULL,@UnreadOnly bit=0,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT TOP(200)* FROM dbo.Notification WHERE UserId=@UserId AND IsDeleted=0 AND (@UnreadOnly=0 OR IsRead=0) ORDER BY InsertDate DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Notification(UserId,EventType,Title,Message,Link,IsRead,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@UserId,@EventType,@Title,@Message,@Link,ISNULL(@IsRead,0),1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='MARKREAD' BEGIN UPDATE dbo.Notification SET IsRead=1,ReadDate=GETDATE(),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND UserId=@UserId AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_NOTIFICATION_PREFERENCE
CREATE OR ALTER PROCEDURE dbo.SP_NOTIFICATION_PREFERENCE @Id bigint=NULL,@UserId bigint=NULL,@EventType varchar(50)=NULL,@EmailEnabled bit=NULL,@InAppEnabled bit=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.NotificationPreference WHERE UserId=@UserId AND IsDeleted=0;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.NotificationPreference(UserId,EventType,EmailEnabled,InAppEnabled,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@UserId,@EventType,ISNULL(@EmailEnabled,1),ISNULL(@InAppEnabled,1),1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.NotificationPreference SET EmailEnabled=ISNULL(@EmailEnabled,EmailEnabled),InAppEnabled=ISNULL(@InAppEnabled,InAppEnabled),UpdateDate=GETDATE(),UpdatedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_PERMISSION
CREATE OR ALTER PROCEDURE dbo.SP_PERMISSION @Id bigint=NULL,@Name varchar(100)=NULL,@Module varchar(100)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Permission WHERE IsDeleted=0 AND (@Id IS NULL OR Id=@Id);
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Permission(Name,Module,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@Name,@Module,ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Permission SET Name=@Name,Module=@Module,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
END
GO

-- SP_PROJECT
CREATE OR ALTER PROCEDURE dbo.SP_PROJECT
 @Id bigint=NULL,@Name varchar(200)=NULL,@Description varchar(max)=NULL,@Key varchar(50)=NULL,@Status varchar(30)=NULL,@DepartmentId bigint=NULL,@OwnerUserId bigint=NULL,@StartDate date=NULL,@TargetDate date=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(200)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Project WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
 ELSE IF @Action='INSERT' BEGIN INSERT dbo.Project(Name,Description,[Key],Status,DepartmentId,OwnerUserId,StartDate,TargetDate,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@Name,@Description,@Key,@Status,@DepartmentId,@OwnerUserId,@StartDate,@TargetDate,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Project SET Name=@Name,Description=@Description,[Key]=@Key,Status=@Status,DepartmentId=@DepartmentId,OwnerUserId=@OwnerUserId,StartDate=@StartDate,TargetDate=@TargetDate,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Project SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_REFRESH_TOKEN
CREATE OR ALTER PROCEDURE dbo.SP_REFRESH_TOKEN
    @Id bigint = NULL,@UserId bigint = NULL,@TokenHash varchar(500) = NULL,@ExpiryDate datetime = NULL,
    @JwtId varchar(100) = NULL,@ReplacedByTokenHash varchar(500) = NULL,@CreatedByIp varchar(50) = NULL,@RevokedByIp varchar(50) = NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.RefreshToken WHERE TokenHash=@TokenHash AND IsDeleted=0;
 ELSE IF @Action='INSERT' INSERT dbo.RefreshToken(UserId,TokenHash,ExpiryDate,JwtId,CreatedByIp,IsRevoked,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@UserId,@TokenHash,@ExpiryDate,@JwtId,@CreatedByIp,0,1,0,GETDATE(),@UserId);
 ELSE IF @Action='UPDATE' UPDATE dbo.RefreshToken SET IsRevoked=1,Active=0,ReplacedByTokenHash=@ReplacedByTokenHash,RevokedByIp=@RevokedByIp,UpdateDate=GETDATE() WHERE Id=@Id;
 ELSE IF @Action='REVOKEALL' UPDATE dbo.RefreshToken SET IsRevoked=1,Active=0,RevokedByIp=@RevokedByIp,UpdateDate=GETDATE() WHERE UserId=@UserId AND IsRevoked=0 AND IsDeleted=0;
 ELSE IF @Action='CLEANUP' DELETE FROM dbo.RefreshToken WHERE ExpiryDate < DATEADD(day, -30, GETDATE()) AND IsRevoked=1;
END
GO

-- SP_REPORT
CREATE OR ALTER PROCEDURE dbo.SP_REPORT @ProjectId bigint=NULL,@From datetime=NULL,@To datetime=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='VELOCITY' BEGIN SELECT CONVERT(char(7),T.UpdateDate,120) Period,COUNT(DISTINCT S.Id) CompletedStories,CAST(0 AS decimal(18,2))CompletedStoryPoints,COUNT(T.Id)CompletedTasks FROM dbo.Task T JOIN dbo.UserStory S ON S.Id=T.StoryId AND S.IsDeleted=0 WHERE T.IsDeleted=0 AND T.Status='Done' AND(@From IS NULL OR T.UpdateDate>=@From) AND(@To IS NULL OR T.UpdateDate<@To) AND(@ProjectId IS NULL OR S.ProjectId=@ProjectId)GROUP BY CONVERT(char(7),T.UpdateDate,120)ORDER BY Period; END
  ELSE IF @Action='WORKLOAD' BEGIN SELECT U.Id UserId,U.FullName UserName,COALESCE(T.OpenTasks,0)OpenTasks,CAST(0 AS int)OpenIssues,COALESCE(T.EstimatedHours,0)EstimatedHours FROM dbo.[User] U OUTER APPLY(SELECT COUNT(1)OpenTasks,COALESCE(SUM(T.EstimateHours),0)EstimatedHours FROM dbo.Task T JOIN dbo.UserStory S ON S.Id=T.StoryId WHERE T.AssigneeUserId=U.Id AND T.IsDeleted=0 AND T.Status NOT IN('Done','Cancelled') AND(@ProjectId IS NULL OR S.ProjectId=@ProjectId))T WHERE U.IsDeleted=0 AND U.Active=1 ORDER BY U.FullName; END
END
GO

-- SP_ROLE
CREATE OR ALTER PROCEDURE dbo.SP_ROLE @Id bigint=NULL,@Name varchar(100)=NULL,@Description varchar(500)=NULL,@IsSystem bit=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Role WHERE IsDeleted=0 AND (@Id IS NULL OR Id=@Id);
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Role(Name,Description,IsSystem,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@Name,@Description,ISNULL(@IsSystem,0),ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Role SET Name=@Name,Description=@Description,IsSystem=ISNULL(@IsSystem,IsSystem),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
END
GO

-- SP_ROLE_PERMISSION
CREATE OR ALTER PROCEDURE dbo.SP_ROLE_PERMISSION @Id bigint=NULL,@RoleId bigint=NULL,@PermissionId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.RolePermission WHERE IsDeleted=0 AND (@RoleId IS NULL OR RoleId=@RoleId);
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.RolePermission(RoleId,PermissionId,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@RoleId,@PermissionId,ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.RolePermission SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
END
GO

-- SP_TASK
CREATE OR ALTER PROCEDURE dbo.SP_TASK
  @Id bigint=NULL,@StoryId bigint=NULL,@ProjectId bigint=NULL,@AssigneeUserId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@EstimateHours decimal(18,2)=NULL,@ActualHours decimal(18,2)=NULL,@Status varchar(30)=NULL,@Priority int=NULL,@DueDate date=NULL,@CompletedDate datetime=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Task WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Task WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Task WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Task(StoryId,ProjectId,AssigneeUserId,Title,Description,EstimateHours,ActualHours,Status,Priority,DueDate,CompletedDate,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@StoryId,@ProjectId,@AssigneeUserId,@Title,@Description,@EstimateHours,@ActualHours,@Status,@Priority,@DueDate,@CompletedDate,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Task SET StoryId=@StoryId,ProjectId=@ProjectId,AssigneeUserId=@AssigneeUserId,Title=@Title,Description=@Description,EstimateHours=@EstimateHours,ActualHours=@ActualHours,Status=@Status,Priority=@Priority,DueDate=@DueDate,CompletedDate=@CompletedDate,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Task SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- SP_USER
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

-- SP_USER_STORY
CREATE OR ALTER PROCEDURE dbo.SP_USER_STORY
  @Id bigint=NULL,@ProjectId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@AcceptanceCriteria varchar(max)=NULL,@Status varchar(30)=NULL,@Priority int=NULL,@StoryPoints decimal(18,2)=NULL,@AssigneeUserId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.UserStory WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
 ELSE IF @Action='INSERT' BEGIN INSERT dbo.UserStory(ProjectId,Title,Description,AcceptanceCriteria,Status,Priority,StoryPoints,AssigneeUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@Title,@Description,@AcceptanceCriteria,@Status,@Priority,@StoryPoints,@AssigneeUserId,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.UserStory SET ProjectId=@ProjectId,Title=@Title,Description=@Description,AcceptanceCriteria=@AcceptanceCriteria,Status=@Status,Priority=@Priority,StoryPoints=@StoryPoints,AssigneeUserId=@AssigneeUserId,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.UserStory SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO
