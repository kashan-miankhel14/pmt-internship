-- ============================================================================
-- Migration 0021: Sprint Workflow Engine & Complete Stored Procedure Sanitization
--
-- 1. Sprint Lifecycle Stored Procedures:
--    - dbo.usp_Sprint_Start
--    - dbo.usp_Sprint_Complete
--    - dbo.usp_Sprint_Workflow
--
-- 2. Auth & Session Stored Procedures:
--    - dbo.usp_Auth_RotateRefreshToken
--    - Updated dbo.SP_REFRESH_TOKEN
--
-- 3. AI Agent Tool Call & Session Stored Procedures:
--    - dbo.usp_AiChatSession_GetById
--    - dbo.usp_AiAgentToolCall_Complete
--
-- 4. Entity Stored Procedure Enhancements:
--    - dbo.SP_USER (UPDATEPASSWORD, LOGINSUCCESS, LOGINFAILURE)
--    - dbo.SP_TASK (CHECKCHILDREN)
--    - dbo.SP_PROJECT (FETCHBYKEY)
--    - dbo.SP_ADMIN (RESETPASSWORD)
-- ============================================================================

-- ============================================================================
-- 1. Sprint Procedures
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

CREATE OR ALTER PROCEDURE dbo.usp_Sprint_Workflow
    @Id bigint,
    @Action varchar(30),
    @UserId bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'START'
    BEGIN
        EXEC dbo.usp_Sprint_Start @Id = @Id, @UserId = @UserId;
    END
    ELSE IF @NormalizedAction = 'COMPLETE'
    BEGIN
        EXEC dbo.usp_Sprint_Complete @Id = @Id, @UserId = @UserId;
    END
    ELSE IF @NormalizedAction = 'CANCEL'
    BEGIN
        UPDATE dbo.Sprint
        SET Status = 'CANCELLED',
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id
          AND IsDeleted = 0
          AND Status IN ('PLANNED', 'ACTIVE');

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'REOPEN'
    BEGIN
        UPDATE dbo.Sprint
        SET Status = 'PLANNED',
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id
          AND IsDeleted = 0
          AND Status = 'CANCELLED';

        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE
    BEGIN
        SELECT CAST(0 AS bigint);
    END
END
GO

-- ============================================================================
-- 2. Auth & Token Stored Procedures
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_Auth_RotateRefreshToken
    @TokenHash varchar(500),
    @NewTokenHash varchar(500),
    @ExpiresAt datetime,
    @JwtId varchar(100) = NULL,
    @CreatedByIp varchar(50) = NULL,
    @RevokedByIp varchar(50) = NULL
