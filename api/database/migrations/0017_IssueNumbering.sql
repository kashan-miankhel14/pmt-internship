/*
    0017_IssueNumbering.sql

    Adds Jira-style, per-project issue numbering: dbo.Issue.Number combined with
    dbo.Project.[Key] yields a human-facing key such as "PMT-1".

    dbo.ProjectCounters (ProjectId, LastIssueNumber) was introduced in
    0014_JiraFlowSchema.sql but nothing ever incremented it. This migration makes
    it the single source of truth for the next issue number.

    Changes applied:
        1. Safety net: create dbo.ProjectCounters if a database predates 0014.
        2. Add dbo.Issue.Number int NOT NULL DEFAULT 0 (DF_issue_number).
        3. Backfill Number for existing live issues: sequential per project,
           ordered by Id, via ROW_NUMBER(). Guarded so it only runs while
           unnumbered issues exist, which keeps re-runs from renumbering issues
           that the procedure has since numbered.
        4. Reconcile dbo.ProjectCounters: raise LastIssueNumber to the highest
           issue number per project and insert a zeroed row for every project
           that has no counter yet. Never lowers a counter, so a number can
           never be handed out twice.
        5. Create UNIQUE NONCLUSTERED IX_issue_project_number on
           (ProjectId, Number) WHERE IsDeleted = 0. Created *after* the backfill
           and only when no duplicate pair exists, because every pre-existing row
           starts at Number = 0. The filter keeps soft-deleted rows (which stay
           at 0) from colliding.
        6. CREATE OR ALTER dbo.SP_ISSUE:
             - INSERT now allocates the next number atomically. A single MERGE
               against dbo.ProjectCounters WITH (HOLDLOCK) increments and returns
               LastIssueNumber (and seeds the row when a project has no counter),
               so concurrent inserts serialize on that row instead of colliding.
               The counter bump and the issue insert share one transaction under
               XACT_ABORT, and the procedure still returns SCOPE_IDENTITY().
             - FETCH/PAGED additionally return Number (via I.*) and the joined
               Project.[Key] as ProjectKey so callers can render "PMT-1".
             - UPDATE deliberately does not touch Number; DELETE is unchanged.
        7. Record this migration in dbo.SchemaMigration.

    IDEMPOTENT and re-runnable. Each section is in its own GO batch.
    No wrapping transaction: partial success is safe to re-run.

    Must run after 0014_JiraFlowSchema.sql (owns dbo.ProjectCounters).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. dbo.ProjectCounters safety net (0014 owns the canonical definition)
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
-- 2. dbo.Issue.Number
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Issue') AND name = 'Number')
    ALTER TABLE dbo.Issue ADD Number int NOT NULL CONSTRAINT DF_issue_number DEFAULT ((0));
GO

-- ============================================================================
-- 3. Backfill Number for existing issues (sequential per project, ordered by Id)
--
--    Only runs while at least one live issue is still unnumbered. Without that
--    guard a re-run would renumber issues that SP_ISSUE has already numbered and
--    desynchronise them from dbo.ProjectCounters.
-- ============================================================================

IF EXISTS (SELECT 1 FROM dbo.Issue WHERE Number = 0 AND IsDeleted = 0)
BEGIN
    UPDATE i
    SET Number = r.rn
    FROM (
        SELECT Id, ROW_NUMBER() OVER (PARTITION BY ProjectId ORDER BY Id) AS rn
        FROM dbo.Issue
        WHERE IsDeleted = 0
    ) r
    JOIN dbo.Issue i ON i.Id = r.Id;
END
GO

-- ============================================================================
-- 4. Reconcile dbo.ProjectCounters with the backfilled numbers
--
--    Raise-only: a counter is never lowered, so numbers already handed out by
--    SP_ISSUE can never be reused. Safe to re-run unconditionally.
-- ============================================================================

UPDATE pc
SET LastIssueNumber = m.MaxNumber
FROM dbo.ProjectCounters pc
JOIN (
    SELECT ProjectId, MAX(Number) AS MaxNumber
    FROM dbo.Issue
    WHERE IsDeleted = 0
    GROUP BY ProjectId
) m ON m.ProjectId = pc.ProjectId
WHERE m.MaxNumber > pc.LastIssueNumber;
GO

INSERT dbo.ProjectCounters (ProjectId, LastIssueNumber)
SELECT p.Id, ISNULL(m.MaxNumber, 0)
FROM dbo.Project p
LEFT JOIN (
    SELECT ProjectId, MAX(Number) AS MaxNumber
    FROM dbo.Issue
    WHERE IsDeleted = 0
    GROUP BY ProjectId
) m ON m.ProjectId = p.Id
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProjectCounters pc WHERE pc.ProjectId = p.Id);
GO

-- ============================================================================
-- 5. Unique index on (ProjectId, Number) -- created AFTER the backfill
--
--    Filtered to live rows: soft-deleted issues keep Number = 0 and would
--    otherwise collide. The duplicate guard keeps the migration re-runnable on a
--    database that somehow still holds duplicates instead of failing the batch.
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_issue_project_number' AND object_id = OBJECT_ID('dbo.Issue'))
AND NOT EXISTS (
    SELECT 1
    FROM dbo.Issue
    WHERE IsDeleted = 0
    GROUP BY ProjectId, Number
    HAVING COUNT_BIG(1) > 1
)
    CREATE UNIQUE NONCLUSTERED INDEX IX_issue_project_number ON dbo.Issue(ProjectId ASC, Number ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 6. SP_ISSUE: atomic number allocation on INSERT, Number/ProjectKey on reads
--
--    Parameter list is unchanged from 0010_CriticalFixes.sql: the number is
--    allocated server-side, never supplied by the caller. INSERT still returns a
--    single bigint (SCOPE_IDENTITY) so IssueRepository.CreateAsync is unaffected.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_ISSUE
  @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@Severity varchar(20)=NULL,@Status varchar(30)=NULL,@ReportedByUserId bigint=NULL,@AssigneeUserId bigint=NULL,@ResolvedDate datetime=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN
  SET NOCOUNT ON;
  SET XACT_ABORT ON;

  IF @Action='FETCH'
    SELECT I.*, P.[Key] AS ProjectKey
    FROM dbo.Issue I
    LEFT JOIN dbo.Project P ON P.Id=I.ProjectId
    WHERE I.Id=@Id AND I.IsDeleted=0;
  ELSE IF @Action='PAGED'
  BEGIN
    SELECT COUNT_BIG(1)
    FROM dbo.Issue
    WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%');

    SELECT I.*, P.[Key] AS ProjectKey
    FROM dbo.Issue I
    LEFT JOIN dbo.Project P ON P.Id=I.ProjectId
    WHERE I.IsDeleted=0 AND (@Search IS NULL OR I.Title LIKE '%'+@Search+'%' OR I.Description LIKE '%'+@Search+'%')
    ORDER BY I.Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
  END
  ELSE IF @Action='INSERT'
  BEGIN
    DECLARE @Allocated TABLE (Number int NOT NULL);
    DECLARE @Number int, @NewId bigint;

    BEGIN TRY
      BEGIN TRANSACTION;

      -- Atomically take the next number. HOLDLOCK makes the match/insert decision
      -- serializable, so two concurrent inserts for the same project queue up on
      -- the counter row rather than both reading the same LastIssueNumber.
      MERGE dbo.ProjectCounters WITH (HOLDLOCK) AS T
      USING (SELECT @ProjectId AS ProjectId) AS S
        ON T.ProjectId = S.ProjectId
      WHEN MATCHED THEN
        UPDATE SET LastIssueNumber = T.LastIssueNumber + 1
      WHEN NOT MATCHED THEN
        INSERT (ProjectId, LastIssueNumber) VALUES (S.ProjectId, 1)
      OUTPUT INSERTED.LastIssueNumber INTO @Allocated(Number);

      SELECT @Number = Number FROM @Allocated;

      INSERT dbo.Issue(ProjectId,TaskId,Number,Title,Description,Severity,Status,ReportedByUserId,AssigneeUserId,ResolvedDate,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy)
      VALUES(@ProjectId,@TaskId,@Number,@Title,@Description,@Severity,@Status,@ReportedByUserId,@AssigneeUserId,@ResolvedDate,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId);

      SET @NewId = CONVERT(bigint,SCOPE_IDENTITY());

      COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
      IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
      THROW;
    END CATCH

    SELECT @NewId;
  END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Issue SET ProjectId=ISNULL(@ProjectId,ProjectId),TaskId=ISNULL(@TaskId,TaskId),Title=ISNULL(@Title,Title),Description=ISNULL(@Description,Description),Severity=ISNULL(@Severity,Severity),Status=ISNULL(@Status,Status),ReportedByUserId=ISNULL(@ReportedByUserId,ReportedByUserId),AssigneeUserId=ISNULL(@AssigneeUserId,AssigneeUserId),ResolvedDate=ISNULL(@ResolvedDate,ResolvedDate),Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Issue SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 7. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0017' AND Name = 'IssueNumbering' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0017', 'IssueNumbering', SYSUTCDATETIME(), 1);
END
GO
