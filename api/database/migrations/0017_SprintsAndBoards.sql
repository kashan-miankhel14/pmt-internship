/*
    0017_SprintsAndBoards.sql

    Adds the two pieces of project configuration the Jira-flow templates already
    advertise but never created:

        1. dbo.Sprint       - the sprint container SCRUM projects need for sprint
                              planning, burndown and velocity. Nothing existed before
                              this migration, so "sprint planning" was a claim with no
                              table behind it.
        2. dbo.BoardColumns - per-project board column configuration. The board columns
                              were hardcoded in the frontend, so a project template could
                              not actually shape its own board.

    Plus:
        - dbo.UserStory.SprintId (nullable, deliberately un-enforced) so a story can be
          pulled into a sprint. Tasks stay under their story and are not sprint-scoped.
        - Board column seed data derived from the existing dbo.Project.TypeCode, applied
          the way the template would have applied it at creation time.
        - Stored procedures: SP_SPRINT, usp_Sprint_Complete, SP_BOARD_COLUMN, and a
          CREATE OR ALTER of SP_USER_STORY that threads @SprintId through INSERT/UPDATE.

    Board column templates (mirrors the template contract):
        SCRUM  -> To Do, In Progress, Done
        BASIC  -> To Do, In Progress, Done
        KANBAN -> Backlog, Selected, In Progress, Review, Done
    The final column of each template is flagged CompleteColumn = 1.

    Conventions (aligned with 0009-0016):
        - Idempotent: IF NOT EXISTS guards for DDL, CREATE OR ALTER for procedures, and a
          NOT EXISTS anti-join for the seed. Re-runnable with no wrapping transaction.
        - Each object is created in its own GO batch so CREATE PROCEDURE is the first
          statement of its batch.
        - SET QUOTED_IDENTIFIER / ANSI_NULLS ON; NOCOUNT ON; XACT_ABORT ON.
        - BIGINT IDENTITY(1,1) PKs, dbo schema.
        - Full audit tail on every new table:
          Active, InsertDate, InsertedBy, UpdateDate, UpdatedBy,
          IsDeleted, DeletedDate, DeletedBy.
        - Soft delete: procedures filter on IsDeleted = 0 and uniqueness is enforced with
          a filtered index so a soft-deleted row releases its name.
        - Records this migration in dbo.SchemaMigration.

    Must run after 0014_JiraFlowSchema.sql (owns dbo.Project.TypeCode, which the board
    column seed reads) and after 0010_CriticalFixes.sql (owns the SP_USER_STORY baseline
    this migration re-issues).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. dbo.Sprint
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Sprint')
BEGIN
    CREATE TABLE dbo.Sprint(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NOT NULL,
        Name nvarchar(150) NOT NULL,
        Goal nvarchar(1000) NULL,
        StartDate date NULL,
        EndDate date NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_sprint_status DEFAULT ('PLANNED'),
        Active bit NOT NULL CONSTRAINT DF_sprint_active DEFAULT ((1)),
        InsertDate datetime NULL CONSTRAINT DF_sprint_insertdate DEFAULT (GETDATE()),
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_sprint_isdeleted DEFAULT ((0)),
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_sprint PRIMARY KEY CLUSTERED (Id ASC),
        CONSTRAINT CK_sprint_status CHECK (Status IN ('PLANNED', 'ACTIVE', 'COMPLETED'))
    ) ON [PRIMARY];
END
GO

-- Foreign key kept separate so it self-heals on a database where the table pre-exists.
IF OBJECT_ID('dbo.Sprint') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_sprint_project')
    ALTER TABLE dbo.Sprint ADD CONSTRAINT FK_sprint_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
GO

-- Every sprint read is "the live sprints of one project", so the filter columns lead.
IF OBJECT_ID('dbo.Sprint') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_sprint_project_isdeleted' AND object_id = OBJECT_ID('dbo.Sprint'))
    CREATE NONCLUSTERED INDEX IX_sprint_project_isdeleted ON dbo.Sprint(ProjectId ASC, IsDeleted ASC);
GO

-- ============================================================================
-- 2. dbo.BoardColumns
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'BoardColumns')
BEGIN
    CREATE TABLE dbo.BoardColumns(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NOT NULL,
        Name nvarchar(60) NOT NULL,
        Ordinal int NOT NULL,
        CompleteColumn bit NOT NULL CONSTRAINT DF_boardcolumns_completecolumn DEFAULT ((0)),
        CreatedBy bigint NULL,
        Active bit NOT NULL CONSTRAINT DF_boardcolumns_active DEFAULT ((1)),
        InsertDate datetime NULL CONSTRAINT DF_boardcolumns_insertdate DEFAULT (GETDATE()),
        InsertedBy bigint NULL,
        UpdateDate datetime NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_boardcolumns_isdeleted DEFAULT ((0)),
        DeletedDate datetime NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_boardcolumns PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

IF OBJECT_ID('dbo.BoardColumns') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_boardcolumns_project')
    ALTER TABLE dbo.BoardColumns ADD CONSTRAINT FK_boardcolumns_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
GO

-- (ProjectId, Name) is unique. Enforced as a filtered index rather than a UNIQUE
-- constraint so a soft-deleted column releases its name and can be recreated, which is
-- the uniqueness convention used throughout this schema.
IF OBJECT_ID('dbo.BoardColumns') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_boardcolumns_project_name' AND object_id = OBJECT_ID('dbo.BoardColumns'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_boardcolumns_project_name ON dbo.BoardColumns(ProjectId ASC, Name ASC) WHERE IsDeleted = 0;
GO

-- Ordering read path: the board is always fetched for one project in Ordinal order.
IF OBJECT_ID('dbo.BoardColumns') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_boardcolumns_project_ordinal' AND object_id = OBJECT_ID('dbo.BoardColumns'))
    CREATE NONCLUSTERED INDEX IX_boardcolumns_project_ordinal ON dbo.BoardColumns(ProjectId ASC, Ordinal ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 3. Seed board columns for existing projects, per TypeCode
--
--    Idempotent per project rather than per row: a project that already has at least
--    one live board column is left completely alone, so an operator who reordered or
--    renamed their board never has template rows pushed back underneath them. Only
--    projects with zero live columns are seeded.
-- ============================================================================

IF OBJECT_ID('dbo.BoardColumns') IS NOT NULL AND OBJECT_ID('dbo.Project') IS NOT NULL
BEGIN
    INSERT dbo.BoardColumns (ProjectId, Name, Ordinal, CompleteColumn, CreatedBy, Active, IsDeleted, InsertDate, InsertedBy)
    SELECT p.Id,
           t.Name,
           t.Ordinal,
           t.CompleteColumn,
           NULL,
           1,
           0,
           GETDATE(),
           NULL
    FROM dbo.Project p
    JOIN (VALUES
            ('SCRUM',  N'To Do',       1, CONVERT(bit, 0)),
            ('SCRUM',  N'In Progress', 2, CONVERT(bit, 0)),
            ('SCRUM',  N'Done',        3, CONVERT(bit, 1)),
            ('BASIC',  N'To Do',       1, CONVERT(bit, 0)),
            ('BASIC',  N'In Progress', 2, CONVERT(bit, 0)),
            ('BASIC',  N'Done',        3, CONVERT(bit, 1)),
            ('KANBAN', N'Backlog',     1, CONVERT(bit, 0)),
            ('KANBAN', N'Selected',    2, CONVERT(bit, 0)),
            ('KANBAN', N'In Progress', 3, CONVERT(bit, 0)),
            ('KANBAN', N'Review',      4, CONVERT(bit, 0)),
            ('KANBAN', N'Done',        5, CONVERT(bit, 1))
         ) AS t(TypeCode, Name, Ordinal, CompleteColumn)
      ON t.TypeCode = p.TypeCode
    WHERE p.IsDeleted = 0
      AND NOT EXISTS (
            SELECT 1
            FROM dbo.BoardColumns bc
            WHERE bc.ProjectId = p.Id AND bc.IsDeleted = 0
          );
END
GO

-- ============================================================================
-- 4. Link dbo.UserStory to a sprint
--
--    No foreign key by design: sprint membership is a soft, frequently-rewritten
--    association (stories are dragged in and out of sprints, and sprints are soft
--    deleted), so an enforced reference would buy little and block ordinary
--    housekeeping. Tasks are intentionally not sprint-scoped: they stay under
--    their story and inherit the story's sprint.
-- ============================================================================

IF OBJECT_ID('dbo.UserStory') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserStory') AND name = 'SprintId')
    ALTER TABLE dbo.UserStory ADD SprintId bigint NULL;
GO

IF OBJECT_ID('dbo.UserStory') IS NOT NULL
AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserStory') AND name = 'SprintId')
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_userstory_sprintid' AND object_id = OBJECT_ID('dbo.UserStory'))
    CREATE NONCLUSTERED INDEX IX_userstory_sprintid ON dbo.UserStory(SprintId ASC);
GO

-- ============================================================================
-- 5. SP_SPRINT
--
--    Mirrors the SP_* conventions established in 0010: a single @Action switch,
--    ISNULL coalescing on UPDATE so a partial payload cannot null out a column,
--    IsDeleted = 0 on INSERT, soft DELETE, and a scalar bigint result for every
--    write branch. PAGED is scoped to one project.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_SPRINT
  @Id bigint=NULL,@ProjectId bigint=NULL,@Name nvarchar(150)=NULL,@Goal nvarchar(1000)=NULL,@StartDate date=NULL,@EndDate date=NULL,@Status varchar(20)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search nvarchar(150)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Sprint WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN
    SELECT COUNT_BIG(1) FROM dbo.Sprint WHERE IsDeleted=0 AND (@ProjectId IS NULL OR ProjectId=@ProjectId) AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Goal LIKE '%'+@Search+'%');
    SELECT * FROM dbo.Sprint WHERE IsDeleted=0 AND (@ProjectId IS NULL OR ProjectId=@ProjectId) AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Goal LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
  END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Sprint(ProjectId,Name,Goal,StartDate,EndDate,Status,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@ProjectId,@Name,@Goal,@StartDate,@EndDate,ISNULL(@Status,'PLANNED'),ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Sprint SET ProjectId=ISNULL(@ProjectId,ProjectId),Name=ISNULL(@Name,Name),Goal=ISNULL(@Goal,Goal),StartDate=ISNULL(@StartDate,StartDate),EndDate=ISNULL(@EndDate,EndDate),Status=ISNULL(@Status,Status),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Sprint SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 6. usp_Sprint_Complete
--
--    Guarded transition ACTIVE -> COMPLETED. The Status predicate is what makes the
--    call safe to repeat: completing an already-COMPLETED or still-PLANNED sprint
--    matches no row and returns 0 rather than rewriting history.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_Sprint_Complete
    @Id bigint,
    @UserId bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Sprint
    SET Status = 'COMPLETED',
        UpdateDate = GETDATE(),
        UpdatedBy = @UserId
    WHERE Id = @Id
      AND IsDeleted = 0
      AND Status = 'ACTIVE';

    SELECT CONVERT(bigint, @@ROWCOUNT);
END
GO

-- ============================================================================
-- 7. SP_BOARD_COLUMN
--
--    FETCH returns the whole board for one project in Ordinal order (Id breaks ties
--    so the order is deterministic when two columns share an Ordinal).
--
--    INSERT and UPDATE guard the (ProjectId, Name) uniqueness explicitly, the way
--    SP_TEAM and SP_PROJECT_ROLE do in 0014, so a clash is reported as a named error
--    instead of a raw index violation.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_BOARD_COLUMN
  @Id bigint=NULL,@ProjectId bigint=NULL,@Name nvarchar(60)=NULL,@Ordinal int=NULL,@CompleteColumn bit=NULL,@CreatedBy bigint=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.BoardColumns WHERE IsDeleted=0 AND (@ProjectId IS NULL OR ProjectId=@ProjectId) ORDER BY Ordinal ASC, Id ASC;
  ELSE IF @Action='INSERT'
  BEGIN
    IF NULLIF(LTRIM(RTRIM(ISNULL(@Name, N''))), N'') IS NULL
      THROW 50000, 'SP_BOARD_COLUMN INSERT requires @Name.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Project WHERE Id=@ProjectId AND IsDeleted=0)
      THROW 50000, 'SP_BOARD_COLUMN INSERT: project not found or deleted.', 1;
    IF EXISTS (SELECT 1 FROM dbo.BoardColumns WHERE ProjectId=@ProjectId AND Name=@Name AND IsDeleted=0)
      THROW 50000, 'A board column with that name already exists on this project.', 1;

    INSERT dbo.BoardColumns(ProjectId,Name,Ordinal,CompleteColumn,CreatedBy,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy)
    VALUES(@ProjectId,@Name,ISNULL(@Ordinal,0),ISNULL(@CompleteColumn,0),ISNULL(@CreatedBy,@UserId),1,0,GETDATE(),@UserId,GETDATE(),@UserId);

    SELECT CONVERT(bigint,SCOPE_IDENTITY());
  END
  ELSE IF @Action='UPDATE'
  BEGIN
    IF @Name IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.BoardColumns WHERE Name=@Name AND IsDeleted=0 AND Id<>@Id AND ProjectId=(SELECT ProjectId FROM dbo.BoardColumns WHERE Id=@Id))
      THROW 50000, 'A board column with that name already exists on this project.', 1;

    UPDATE dbo.BoardColumns
    SET Name=ISNULL(@Name,Name),Ordinal=ISNULL(@Ordinal,Ordinal),CompleteColumn=ISNULL(@CompleteColumn,CompleteColumn),UpdateDate=GETDATE(),UpdatedBy=@UserId
    WHERE Id=@Id AND IsDeleted=0;

    SELECT CONVERT(bigint,@@ROWCOUNT);
  END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.BoardColumns SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 8. SP_USER_STORY: thread @SprintId through
--
--    Reproduced from 0010_CriticalFixes.sql (section 8) verbatim except for the new
--    @SprintId parameter, so CREATE OR ALTER replaces the whole object without
--    regressing any of the fixes 0010 applied. SprintId defaults to NULL, so every
--    existing caller keeps working unchanged.
--
--    FETCH and PAGED project with SELECT *, so SprintId joins the result set as soon
--    as section 4 has added the column - no projection change is needed or wanted.
--    UPDATE uses ISNULL, matching every other nullable column here: a caller that
--    omits @SprintId leaves the story's sprint membership untouched.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_USER_STORY
  @Id bigint=NULL,@ProjectId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@AcceptanceCriteria varchar(max)=NULL,@Status varchar(30)=NULL,@Priority int=NULL,@StoryPoints decimal(18,2)=NULL,@AssigneeUserId bigint=NULL,@SprintId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.UserStory WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.UserStory(ProjectId,Title,Description,AcceptanceCriteria,Status,Priority,StoryPoints,AssigneeUserId,SprintId,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@ProjectId,@Title,@Description,@AcceptanceCriteria,@Status,@Priority,@StoryPoints,@AssigneeUserId,@SprintId,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.UserStory SET ProjectId=ISNULL(@ProjectId,ProjectId),Title=ISNULL(@Title,Title),Description=ISNULL(@Description,Description),AcceptanceCriteria=ISNULL(@AcceptanceCriteria,AcceptanceCriteria),Status=ISNULL(@Status,Status),Priority=ISNULL(@Priority,Priority),StoryPoints=ISNULL(@StoryPoints,StoryPoints),AssigneeUserId=ISNULL(@AssigneeUserId,AssigneeUserId),SprintId=ISNULL(@SprintId,SprintId),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.UserStory SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 9. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0017' AND Name = 'SprintsAndBoards' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0017', 'SprintsAndBoards', GETDATE(), 1);
END
GO