AS BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @UserId bigint = 0, @Status int = 0;

    BEGIN TRY
        BEGIN TRAN;
            DECLARE @IsRevoked bit, @ExpiryDate datetime;
            SELECT @UserId = UserId, @IsRevoked = IsRevoked, @ExpiryDate = ExpiryDate
            FROM dbo.RefreshToken WITH (UPDLOCK, ROWLOCK)
            WHERE TokenHash = @TokenHash AND IsDeleted = 0;

            IF @UserId IS NULL
                SET @Status = 0;            -- NotFound
            ELSE IF @IsRevoked = 1
                SET @Status = 1;            -- ReuseDetected
            ELSE IF @ExpiryDate < GETDATE()
                SET @Status = 0;            -- Expired
            ELSE
            BEGIN
                UPDATE dbo.RefreshToken
                SET IsRevoked = 1,
                    Active = 0,
                    ReplacedByTokenHash = @NewTokenHash,
                    RevokedByIp = @RevokedByIp,
                    UpdateDate = GETDATE()
                WHERE TokenHash = @TokenHash AND IsDeleted = 0 AND IsRevoked = 0;

                IF @@ROWCOUNT = 0
                    SET @Status = 1;        -- Lost race
                ELSE
                BEGIN
                    INSERT INTO dbo.RefreshToken
                        (UserId, TokenHash, ExpiryDate, JwtId, CreatedByIp, IsRevoked, Active, IsDeleted, InsertDate, InsertedBy)
                    VALUES
                        (@UserId, @NewTokenHash, @ExpiresAt, @JwtId, @CreatedByIp, 0, 1, 0, GETDATE(), @UserId);
                    SET @Status = 2;        -- Rotated
                END
            END
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH

    SELECT @Status AS Status, ISNULL(@UserId, 0) AS UserId;
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_REFRESH_TOKEN
    @Id bigint = NULL,
    @UserId bigint = NULL,
    @TokenHash varchar(500) = NULL,
    @ExpiryDate datetime = NULL,
    @JwtId varchar(100) = NULL,
    @ReplacedByTokenHash varchar(500) = NULL,
    @CreatedByIp varchar(50) = NULL,
    @RevokedByIp varchar(50) = NULL,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(20) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
    BEGIN
        SELECT TOP (1)
               Id, UserId, TokenHash, ExpiryDate, IsRevoked,
               ISNULL(JwtId, '') AS JwtId, ReplacedByTokenHash, CreatedByIp, RevokedByIp,
               Active, InsertDate, InsertedBy, UpdateDate, UpdatedBy, IsDeleted, DeletedDate, DeletedBy
        FROM dbo.RefreshToken
        WHERE TokenHash = @TokenHash AND IsDeleted = 0;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT INTO dbo.RefreshToken
            (UserId, TokenHash, ExpiryDate, JwtId, CreatedByIp, IsRevoked, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES
            (@UserId, @TokenHash, @ExpiryDate, @JwtId, @CreatedByIp, 0, 1, 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE' OR @NormalizedAction = 'REVOKE'
    BEGIN
        UPDATE dbo.RefreshToken
        SET IsRevoked = 1,
            Active = 0,
            ReplacedByTokenHash = @ReplacedByTokenHash,
            RevokedByIp = @RevokedByIp,
            UpdateDate = GETDATE()
        WHERE Id = @Id AND IsDeleted = 0;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'REVOKEALL'
    BEGIN
        UPDATE dbo.RefreshToken
        SET IsRevoked = 1,
            Active = 0,
            RevokedByIp = @RevokedByIp,
            UpdateDate = GETDATE()
        WHERE UserId = @UserId AND IsRevoked = 0 AND IsDeleted = 0;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'CLEANUP'
    BEGIN
        DELETE FROM dbo.RefreshToken
        WHERE ExpiryDate < DATEADD(day, -30, GETDATE()) AND IsRevoked = 1;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

-- ============================================================================
-- 3. AI Agent Tool Call & Session Stored Procedures
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.usp_AiChatSession_GetById
    @Id uniqueidentifier
AS BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
           Id, UserId, Title, ProjectId, CreatedAt, UpdatedAt,
           Active, IsDeleted, InsertedBy, UpdatedBy, DeletedDate, DeletedBy
    FROM dbo.AiChatSession
    WHERE Id = @Id AND IsDeleted = 0;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_AiAgentToolCall_Complete
    @Id bigint,
    @Status varchar(30),
    @ResultJson nvarchar(max) = NULL,
    @UserId bigint = NULL
AS BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AiAgentToolCall
    SET [Status] = @Status,
        ResultJson = @ResultJson,
        CompletedAt = SYSUTCDATETIME(),
        UpdatedBy = @UserId
    WHERE Id = @Id AND IsDeleted = 0;

    SELECT CONVERT(bigint, @@ROWCOUNT);
END
GO

-- ============================================================================
-- 4. Entity Stored Procedure Enhancements
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_USER
    @Id bigint = NULL,
    @FullName varchar(150) = NULL,
    @Email varchar(200) = NULL,
    @PasswordHash varchar(500) = NULL,
    @RoleId bigint = NULL,
    @DepartmentId bigint = NULL,
    @Active bit = NULL,
    @UserId bigint = NULL,
    @Search varchar(200) = NULL,
    @Value varchar(200) = NULL,
    @Max int = 5,
    @DurationSeconds int = 900,
    @Page int = 1,
    @PageSize int = 50,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'FETCH'
    BEGIN
        IF @Id IS NOT NULL
            SELECT * FROM dbo.[User] WHERE Id = @Id AND IsDeleted = 0;
        ELSE IF @Value IS NOT NULL
            SELECT * FROM dbo.[User] WHERE (Email = @Value OR FullName = @Value) AND IsDeleted = 0;
        ELSE IF @Email IS NOT NULL
            SELECT * FROM dbo.[User] WHERE Email = @Email AND IsDeleted = 0;
    END
    ELSE IF @NormalizedAction = 'PAGED'
    BEGIN
        SELECT COUNT_BIG(1) FROM dbo.[User]
        WHERE IsDeleted = 0 AND (@Search IS NULL OR FullName LIKE '%' + @Search + '%' OR Email LIKE '%' + @Search + '%');

        SELECT * FROM dbo.[User]
        WHERE IsDeleted = 0 AND (@Search IS NULL OR FullName LIKE '%' + @Search + '%' OR Email LIKE '%' + @Search + '%')
        ORDER BY Id DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE IF @NormalizedAction = 'INSERT'
    BEGIN
        INSERT INTO dbo.[User](FullName, Email, PasswordHash, RoleId, DepartmentId, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@FullName, @Email, @PasswordHash, @RoleId, @DepartmentId, ISNULL(@Active, 1), 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.[User]
        SET FullName = @FullName,
            Email = @Email,
            PasswordHash = ISNULL(@PasswordHash, PasswordHash),
            RoleId = ISNULL(@RoleId, RoleId),
            DepartmentId = ISNULL(@DepartmentId, DepartmentId),
            Active = ISNULL(@Active, Active),
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'UPDATEPASSWORD' OR @NormalizedAction = 'UPDATE_PASSWORD'
    BEGIN
        UPDATE dbo.[User]
        SET PasswordHash = @PasswordHash,
            UpdateDate = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'LOGINSUCCESS' OR @NormalizedAction = 'LOGIN_SUCCESS'
    BEGIN
        UPDATE dbo.[User]
        SET FailedLoginAttempts = 0,
            IsLocked = 0,
            LockoutEnd = NULL,
            LastLoginDate = GETDATE()
        WHERE Id = @Id;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'LOGINFAILURE' OR @NormalizedAction = 'LOGIN_FAILURE'
    BEGIN
        UPDATE dbo.[User]
        SET FailedLoginAttempts = FailedLoginAttempts + 1,
            IsLocked = CASE WHEN FailedLoginAttempts + 1 >= @Max THEN 1 ELSE 0 END,
            LockoutEnd = CASE WHEN FailedLoginAttempts + 1 >= @Max THEN DATEADD(second, @DurationSeconds, GETDATE()) ELSE NULL END
        WHERE Id = @Id;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'DELETE'
    BEGIN
        UPDATE dbo.[User]
        SET IsDeleted = 1, Active = 0, DeletedDate = GETDATE(), DeletedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
    ELSE IF @NormalizedAction = 'ROLES'
    BEGIN
        IF @Id IS NOT NULL
            SELECT R.Name FROM dbo.Role R INNER JOIN dbo.[User] U ON U.RoleId = R.Id WHERE U.Id = @Id AND U.IsDeleted = 0;
        ELSE
            SELECT * FROM dbo.Role WHERE IsDeleted = 0;
    END
    ELSE IF @NormalizedAction = 'PERMISSIONS'
    BEGIN
        SELECT P.Name
        FROM dbo.Permission P
        INNER JOIN dbo.RolePermission RP ON RP.PermissionId = P.Id
        INNER JOIN dbo.[User] U ON U.RoleId = RP.RoleId
        WHERE U.Id = @Id AND U.IsDeleted = 0 AND RP.IsDeleted = 0 AND P.IsDeleted = 0;
    END
    ELSE IF @NormalizedAction = 'SETROLE'
    BEGIN
        UPDATE dbo.[User]
        SET RoleId = @RoleId, UpdateDate = GETDATE(), UpdatedBy = @UserId
        WHERE Id = @Id AND IsDeleted = 0;
        SELECT CONVERT(bigint, @@ROWCOUNT);
    END
END
GO

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
        VALUES(@StoryId, @ProjectId, @AssigneeUserId, @Title, @Description, @EstimateHours, @ActualHours, @Status, @Priority, @DueDate, @CompletedDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Task
        SET StoryId = @StoryId, ProjectId = @ProjectId, AssigneeUserId = @AssigneeUserId, Title = @Title,
            Description = @Description, EstimateHours = @EstimateHours, ActualHours = @ActualHours,
            Status = @Status, Priority = @Priority, DueDate = @DueDate, CompletedDate = @CompletedDate,
            Active = ISNULL(@Active, Active), UpdateDate = GETDATE(), UpdatedBy = @UserId
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
        VALUES(@Name, @Description, @Key, @Status, @DepartmentId, @OwnerUserId, @StartDate, @TargetDate, ISNULL(@Active, 1), 0, GETDATE(), @UserId);
        SELECT CONVERT(bigint, SCOPE_IDENTITY());
    END
    ELSE IF @NormalizedAction = 'UPDATE'
    BEGIN
        UPDATE dbo.Project
        SET Name = @Name, Description = @Description, [Key] = @Key, Status = @Status,
            DepartmentId = @DepartmentId, OwnerUserId = @OwnerUserId, StartDate = @StartDate,
            TargetDate = @TargetDate, Active = ISNULL(@Active, Active), UpdateDate = GETDATE(), UpdatedBy = @UserId
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

CREATE OR ALTER PROCEDURE dbo.SP_ADMIN
    @FullName varchar(150) = NULL,
    @Email varchar(200) = NULL,
    @PasswordHash varchar(500) = NULL,
    @Action varchar(30)
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedAction varchar(30) = UPPER(LTRIM(RTRIM(@Action)));

    IF @NormalizedAction = 'BOOTSTRAP'
    BEGIN
        DECLARE @RoleId bigint, @DeptId bigint, @NewUserId bigint;
        SELECT TOP 1 @RoleId = Id FROM dbo.Role WHERE Name = 'Administrator';
        SELECT TOP 1 @DeptId = Id FROM dbo.Department WHERE Active = 1;
        IF @RoleId IS NULL OR @DeptId IS NULL THROW 50000, 'Roles and Departments must be seeded first', 1;
        
        IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE Email = @Email)
        BEGIN
            INSERT dbo.[User](FullName, Email, PasswordHash, RoleId, DepartmentId, Active, IsDeleted, InsertDate)
            VALUES(@FullName, @Email, @PasswordHash, @RoleId, @DeptId, 1, 0, GETDATE());
            SELECT CONVERT(bigint, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            SELECT TOP 1 Id FROM dbo.[User] WHERE Email = @Email;
        END
    END
    ELSE IF @NormalizedAction = 'RESETPASSWORD' OR @NormalizedAction = 'RESET_PASSWORD'
    BEGIN
        UPDATE U
        SET U.PasswordHash = @PasswordHash, U.UpdateDate = GETDATE()
        FROM dbo.[User] U
        INNER JOIN dbo.Role R ON R.Id = U.RoleId
        WHERE R.Name = 'Administrator' AND U.IsDeleted = 0 AND U.Active = 1;
        SELECT CONVERT(int, @@ROWCOUNT);
    END
END
GO

-- ============================================================================
-- 5. Record Schema Migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0021' AND Name = 'SprintWorkflowAndCleanProcedures' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0021', 'SprintWorkflowAndCleanProcedures', SYSUTCDATETIME(), 1);
END
GO
