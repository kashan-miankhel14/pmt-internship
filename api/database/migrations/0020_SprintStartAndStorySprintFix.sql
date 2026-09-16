/*
    0020_SprintStartAndStorySprintFix.sql

    Three procedure-level corrections. No schema changes: every table, column and index
    this script touches already exists, so the whole migration is CREATE OR ALTER only.

        1. SP_USER_STORY - the UPDATE branch assigned SprintId with
           ISNULL(@SprintId, SprintId), which made "move this story back to the backlog"
           impossible: the repository sends entity.SprintId on every update, so a cleared
           sprint arrived as NULL and ISNULL faithfully restored the old value. The story
           could be pulled into a sprint and never pulled out again. @SprintId is now
           written directly, which is correct because UserStoryRepository.UpdateAsync
           always supplies the column (a null there means "no sprint", not "unspecified").
           Every other column keeps its ISNULL coalescing exactly as 0017 left it, and the
           INSERT branch is unchanged.

        2. SP_NOTIFICATION - re-asserted with @Title and @Link. The 0009 baseline already
           has them, but 0003 shipped an earlier four-parameter version, and a database
           that was restored from a backup taken between those two migrations still has
           the old proc. NotificationRepository.CreateAsync now sends Title and Link on
           every insert, so that drift surfaces as "Procedure or function SP_NOTIFICATION
           has too many arguments specified" on any notification write. Re-issuing the
           0009 definition verbatim heals it. Behaviour is otherwise identical: same
           FETCH, same INSERT column list, same MARKREAD including the ReadDate stamp.

        3. dbo.usp_Sprint_Start - new. The mirror image of usp_Sprint_Complete (0017,
           section 6): a guarded PLANNED -> ACTIVE transition returning the number of rows
           it moved. Sprint start had no procedure of its own, so SprintService performed a
           read-then-write through SP_SPRINT's UPDATE branch and two concurrent starts could
           both pass the PLANNED check. With the guard inside the UPDATE, the second caller
           matches no row and is told the status changed underneath it, which is exactly how
           completion already behaves.

    Conventions (aligned with 0009-0019):
        - Idempotent: CREATE OR ALTER for every procedure, no DDL, re-runnable with no
          wrapping transaction.
        - Each procedure is created in its own GO batch so CREATE PROCEDURE is the first
          statement of its batch.
        - SET QUOTED_IDENTIFIER / ANSI_NULLS ON; NOCOUNT ON; XACT_ABORT ON.
        - Every write branch returns a scalar bigint (SCOPE_IDENTITY or @@ROWCOUNT).
        - Records this migration in dbo.SchemaMigration.

    Must run after 0017_SprintsAndBoards.sql (owns dbo.Sprint, the UserStory.SprintId column
    and the SP_USER_STORY baseline re-issued here) and after 0009_DeployCompleteSchema.sql
    (owns dbo.Notification.Title / .Link and the SP_NOTIFICATION baseline).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. SP_USER_STORY: let a story leave its sprint
--
--    Reproduced from 0017_SprintsAndBoards.sql (section 8) verbatim except for the
--    UPDATE branch's SprintId assignment, so CREATE OR ALTER replaces the whole object
--    without regressing the @SprintId threading 0017 added or the fixes 0010 applied.
--
--    SprintId=@SprintId (not ISNULL(@SprintId,SprintId)) is the whole point of this
--    section: the repository sends the column on every update, so passing NULL is a
--    deliberate "return this story to the backlog" and must be honoured. INSERT already
--    wrote @SprintId directly and is left exactly as it was.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_USER_STORY
  @Id bigint=NULL,@ProjectId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@AcceptanceCriteria varchar(max)=NULL,@Status varchar(30)=NULL,@Priority int=NULL,@StoryPoints decimal(18,2)=NULL,@AssigneeUserId bigint=NULL,@SprintId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.UserStory WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.UserStory(ProjectId,Title,Description,AcceptanceCriteria,Status,Priority,StoryPoints,AssigneeUserId,SprintId,Active,IsDeleted,InsertDate,InsertedBy,UpdateDate,UpdatedBy) VALUES(@ProjectId,@Title,@Description,@AcceptanceCriteria,@Status,@Priority,@StoryPoints,@AssigneeUserId,@SprintId,ISNULL(@Active,1),0,GETDATE(),@UserId,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.UserStory SET ProjectId=ISNULL(@ProjectId,ProjectId),Title=ISNULL(@Title,Title),Description=ISNULL(@Description,Description),AcceptanceCriteria=ISNULL(@AcceptanceCriteria,AcceptanceCriteria),Status=ISNULL(@Status,Status),Priority=ISNULL(@Priority,Priority),StoryPoints=ISNULL(@StoryPoints,StoryPoints),AssigneeUserId=ISNULL(@AssigneeUserId,AssigneeUserId),SprintId=@SprintId,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.UserStory SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 2. SP_NOTIFICATION: guarantee @Title and @Link exist
--
--    Reproduced from 0009_DeployCompleteSchema.sql verbatim. This is a re-assertion,
--    not a change: on a database that already has the 0009 definition this rewrites the
--    object with an identical one, and on a database still carrying the 0003 version it
--    adds the two parameters the repository now sends. FETCH, the INSERT column list and
--    MARKREAD (including the ReadDate stamp 0009 introduced) are preserved exactly.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_NOTIFICATION
 @Id bigint=NULL,@UserId bigint=NULL,@EventType varchar(50)=NULL,@Title varchar(250)=NULL,@Message varchar(500)=NULL,@Link varchar(500)=NULL,@IsRead bit=NULL,@UnreadOnly bit=0,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT TOP(200)* FROM dbo.Notification WHERE UserId=@UserId AND IsDeleted=0 AND (@UnreadOnly=0 OR IsRead=0) ORDER BY InsertDate DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Notification(UserId,EventType,Title,Message,Link,IsRead,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@UserId,@EventType,@Title,@Message,@Link,ISNULL(@IsRead,0),1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='MARKREAD' BEGIN UPDATE dbo.Notification SET IsRead=1,ReadDate=GETDATE(),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND UserId=@UserId AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

-- ============================================================================
-- 3. usp_Sprint_Start
--
--    Guarded transition PLANNED -> ACTIVE, mirroring usp_Sprint_Complete: same
--    parameter shape (@Id, @UserId), same scalar bigint output, same soft-delete filter.
--    The Status predicate is what makes the call safe to repeat: starting an already
--    ACTIVE or a COMPLETED sprint matches no row and returns 0 rather than reopening it.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_Sprint_Start
    @Id bigint,
    @UserId bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Sprint
    SET Status = 'ACTIVE',
        UpdateDate = GETDATE(),
        UpdatedBy = @UserId
    WHERE Id = @Id
      AND IsDeleted = 0
      AND Status = 'PLANNED';

    SELECT CONVERT(bigint, @@ROWCOUNT);
END
GO

-- ============================================================================
-- 4. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0020' AND Name = 'SprintStartAndStorySprintFix' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0020', 'SprintStartAndStorySprintFix', SYSUTCDATETIME(), 1);
END
GO
