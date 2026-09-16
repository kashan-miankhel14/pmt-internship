-- ============================================================================
-- Migration 0022: Complete Procedure Update Normalization & Nullable Column Fixes
--
-- Ensures all standard CRUD stored procedures have case-insensitive action matching,
-- return proper ROWCOUNT on UPDATE/DELETE, and correctly handle nullable/cleared fields.
-- ============================================================================

-- ============================================================================
-- 1. SP_PROJECT
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.SP_PROJECT
    @Id bigint = NULL,
    @Name varchar(200) = NULL,
    @Description varchar(max) = NULL,
    @Key varchar(50) = NULL,
    @Status varchar(30) = NULL,
    @DepartmentId bigint = NULL,
    @OwnerUserId bigint = NULL,
    @StartDate date = NULL,
    @TargetDate date = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @Search varchar(200) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT * FROM dbo.Project WHERE Id = @Id AND IsDeleted = 0;
    ELSE IF @NormalizedAction = 'FETCHBYKEY' OR @NormalizedAction = 'FETCH_BY_KEY'
        SELECT TOP (1) Id, Name, Description, [Key], Status, DepartmentId, OwnerUserId, StartDate, TargetDate, Active, InsertDate, InsertedBy, UpdateDate, UpdatedBy, IsDeleted, DeletedDate, DeletedBy
        FROM dbo.Project
        WHERE [Key] = @Key AND IsDeleted = 0;
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.Project
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%');

        SELECT * FROM dbo.Project
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR Description LIKE '%' + @Search + '%')
        ORDER BY Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT dbo.Project(Name, Description, [Key], Status, DepartmentId, OwnerUserId, StartDate, TargetDate, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@Name, @Description, @Key, ISNULL(@Status, 'Active'), @DepartmentId, @OwnerUserId, @StartDate, @TargetDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Project
        SET Name = ISNULL(@Name, Name),
            Description = @Description,
            [Key] = ISNULL(@Key, [Key]),
            Status = ISNULL(@Status, Status),
            DepartmentId = ISNULL(@DepartmentId, DepartmentId),
            OwnerUserId = ISNULL(@OwnerUserId, OwnerUserId),
            StartDate = @StartDate,
            TargetDate = @TargetDate,
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.Project
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- ============================================================================
-- 2. SP_USER_STORY
-- ============================================================================
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
        INSERT dbo.UserStory(ProjectId, Title, Description, AcceptanceCriteria, Status, Priority, StoryPoints, AssigneeUserId, SprintId, Active, IsDeleted, InsertDate, InsertedBy, UpdateDate, UpdatedBy)
        VALUES(@ProjectId, @Title, @Description, @AcceptanceCriteria, ISNULL(@Status, 'Backlog'), ISNULL(@Priority, 3), @StoryPoints, @AssigneeUserId, @SprintId, ISNULL(@Active, 1), 0, GETDATE(), @UserId, GETDATE(), @UserId);
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

-- ============================================================================
-- 3. SP_TASK
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.SP_TASK
    @Id bigint = NULL,
    @StoryId bigint = NULL,
    @ProjectId bigint = NULL,
    @AssigneeUserId bigint = NULL,
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
        INSERT dbo.Task(StoryId, ProjectId, AssigneeUserId, Title, Description, EstimateHours, ActualHours, Status, Priority, DueDate, CompletedDate, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@StoryId, @ProjectId, @AssigneeUserId, @Title, @Description, @EstimateHours, @ActualHours, ISNULL(@Status, 'ToDo'), ISNULL(@Priority, 3), @DueDate, @CompletedDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Task
        SET StoryId = ISNULL(@StoryId, StoryId),
            ProjectId = ISNULL(@ProjectId, ProjectId),
            AssigneeUserId = @AssigneeUserId,
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

-- ============================================================================
-- 4. SP_ISSUE
-- ============================================================================
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

                INSERT dbo.Issue(ProjectId, TaskId, Number, Title, Description, Severity, Status, ReportedByUserId, AssigneeUserId, ResolvedDate, Active, IsDeleted, InsertDate, InsertedBy, UpdateDate, UpdatedBy)
                VALUES(@ProjectId, @TaskId, @Number, @Title, @Description, ISNULL(@Severity, 'Medium'), ISNULL(@Status, 'Open'), @ReportedByUserId, @AssigneeUserId, @ResolvedDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId, GETDATE(), @UserId);

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

-- ============================================================================
-- 5. SP_SPRINT
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.SP_SPRINT
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @Name nvarchar(150) = NULL,
    @Goal nvarchar(1000) = NULL,
    @StartDate date = NULL,
    @EndDate date = NULL,
    @Status varchar(20) = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @Search nvarchar(150) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT * FROM dbo.Sprint WHERE Id = @Id AND IsDeleted = 0;
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.Sprint
        WHERE IsDeleted = 0 AND (@ProjectId IS NULL OR ProjectId = @ProjectId) AND (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR Goal LIKE '%' + @Search + '%');

        SELECT * FROM dbo.Sprint
        WHERE IsDeleted = 0 AND (@ProjectId IS NULL OR ProjectId = @ProjectId) AND (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR Goal LIKE '%' + @Search + '%')
        ORDER BY Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT dbo.Sprint(ProjectId, Name, Goal, StartDate, EndDate, Status, Active, IsDeleted, InsertDate, InsertedBy, UpdateDate, UpdatedBy)
        VALUES(@ProjectId, @Name, @Goal, @StartDate, @EndDate, ISNULL(@Status, 'PLANNED'), ISNULL(@Active, 1), 0, GETDATE(), @UserId, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Sprint
        SET ProjectId = ISNULL(@ProjectId, ProjectId),
            Name = ISNULL(@Name, Name),
            Goal = @Goal,
            StartDate = @StartDate,
            EndDate = @EndDate,
            Status = ISNULL(@Status, Status),
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.Sprint
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- ============================================================================
-- 6. SP_BOARD_COLUMN
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.SP_BOARD_COLUMN
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @Name varchar(100) = NULL,
    @Ordinal int = NULL,
    @CompleteColumn bit = NULL,
    @CreatedBy bigint = NULL,
    @UserId bigint = NULL,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT * FROM dbo.BoardColumns
        WHERE ProjectId = @ProjectId AND IsDeleted = 0
        ORDER BY Ordinal;
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT dbo.BoardColumns(ProjectId, Name, Ordinal, CompleteColumn, CreatedBy, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@ProjectId, @Name, @Ordinal, ISNULL(@CompleteColumn, 0), @CreatedBy, 1, 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.BoardColumns
        SET Name = ISNULL(@Name, Name),
            Ordinal = ISNULL(@Ordinal, Ordinal),
            CompleteColumn = ISNULL(@CompleteColumn, CompleteColumn),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.BoardColumns
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- ============================================================================
-- 7. SP_DEPARTMENT
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.SP_DEPARTMENT
    @Id bigint = NULL,
    @Name varchar(150) = NULL,
    @Code varchar(50) = NULL,
    @Description varchar(MAX) = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @InsertedBy bigint = NULL,
    @UpdatedBy bigint = NULL,
    @Search varchar(150) = NULL,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
        SELECT * FROM dbo.Department WHERE Id = ISNULL(@Id, Id) AND IsDeleted = 0;
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.Department
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR ISNULL(Code, '') LIKE '%' + @Search + '%');

        SELECT * FROM dbo.Department
        WHERE IsDeleted = 0 AND (@Search IS NULL OR Name LIKE '%' + @Search + '%' OR ISNULL(Code, '') LIKE '%' + @Search + '%')
        ORDER BY Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT dbo.Department(Name, Code, Description, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@Name, @Code, @Description, ISNULL(@Active, 1), 0, GETDATE(), ISNULL(@InsertedBy, @UserId));
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Department
        SET Name = ISNULL(@Name, Name),
            Code = @Code,
            Description = @Description,
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = ISNULL(@UpdatedBy, @UserId)
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.Department
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = ISNULL(@UserId, @UpdatedBy)
        WHERE Id = @Id AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO
