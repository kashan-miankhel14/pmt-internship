/*
    0008_AuthImprovements.sql

    Updates SP_REFRESH_TOKEN to support JwtId, CreatedByIp, ReplacedByTokenHash, and RevokedByIp.
    Adds token reuse detection and cleanup capabilities.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;

-- Update SP_REFRESH_TOKEN to support new columns
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
AS
BEGIN
    SET NOCOUNT ON;

    IF @Action = 'FETCH'
        SELECT * FROM dbo.RefreshToken WHERE TokenHash = @TokenHash AND IsDeleted = 0;

    ELSE IF @Action = 'INSERT'
        INSERT dbo.RefreshToken(UserId, TokenHash, ExpiryDate, JwtId, CreatedByIp, IsRevoked, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES(@UserId, @TokenHash, @ExpiryDate, @JwtId, @CreatedByIp, 0, 1, 0, GETDATE(), @UserId);

    ELSE IF @Action = 'UPDATE'
        UPDATE dbo.RefreshToken
        SET IsRevoked = 1, Active = 0, ReplacedByTokenHash = @ReplacedByTokenHash, RevokedByIp = @RevokedByIp, UpdateDate = GETDATE()
        WHERE Id = @Id;

    ELSE IF @Action = 'REVOKEALL'
        UPDATE dbo.RefreshToken
        SET IsRevoked = 1, Active = 0, RevokedByIp = @RevokedByIp, UpdateDate = GETDATE()
        WHERE UserId = @UserId AND IsRevoked = 0 AND IsDeleted = 0;

    ELSE IF @Action = 'CLEANUP'
        DELETE FROM dbo.RefreshToken WHERE ExpiryDate < DATEADD(day, -30, GETDATE()) AND IsRevoked = 1;
END
GO

COMMIT;
GO