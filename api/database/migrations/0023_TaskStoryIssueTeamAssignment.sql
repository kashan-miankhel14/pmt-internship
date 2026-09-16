-- ============================================================================
-- Migration 0023: Task, UserStory, and Issue Team Assignment Support
-- 
-- Adds TeamId to dbo.Task, dbo.UserStory, and dbo.Issue to support assigning
-- work items to a Team (e.g. Backend, Frontend, QA) alongside or independently
-- of an individual user assignment (AssigneeUserId).
--
-- Updates SP_TASK, SP_USER_STORY, and SP_ISSUE to accept and persist @TeamId.
-- ============================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1. ADD TeamId TO dbo.Task
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.Task') AND name = 'TeamId'
)
BEGIN
    ALTER TABLE dbo.Task ADD TeamId bigint NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE name = 'FK_task_team' AND parent_object_id = OBJECT_ID('dbo.Task')
)
BEGIN
    ALTER TABLE dbo.Task ADD CONSTRAINT FK_task_team FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_task_teamid' AND object_id = OBJECT_ID('dbo.Task')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_task_teamid ON dbo.Task(TeamId ASC);
END
GO

-- 2. ADD TeamId TO dbo.UserStory
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.UserStory') AND name = 'TeamId'
)
BEGIN
    ALTER TABLE dbo.UserStory ADD TeamId bigint NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE name = 'FK_userstory_team' AND parent_object_id = OBJECT_ID('dbo.UserStory')
)
BEGIN
    ALTER TABLE dbo.UserStory ADD CONSTRAINT FK_userstory_team FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_userstory_teamid' AND object_id = OBJECT_ID('dbo.UserStory')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_userstory_teamid ON dbo.UserStory(TeamId ASC);
END
GO

-- 3. ADD TeamId TO dbo.Issue
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.Issue') AND name = 'TeamId'
)
BEGIN
    ALTER TABLE dbo.Issue ADD TeamId bigint NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE name = 'FK_issue_team' AND parent_object_id = OBJECT_ID('dbo.Issue')
)
BEGIN
    ALTER TABLE dbo.Issue ADD CONSTRAINT FK_issue_team FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_issue_teamid' AND object_id = OBJECT_ID('dbo.Issue')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_issue_teamid ON dbo.Issue(TeamId ASC);
END
GO

