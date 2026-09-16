/*
    0013_AiAgent.sql

    AI Agent - RAG pipeline and agent infrastructure

    Adds the document-chunk (knowledge) store and the agent tool-call audit
    tables, plus the stored procedures that drive RAG ingestion and retrieval
    and agent tool observability.

    Tables:
        1. AiDocumentChunk   - one row per indexed chunk of a source entity
        2. AiAgentToolCall   - one row per tool invocation made by the agent

    Stored procedures (usp_ prefix per the AI feature contract):
        usp_AiDocumentChunk_Upsert
        usp_AiDocumentChunk_Delete
        usp_AiDocumentChunk_Search        (vector similarity + keyword hybrid)
        usp_AiDocumentChunk_SourceSnapshot
        usp_AiAgentToolCall_Create
        usp_AiAgentToolCall_ListBySession

    Vector similarity
    -----------------
    Embeddings are persisted as VARBINARY(MAX) little-endian float32 vectors
    (the wire format returned by OpenAI / Azure OpenAI embeddings). The cosine
    similarity used by usp_AiDocumentChunk_Search is computed entirely in T-SQL
    via the helper inline TVF dbo.fn_AiDecodeVector, which decodes IEEE-754
    single-precision floats from the raw bytes using integer bit math. This
    keeps the migration dependency-free (no CLR, no SQL Server 2025 VECTOR type)
    and runnable on existing Azure SQL / SQL Server instances.

    Conventions (aligned with 0009-0012):
        - Idempotent: IF NOT EXISTS guards for DDL, CREATE OR ALTER for procedures.
        - Each object in its own GO batch; CREATE PROCEDURE first in its batch.
        - SET QUOTED_IDENTIFIER / ANSI_NULLS ON; NOCOUNT ON; XACT_ABORT ON.
        - Soft deletes + conventional audit columns.
        - ProjectId is bigint (NOT int) to match Project.Id; see 0012 note.
        - Records this migration in dbo.SchemaMigration.

    Must run after 0012_AiChat.sql (owns AiChatSession for the tool-call FK).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. AiDocumentChunk
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'AiDocumentChunk')
BEGIN
    CREATE TABLE dbo.AiDocumentChunk(
        Id bigint IDENTITY(1,1) NOT NULL,
        EntityType varchar(30) NOT NULL,
        EntityId int NOT NULL,
        ProjectId bigint NULL,                  -- bigint to match Project.Id
        Title nvarchar(300) NULL,
        Content nvarchar(MAX) NOT NULL,
        SearchText nvarchar(4000) NULL,
        Embedding varbinary(MAX) NULL,
        SourceUpdatedAt datetime2 NOT NULL,
        IndexedAt datetime2 NOT NULL,
        Active bit NOT NULL,
        IsDeleted bit NOT NULL,
        InsertedBy bigint NULL,
        UpdatedBy bigint NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_aidocumentchunk PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 2. AiAgentToolCall
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'AiAgentToolCall')
BEGIN
    CREATE TABLE dbo.AiAgentToolCall(
        Id bigint IDENTITY(1,1) NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        ToolName varchar(100) NOT NULL,
        ArgumentsJson nvarchar(MAX) NULL,
        ResultJson nvarchar(MAX) NULL,
        StartedAt datetime2 NOT NULL,
        CompletedAt datetime2 NULL,
        [Status] varchar(20) NOT NULL,
        Active bit NOT NULL,
        IsDeleted bit NOT NULL,
        InsertedBy bigint NULL,
        UpdatedBy bigint NULL,
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_aiagenttoolcall PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

-- ============================================================================
-- 3. Indexes
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_aidocumentchunk_entity' AND object_id = OBJECT_ID('dbo.AiDocumentChunk'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_aidocumentchunk_entity ON dbo.AiDocumentChunk(EntityType ASC, EntityId ASC) WHERE IsDeleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiDocumentChunk_ProjectId' AND object_id = OBJECT_ID('dbo.AiDocumentChunk'))
    CREATE NONCLUSTERED INDEX IX_AiDocumentChunk_ProjectId ON dbo.AiDocumentChunk(ProjectId ASC) WHERE ProjectId IS NOT NULL;

-- Do not create a normal index on SearchText: NVARCHAR(4000) can exceed SQL
-- Server's 1,700-byte nonclustered key limit. Retrieval uses vector similarity
-- and the bounded TOP query; full-text indexing can be added separately later.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiDocumentChunk_SearchText' AND object_id = OBJECT_ID('dbo.AiDocumentChunk'))
    DROP INDEX IX_AiDocumentChunk_SearchText ON dbo.AiDocumentChunk;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiAgentToolCall_SessionId' AND object_id = OBJECT_ID('dbo.AiAgentToolCall'))
    CREATE NONCLUSTERED INDEX IX_AiAgentToolCall_SessionId ON dbo.AiAgentToolCall(SessionId ASC);
GO

-- ============================================================================
-- 4. Foreign keys
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_aidocumentchunk_project')
    ALTER TABLE dbo.AiDocumentChunk ADD CONSTRAINT FK_aidocumentchunk_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_aiagenttoolcall_session')
    ALTER TABLE dbo.AiAgentToolCall ADD CONSTRAINT FK_aiagenttoolcall_session FOREIGN KEY (SessionId) REFERENCES dbo.AiChatSession(Id);
GO

-- ============================================================================
-- 5. Soft-delete defaults
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiDocumentChunk') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiDocumentChunk'), 'IsDeleted', 'ColumnId'))
    ALTER TABLE dbo.AiDocumentChunk ADD CONSTRAINT DF_aidocumentchunk_isdeleted DEFAULT ((0)) FOR IsDeleted;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiDocumentChunk') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiDocumentChunk'), 'Active', 'ColumnId'))
    ALTER TABLE dbo.AiDocumentChunk ADD CONSTRAINT DF_aidocumentchunk_active DEFAULT ((1)) FOR Active;

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiAgentToolCall') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiAgentToolCall'), 'IsDeleted', 'ColumnId'))
    ALTER TABLE dbo.AiAgentToolCall ADD CONSTRAINT DF_aiagenttoolcall_isdeleted DEFAULT ((0)) FOR IsDeleted;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.AiAgentToolCall') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.AiAgentToolCall'), 'Active', 'ColumnId'))
    ALTER TABLE dbo.AiAgentToolCall ADD CONSTRAINT DF_aiagenttoolcall_active DEFAULT ((1)) FOR Active;
GO

-- ============================================================================
-- 6. Vector decode helper (IEEE-754 float32 -> (Idx, Val))
-- ============================================================================
-- Decodes a little-endian float32 VARBINARY vector into a table of
-- (Idx, Val) rows using integer bit math only. Used by the similarity search.
-- 4000 rows supports vectors up to 16000 bytes (4000 dims @ 4 bytes).

IF OBJECT_ID('dbo.fn_AiDecodeVector', 'IF') IS NULL
    EXEC(N'CREATE FUNCTION dbo.fn_AiDecodeVector(@Embedding varbinary(max)) RETURNS TABLE AS RETURN (SELECT 1 AS placeholder WHERE 1=0);');
GO

CREATE OR ALTER FUNCTION dbo.fn_AiDecodeVector(@Embedding varbinary(max))
RETURNS TABLE
AS
RETURN (
    WITH Num AS (
        SELECT n = ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1
        FROM (VALUES (0),(0),(0),(0),(0),(0),(0),(0),(0),(0),
                     (0),(0),(0),(0),(0),(0),(0),(0),(0),(0)) a(x)
        CROSS JOIN (VALUES (0),(0),(0),(0),(0),(0),(0),(0),(0),(0),
                          (0),(0),(0),(0),(0),(0),(0),(0),(0),(0)) b(x)
        CROSS JOIN (VALUES (0),(0),(0),(0),(0),(0),(0),(0),(0),(0),
                          (0),(0),(0),(0),(0),(0),(0),(0),(0),(0)) c(x)
        CROSS JOIN (VALUES (0),(0),(0),(0),(0),(0),(0),(0),(0),(0)) d(x)
    ),
    W AS (
        SELECT
            Num.n AS Idx,
            CAST(CAST(SUBSTRING(@Embedding, Num.n * 4 + 1, 4) AS int) AS bigint) AS w
        FROM Num
        WHERE Num.n * 4 + 1 <= DATALENGTH(@Embedding)
    )
    SELECT
        w.Idx,
        CAST(
            CASE
                WHEN (w.w & CONVERT(bigint, 2139095040)) / CONVERT(bigint, 8388608) = CONVERT(bigint, 255) THEN 0.0   -- exp == 255: Inf / NaN -> treat as 0
                ELSE CASE WHEN (w.w & CONVERT(bigint, 2147483648)) <> CONVERT(bigint, 0) THEN -1.0 ELSE 1.0 END
                   * POWER(2.0, ((w.w & CONVERT(bigint, 2139095040)) / CONVERT(bigint, 8388608)) - CONVERT(bigint, 127))
                   * (1.0 + (w.w & CONVERT(bigint, 8388607)) / 8388608.0)
            END
        AS float) AS Val
    FROM W
);
GO

-- ============================================================================
-- 7. Stored procedures
-- ============================================================================

-- usp_AiDocumentChunk_Upsert
-- Inserts a chunk or, when (EntityType, EntityId) already exists (and is not
-- deleted), updates its content / embedding / timestamps. One chunk per source
-- entity is the contract for the unique index; callers pass a pre-aggregated
-- chunk. Returns the chunk Id.
CREATE OR ALTER PROCEDURE dbo.usp_AiDocumentChunk_Upsert
    @EntityType varchar(30),
    @EntityId int,
    @ProjectId bigint = NULL,
    @Title nvarchar(300) = NULL,
    @Content nvarchar(MAX),
    @SearchText nvarchar(4000) = NULL,
    @Embedding varbinary(MAX) = NULL,
    @SourceUpdatedAt datetime2 = NULL,
    @User bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@EntityType, N''))), N'') IS NULL
        THROW 50000, 'usp_AiDocumentChunk_Upsert requires @EntityType.', 1;
    IF @EntityId IS NULL OR @EntityId <= 0
        THROW 50000, 'usp_AiDocumentChunk_Upsert requires a valid @EntityId.', 1;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@Content, N''))), N'') IS NULL
        THROW 50000, 'usp_AiDocumentChunk_Upsert requires @Content.', 1;

    DECLARE @Now datetime2 = SYSUTCDATETIME();
    DECLARE @Src datetime2 = ISNULL(@SourceUpdatedAt, @Now);
    DECLARE @Id bigint = NULL;

    -- Reactivate a previously soft-deleted chunk (unique index is filtered on IsDeleted = 0).
    UPDATE dbo.AiDocumentChunk
    SET IsDeleted = 0,
        Active = 1,
        ProjectId = ISNULL(@ProjectId, ProjectId),
        Title = NULLIF(LTRIM(RTRIM(@Title)), N''),
        Content = @Content,
        SearchText = NULLIF(LTRIM(RTRIM(@SearchText)), N''),
        Embedding = @Embedding,
        SourceUpdatedAt = @Src,
        IndexedAt = @Now,
        UpdatedBy = @User
    WHERE EntityType = @EntityType AND EntityId = @EntityId AND IsDeleted = 1;

    SELECT @Id = Id
    FROM dbo.AiDocumentChunk
    WHERE EntityType = @EntityType AND EntityId = @EntityId AND IsDeleted = 0;

    IF @Id IS NULL
    BEGIN
        INSERT dbo.AiDocumentChunk(
            EntityType, EntityId, ProjectId, Title, Content, SearchText, Embedding,
            SourceUpdatedAt, IndexedAt, Active, IsDeleted, InsertedBy, UpdatedBy)
        VALUES (
            @EntityType, @EntityId, @ProjectId,
            NULLIF(LTRIM(RTRIM(@Title)), N''),
            @Content,
            NULLIF(LTRIM(RTRIM(@SearchText)), N''),
            @Embedding,
            @Src, @Now, 1, 0, @User, @User);

        SET @Id = CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.AiDocumentChunk
        SET ProjectId = ISNULL(@ProjectId, ProjectId),
            Title = NULLIF(LTRIM(RTRIM(@Title)), N''),
            Content = @Content,
            SearchText = NULLIF(LTRIM(RTRIM(@SearchText)), N''),
            Embedding = @Embedding,
            SourceUpdatedAt = @Src,
            IndexedAt = @Now,
            UpdatedBy = @User
        WHERE Id = @Id AND IsDeleted = 0;
    END

    SELECT @Id AS Id;
END
GO

-- usp_AiDocumentChunk_Delete
-- Soft-deletes a chunk by Id, or by (EntityType, EntityId) when @Id is omitted.
CREATE OR ALTER PROCEDURE dbo.usp_AiDocumentChunk_Delete
    @Id bigint = NULL,
    @EntityType varchar(30) = NULL,
    @EntityId int = NULL,
    @User bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF @Id IS NULL AND (@EntityType IS NULL OR @EntityId IS NULL)
        THROW 50000, 'usp_AiDocumentChunk_Delete requires @Id or both @EntityType and @EntityId.', 1;

    UPDATE dbo.AiDocumentChunk
    SET IsDeleted = 1,
        Active = 0,
        DeletedDate = SYSUTCDATETIME(),
        DeletedBy = @User,
        UpdatedBy = @User
    WHERE IsDeleted = 0
      AND ((@Id IS NOT NULL AND Id = @Id)
           OR (@Id IS NULL AND EntityType = @EntityType AND EntityId = @EntityId));

    SELECT CONVERT(bigint, @@ROWCOUNT) AS RowsAffected;
END
GO

-- usp_AiDocumentChunk_Search
-- Hybrid RAG retrieval:
--   * Optional keyword filter on SearchText (LIKE).
--   * Optional cosine-similarity ranking over @QueryEmbedding using
--     dbo.fn_AiDecodeVector. Returns the @TopN most similar non-deleted chunks
--     for the given EntityType / ProjectId scope, ordered by similarity desc.
-- At least one of @QueryEmbedding / @QueryText must be supplied.
CREATE OR ALTER PROCEDURE dbo.usp_AiDocumentChunk_Search
    @QueryEmbedding varbinary(MAX) = NULL,
    @QueryText nvarchar(4000) = NULL,
    @EntityType varchar(30) = NULL,
    @ProjectId bigint = NULL,
    @TopN int = 5,
    @MinScore float = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF @QueryEmbedding IS NULL
       AND (NULLIF(LTRIM(RTRIM(ISNULL(@QueryText, N''))), N'') IS NULL)
        THROW 50000, 'usp_AiDocumentChunk_Search requires @QueryEmbedding or @QueryText.', 1;

    DECLARE @n int = ISNULL(NULLIF(@TopN, 0), 5);
    IF @n > 100 SET @n = 100;

    -- Decode the query vector once.
    DECLARE @Q TABLE (Idx int PRIMARY KEY, Val float);
    INSERT INTO @Q (Idx, Val)
    SELECT Idx, Val FROM dbo.fn_AiDecodeVector(@QueryEmbedding);

    DECLARE @qnorm float = (SELECT SQRT(SUM(Val * Val)) FROM @Q);
    IF @qnorm IS NULL OR @qnorm = 0 SET @qnorm = NULL;

    SELECT TOP (@n)
        c.Id,
        c.EntityType,
        c.EntityId,
        c.ProjectId,
        c.Title,
        c.Content,
        c.SearchText,
        c.IndexedAt,
        ISNULL(s.Similarity, 0.0) AS Similarity,
        CASE WHEN @QueryText IS NOT NULL
                  AND c.SearchText LIKE N'%' + @QueryText + N'%' THEN 1 ELSE 0 END AS TextMatch
    FROM dbo.AiDocumentChunk c
    OUTER APPLY (
        SELECT SUM(q.Val * v.Val) / NULLIF(@qnorm * SQRT(SUM(v.Val * v.Val)), 0) AS Similarity
        FROM @Q q
        JOIN dbo.fn_AiDecodeVector(c.Embedding) v ON v.Idx = q.Idx
    ) s
    WHERE c.IsDeleted = 0
      AND (@EntityType IS NULL OR c.EntityType = @EntityType)
      AND (@ProjectId IS NULL OR c.ProjectId = @ProjectId)
      AND (
            @QueryText IS NULL
            OR c.SearchText LIKE N'%' + @QueryText + N'%'
            OR s.Similarity IS NOT NULL
          )
      AND (@MinScore IS NULL OR ISNULL(s.Similarity, 0) >= @MinScore)
    ORDER BY ISNULL(s.Similarity, 0) DESC, c.Id ASC;
END
GO

-- usp_AiDocumentChunk_SourceSnapshot
-- Returns the current (Id, EntityId, SourceUpdatedAt, IndexedAt) rows for a
-- source so the indexer can diff against upstream data and skip unchanged or
-- detect deleted sources.
CREATE OR ALTER PROCEDURE dbo.usp_AiDocumentChunk_SourceSnapshot
    @EntityType varchar(30),
    @EntityId int = NULL,
    @ProjectId bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(ISNULL(@EntityType, N''))), N'') IS NULL
        THROW 50000, 'usp_AiDocumentChunk_SourceSnapshot requires @EntityType.', 1;

    SELECT
        Id,
        EntityType,
        EntityId,
        ProjectId,
        SourceUpdatedAt,
        IndexedAt
    FROM dbo.AiDocumentChunk
    WHERE IsDeleted = 0
      AND EntityType = @EntityType
      AND (@EntityId IS NULL OR EntityId = @EntityId)
      AND (@ProjectId IS NULL OR ProjectId = @ProjectId)
    ORDER BY EntityId ASC, Id ASC;
END
GO

-- usp_AiAgentToolCall_Create
-- Records the start of an agent tool invocation. Returns the new Id so the
-- caller can later complete it via a direct UPDATE, or extend this procedure
-- with a COMPLETE action if preferred.
CREATE OR ALTER PROCEDURE dbo.usp_AiAgentToolCall_Create
    @SessionId uniqueidentifier,
    @ToolName varchar(100),
    @ArgumentsJson nvarchar(MAX) = NULL,
    @Status varchar(20) = 'Running',
    @User bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    IF @SessionId IS NULL
        THROW 50000, 'usp_AiAgentToolCall_Create requires @SessionId.', 1;
    IF NULLIF(LTRIM(RTRIM(ISNULL(@ToolName, N''))), N'') IS NULL
        THROW 50000, 'usp_AiAgentToolCall_Create requires @ToolName.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.AiChatSession WHERE Id = @SessionId AND IsDeleted = 0)
        THROW 50000, 'usp_AiAgentToolCall_Create: session not found or deleted.', 1;

    INSERT dbo.AiAgentToolCall(
        SessionId, ToolName, ArgumentsJson, [Status], StartedAt, Active, IsDeleted, InsertedBy, UpdatedBy)
    VALUES (
        @SessionId, @ToolName, @ArgumentsJson, ISNULL(NULLIF(LTRIM(RTRIM(@Status)), N''), 'Running'),
        SYSUTCDATETIME(), 1, 0, @User, @User);

    SELECT CONVERT(bigint, SCOPE_IDENTITY()) AS Id;
END
GO

-- usp_AiAgentToolCall_ListBySession
-- Returns the ordered tool-call audit trail for a session.
CREATE OR ALTER PROCEDURE dbo.usp_AiAgentToolCall_ListBySession
    @SessionId uniqueidentifier
AS BEGIN
    SET NOCOUNT ON;

    IF @SessionId IS NULL
        THROW 50000, 'usp_AiAgentToolCall_ListBySession requires @SessionId.', 1;

    SELECT
        Id,
        SessionId,
        ToolName,
        ArgumentsJson,
        ResultJson,
        StartedAt,
        CompletedAt,
        [Status]
    FROM dbo.AiAgentToolCall
    WHERE SessionId = @SessionId AND IsDeleted = 0
    ORDER BY StartedAt ASC, Id ASC;
END
GO

-- ============================================================================
-- 8. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0013' AND Name = 'AiAgent.sql' AND IsDeleted = 0)
        INSERT dbo.SchemaMigration(Version, Name, AppliedAt, Success) VALUES('0013', 'AiAgent.sql', GETDATE(), 1);
END
GO
