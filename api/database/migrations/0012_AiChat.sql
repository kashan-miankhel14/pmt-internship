/*
    0012_AiChat.sql

    AI Agent - Basic Chat Infrastructure

    Adds the chat session / message tables and the supporting stored
    procedures for the conversational AI feature.

    Tables:
        1. AiChatSession  - one row per conversation
        2. AiChatMessage  - one row per user/assistant/system message

    Stored procedures (usp_ prefix per the AI feature contract):
        usp_AiChatSession_Create
        usp_AiChatSession_ListByUser
        usp_AiChatSession_Delete
        usp_AiChatMessage_Create
        usp_AiChatMessage_ListBySession

    Conventions (aligned with 0009/0010/0011):
        - Idempotent: IF NOT EXISTS guards for DDL, CREATE OR ALTER for procedures.
        - Each object is created in its own GO batch so CREATE PROCEDURE is the
          first statement of its batch.
        - SET QUOTED_IDENTIFIER / ANSI_NULLS ON; NOCOUNT ON; XACT_ABORT ON.
        - Soft deletes: IsDeleted bit NOT NULL DEFAULT 0 plus the conventional
          audit columns (Active, InsertedBy, UpdatedBy, DeletedDate, DeletedBy).
        - UserId / ProjectId are bigint (NOT int as loosely sketched) so they can
          reference the existing [User].Id and Project.Id primary keys, which are
          bigint. Changing them to int would make the foreign keys uncreatable.
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
-- 1. AiChatSession
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'AiChatSession')
BEGIN
    CREATE TABLE dbo.AiChatSession(
        Id uniqueidentifier NOT NULL,
        UserId bigint NOT NULL,                 -- bigint to match [User].Id
        Title nvarchar(200) NULL,
        ProjectId bigint NULL,                  -- bigint to match Project.Id
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NOT NULL,
        Active bit NOT NULL,
        IsDeleted bit NOT NULL,
        InsertedBy bigint NULL,
        UpdatedBy bigint NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_aichatsession PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 2. AiChatMessage
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'AiChatMessage')
BEGIN
    CREATE TABLE dbo.AiChatMessage(
        Id bigint IDENTITY(1,1) NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        [Role] varchar(16) NOT NULL,
        Content nvarchar(MAX) NOT NULL,
        ContextJson nvarchar(MAX) NULL,
        TokenCount int NULL,
        LatencyMs int NULL,
        CreatedAt datetime2 NOT NULL,
        Active bit NOT NULL,
        IsDeleted bit NOT NULL,
        InsertedBy bigint NULL,
        UpdatedBy bigint NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_aichatmessage PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 3. Indexes
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiChatMessage_SessionId_CreatedAt' AND object_id = OBJECT_ID('dbo.AiChatMessage'))
    CREATE NONCLUSTERED INDEX IX_AiChatMessage_SessionId_CreatedAt ON dbo.AiChatMessage(SessionId ASC, CreatedAt ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiChatSession_UserId_UpdatedAt' AND object_id = OBJECT_ID('dbo.AiChatSession'))
    CREATE NONCLUSTERED INDEX IX_AiChatSession_UserId_UpdatedAt ON dbo.AiChatSession(UserId ASC, UpdatedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiChatSession_ProjectId' AND object_id = OBJECT_ID('dbo.AiChatSession'))
    CREATE NONCLUSTERED INDEX IX_AiChatSession_ProjectId ON dbo.AiChatSession(ProjectId ASC) WHERE ProjectId IS NOT NULL;
GO

-- ============================================================================
-- 4. Foreign keys
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_aichatsession_user')
    ALTER TABLE dbo.AiChatSession ADD CONSTRAINT FK_aichatsession_user FOREIGN KEY (UserId) REFERENCES dbo.[User](Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_aichatsession_project')
    ALTER TABLE dbo.AiChatSession ADD CONSTRAINT FK_aichatsession_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_aichatmessage_session')
    ALTER TABLE dbo.AiChatMessage ADD CONSTRAINT FK_aichatmessage_session FOREIGN KEY (SessionId) REFERENCES dbo.AiChatSession(Id);
GO

-- ============================================================================
-- 5. Soft-delete defaults (IsDeleted / Active)
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiChatSession') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiChatSession'), 'IsDeleted', 'ColumnId'))
    ALTER TABLE dbo.AiChatSession ADD CONSTRAINT DF_aichatsession_isdeleted DEFAULT ((0)) FOR IsDeleted;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiChatSession') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiChatSession'), 'Active', 'ColumnId'))
    ALTER TABLE dbo.AiChatSession ADD CONSTRAINT DF_aichatsession_active DEFAULT ((1)) FOR Active;

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiChatMessage') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiChatMessage'), 'IsDeleted', 'ColumnId'))
    ALTER TABLE dbo.AiChatMessage ADD CONSTRAINT DF_aichatmessage_isdeleted DEFAULT ((0)) FOR IsDeleted;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiChatMessage') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiChatMessage'), 'Active', 'ColumnId'))
    ALTER TABLE dbo.AiChatMessage ADD CONSTRAINT DF_aichatmessage_active DEFAULT ((1)) FOR Active;
GO

-- ============================================================================
-- 6. Stored procedures
-- ============================================================================

-- usp_AiChatSession_Create
-- Creates a new conversation. @Id is generated server side (NEWID) so callers
-- can start posting messages immediately. Returns the new session Id.
CREATE OR ALTER PROCEDURE dbo.usp_AiChatSession_Create
    @UserId bigint,
    @Title nvarchar(200) = NULL,
    @ProjectId bigint = NULL,
    @User bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF @UserId IS NULL OR @UserId <= 0
        THROW 50000, 'usp_AiChatSession_Create requires a valid @UserId.', 1;

    DECLARE @Id uniqueidentifier = NEWID();
    DECLARE @Now datetime2 = SYSUTCDATETIME();

    INSERT dbo.AiChatSession(Id, UserId, Title, ProjectId, CreatedAt, UpdatedAt, Active, IsDeleted, InsertedBy, UpdatedBy)
    VALUES (@Id, @UserId, NULLIF(LTRIM(RTRIM(@Title)), N''), @ProjectId, @Now, @Now, 1, 0, @User, @User);

    SELECT @Id AS Id;
END
GO

-- usp_AiChatSession_ListByUser
-- Returns a paged list of a user's non-deleted sessions, newest activity first,
-- with a live message count per session.
CREATE OR ALTER PROCEDURE dbo.usp_AiChatSession_ListByUser
    @UserId bigint,
    @Page int = 1,
    @PageSize int = 20
AS BEGIN
    SET NOCOUNT ON;

    IF @UserId IS NULL OR @UserId <= 0
        THROW 50000, 'usp_AiChatSession_ListByUser requires a valid @UserId.', 1;

    SET @Page = ISNULL(NULLIF(@Page, 0), 1);
    SET @PageSize = ISNULL(NULLIF(@PageSize, 0), 20);
    IF @PageSize > 100 SET @PageSize = 100;

    SELECT COUNT_BIG(1)
    FROM dbo.AiChatSession
    WHERE UserId = @UserId AND IsDeleted = 0;

    SELECT
        s.Id,
        s.UserId,
        s.Title,
        s.ProjectId,
        s.CreatedAt,
        s.UpdatedAt,
        COUNT_BIG(m.Id) AS MessageCount
    FROM dbo.AiChatSession s
    LEFT JOIN dbo.AiChatMessage m
        ON m.SessionId = s.Id AND m.IsDeleted = 0
    WHERE s.UserId = @UserId AND s.IsDeleted = 0
    GROUP BY s.Id, s.UserId, s.Title, s.ProjectId, s.CreatedAt, s.UpdatedAt
    ORDER BY s.UpdatedAt DESC
    OFFSET (@Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
GO

-- usp_AiChatSession_Delete
-- Soft-deletes a session and all of its messages.
CREATE OR ALTER PROCEDURE dbo.usp_AiChatSession_Delete
    @Id uniqueidentifier,
    @User bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF @Id IS NULL
        THROW 50000, 'usp_AiChatSession_Delete requires @Id.', 1;

    UPDATE dbo.AiChatMessage
    SET IsDeleted = 1,
        Active = 0,
        DeletedDate = SYSUTCDATETIME(),
        DeletedBy = @User,
        UpdatedBy = @User
    WHERE SessionId = @Id AND IsDeleted = 0;

    UPDATE dbo.AiChatSession
    SET IsDeleted = 1,
        Active = 0,
        DeletedDate = SYSUTCDATETIME(),
        DeletedBy = @User,
        UpdatedBy = @User
    WHERE Id = @Id AND IsDeleted = 0;

    SELECT CONVERT(bigint, @@ROWCOUNT) AS RowsAffected;
END
GO

-- usp_AiChatMessage_Create
-- Appends a message to a session and bumps the session's UpdatedAt so the
-- session list stays ordered by most-recent activity.
CREATE OR ALTER PROCEDURE dbo.usp_AiChatMessage_Create
    @SessionId uniqueidentifier,
    @Role varchar(16),
    @Content nvarchar(MAX),
    @ContextJson nvarchar(MAX) = NULL,
    @TokenCount int = NULL,
    @LatencyMs int = NULL,
    @User bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF @SessionId IS NULL
        THROW 50000, 'usp_AiChatMessage_Create requires @SessionId.', 1;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@Role, N''))), N'') IS NULL
        THROW 50000, 'usp_AiChatMessage_Create requires @Role.', 1;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@Content, N''))), N'') IS NULL
        THROW 50000, 'usp_AiChatMessage_Create requires @Content.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.AiChatSession WHERE Id = @SessionId AND IsDeleted = 0)
        THROW 50000, 'usp_AiChatMessage_Create: session not found or deleted.', 1;

    INSERT dbo.AiChatMessage(SessionId, [Role], Content, ContextJson, TokenCount, LatencyMs, CreatedAt, Active, IsDeleted, InsertedBy, UpdatedBy)
    VALUES (@SessionId, @Role, @Content, @ContextJson, @TokenCount, @LatencyMs, SYSUTCDATETIME(), 1, 0, @User, @User);

    UPDATE dbo.AiChatSession
    SET UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @User
    WHERE Id = @SessionId;

    SELECT CONVERT(bigint, SCOPE_IDENTITY()) AS Id;
END
GO

-- usp_AiChatMessage_ListBySession
-- Returns the full ordered transcript for a session (oldest first).
CREATE OR ALTER PROCEDURE dbo.usp_AiChatMessage_ListBySession
    @SessionId uniqueidentifier
AS BEGIN
    SET NOCOUNT ON;

    IF @SessionId IS NULL
        THROW 50000, 'usp_AiChatMessage_ListBySession requires @SessionId.', 1;

    SELECT
        Id,
        SessionId,
        [Role],
        Content,
        ContextJson,
        TokenCount,
        LatencyMs,
        CreatedAt
    FROM dbo.AiChatMessage
    WHERE SessionId = @SessionId AND IsDeleted = 0
    ORDER BY CreatedAt ASC, Id ASC;
END
GO

-- ============================================================================
-- 7. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0012' AND Name = 'AiChat.sql' AND IsDeleted = 0)
        INSERT dbo.SchemaMigration(Version, Name, AppliedAt, Success) VALUES('0012', 'AiChat.sql', GETDATE(), 1);
END
GO