-- 4. UPDATE dbo.SP_TASK
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.SP_TASK
    @Id bigint = NULL,
    @StoryId bigint = NULL,
    @ProjectId bigint = NULL,
    @AssigneeUserId bigint = NULL,
    @TeamId bigint = NULL,
    @Title varchar(250) = NULL,
    @Description varchar(max) = NULL,
    @EstimateHours decimal(18,2) = NULL,
    @ActualHours decimal(18,2) = NULL,
    @Status varchar(30) = NULL,
    @Priority int = NULL,
    @DueDate date = NULL,
    @CompletedDate datetime = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @Search varchar(250) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT * FROM dbo.Task WHERE Id = @Id AND IsDeleted = 0;
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.Task
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Title LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%');

        SELECT * FROM dbo.Task
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Title LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%')
        ORDER BY Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'CHECKCHILDREN' OR @NormalizedAction = 'CHECK_CHILDREN'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.Task
        WHERE StoryId = @StoryId AND IsDeleted = 0 AND Status NOT IN ('Done', 'Cancelled');
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT dbo.Task(StoryId, ProjectId, AssigneeUserId, TeamId, Title, Description, EstimateHours, ActualHours, Status, Priority, DueDate, CompletedDate, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@StoryId, @ProjectId, @AssigneeUserId, @TeamId, @Title, @Description, @EstimateHours, @ActualHours, ISNULL(@Status, 'ToDo'), ISNULL(@Priority, 3), @DueDate, @CompletedDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Task
        SET StoryId = ISNULL(@StoryId, StoryId),
            ProjectId = ISNULL(@ProjectId, ProjectId),
            AssigneeUserId = @AssigneeUserId,
            TeamId = @TeamId,
            Title = ISNULL(@Title, Title),
            Description = @Description,
            EstimateHours = @EstimateHours,
            ActualHours = @ActualHours,
            Status = ISNULL(@Status, Status),
            Priority = ISNULL(@Priority, Priority),
            DueDate = @DueDate,
            CompletedDate = @CompletedDate,
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.Task
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- 5. UPDATE dbo.SP_USER_STORY
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.SP_USER_STORY
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @Title varchar(250) = NULL,
    @Description varchar(max) = NULL,
    @AcceptanceCriteria varchar(max) = NULL,
    @Status varchar(30) = NULL,
    @Priority int = NULL,
    @StoryPoints decimal(18,2) = NULL,
    @AssigneeUserId bigint = NULL,
    @TeamId bigint = NULL,
    @SprintId bigint = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @Search varchar(250) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT * FROM dbo.UserStory WHERE Id = @Id AND IsDeleted = 0;
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.UserStory
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Title LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%');

        SELECT * FROM dbo.UserStory
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Title LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%')
        ORDER BY Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT dbo.UserStory(ProjectId, Title, Description, AcceptanceCriteria, Status, Priority, StoryPoints, AssigneeUserId, TeamId, SprintId, Active, IsDeleted, InsertDate, InsertedBy, UpdateDate, UpdatedBy)
        VALUES(@ProjectId, @Title, @Description, @AcceptanceCriteria, ISNULL(@Status, 'Backlog'), ISNULL(@Priority, 3), @StoryPoints, @AssigneeUserId, @TeamId, @SprintId, ISNULL(@Active, 1), 0, GETDATE(), @UserId, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.UserStory
        SET ProjectId = ISNULL(@ProjectId, ProjectId),
            Title = ISNULL(@Title, Title),
            Description = @Description,
            AcceptanceCriteria = @AcceptanceCriteria,
            Status = ISNULL(@Status, Status),
            Priority = ISNULL(@Priority, Priority),
            StoryPoints = @StoryPoints,
            AssigneeUserId = @AssigneeUserId,
            TeamId = @TeamId,
            SprintId = @SprintId,
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.UserStory
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- 6. UPDATE dbo.SP_ISSUE
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.SP_ISSUE
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @TaskId bigint = NULL,
    @Title varchar(250) = NULL,
    @Description varchar(max) = NULL,
    @Severity varchar(20) = NULL,
    @Status varchar(30) = NULL,
    @ReportedByUserId bigint = NULL,
    @AssigneeUserId bigint = NULL,
    @TeamId bigint = NULL,
    @ResolvedDate datetime = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @Search varchar(250) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT I.*, P.[Key] AS ProjectKey
        FROM dbo.Issue I
        LEFT JOIN dbo.Project P ON P.Id = I.ProjectId
        WHERE I.Id = @Id AND I.IsDeleted = 0;
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1)
        FROM dbo.Issue
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Title LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%');

        SELECT I.*, P.[Key] AS ProjectKey
        FROM dbo.Issue I
        LEFT JOIN dbo.Project P ON P.Id = I.ProjectId
        WHERE I.IsDeleted = 0 AND (@Search IS NULL OR I.Title LIKE '%' + @Search + '%' OR I.Description LIKE '%' + @Search + '%')
        ORDER BY I.Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        DECLARE @Allocated TABLE (Number int NOT NULL);
        DECLARE @Number int, @NewId bigint;

        BEGIN TRY
            BEGIN TRANSACTION;
                MERGE dbo.ProjectCounters WITH (HOLDLOCK) AS T
                USING (SELECT @ProjectId AS ProjectId) AS S
                    ON T.ProjectId = S.ProjectId
                WHEN MATCHED THEN
                    UPDATE SET LastIssueNumber = T.LastIssueNumber + 1
                WHEN NOT MATCHED THEN
                    INSERT (ProjectId, LastIssueNumber) VALUES (S.ProjectId, 1)
                OUTPUT INSERTED.LastIssueNumber INTO @Allocated(Number);

                SELECT @Number = Number FROM @Allocated;

                INSERT dbo.Issue(ProjectId, TaskId, Number, Title, Description, Severity, Status, ReportedByUserId, AssigneeUserId, TeamId, ResolvedDate, Active, IsDeleted, InsertDate, InsertedBy, UpdateDate, UpdatedBy)
                VALUES(@ProjectId, @TaskId, @Number, @Title, @Description, ISNULL(@Severity, 'Medium'), ISNULL(@Status, 'Open'), @ReportedByUserId, @AssigneeUserId, @TeamId, @ResolvedDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId, GETDATE(), @UserId);

                SET @NewId = CONVERT(bigint, SCOPE_IDENTITY());
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH

        SELECT @NewId;
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Issue
        SET ProjectId = ISNULL(@ProjectId, ProjectId),
            TaskId = @TaskId,
            Title = ISNULL(@Title, Title),
            Description = @Description,
            Severity = ISNULL(@Severity, Severity),
            Status = ISNULL(@Status, Status),
            ReportedByUserId = ISNULL(@ReportedByUserId, ReportedByUserId),
            AssigneeUserId = @AssigneeUserId,
            TeamId = @TeamId,
            ResolvedDate = @ResolvedDate,
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.Issue
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO
