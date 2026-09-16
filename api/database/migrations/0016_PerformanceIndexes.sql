/*
    0016_PerformanceIndexes.sql

    Fills the remaining index gaps on hot-path lookup columns, focused on the
    Jira-flow membership tables added in 0014_JiraFlowSchema.sql.

    New indexes (previously missing):
        - IX_projectteams_teamid          dbo.ProjectTeams(TeamId)
              Reverse lookup by team. The clustered PK is (ProjectId, TeamId), so
              a query keyed only on TeamId (e.g. SP_TEAM DELETE's referential
              guard, and every "which projects grant this team" read) could not
              seek and scanned the whole grant table.
        - IX_projectteams_projectroleid   dbo.ProjectTeams(ProjectRoleId)
        - IX_projectmembers_projectroleid dbo.ProjectMembers(ProjectRoleId)
        - IX_teams_leaduserid             dbo.Teams(LeadUserId)
              Foreign-key columns that had no backing index, forcing scans on the
              parent-side integrity checks and the membership PAGED joins.

    Already present (verified, intentionally re-guarded so this migration is a
    no-op where they exist and self-heals a DB that is missing them):
        - IX_RefreshToken_TokenHash  dbo.RefreshToken(TokenHash)     -- 0010
        - IX_RefreshToken_UserId     dbo.RefreshToken(UserId)        -- 0001/0009
        - IX_AuditLog_Entity         dbo.AuditLog(EntityName,EntityId) -- 0001/0009
        - IX_AuditLog_UserId         dbo.AuditLog(UserId)            -- 0001/0009

    Conventions (aligned with 0009-0015):
        - Idempotent: every CREATE INDEX is guarded by an sys.indexes existence
          check keyed on (name, object_id).
        - SET QUOTED_IDENTIFIER / ANSI_NULLS ON; NOCOUNT ON; XACT_ABORT ON.
        - Records this migration in dbo.SchemaMigration.

    Must run after 0014_JiraFlowSchema.sql (owns the membership tables).
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. ProjectTeams reverse-lookup and foreign-key indexes
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_projectteams_teamid' AND object_id = OBJECT_ID('dbo.ProjectTeams'))
    CREATE NONCLUSTERED INDEX IX_projectteams_teamid ON dbo.ProjectTeams(TeamId ASC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_projectteams_projectroleid' AND object_id = OBJECT_ID('dbo.ProjectTeams'))
    CREATE NONCLUSTERED INDEX IX_projectteams_projectroleid ON dbo.ProjectTeams(ProjectRoleId ASC);
GO

-- ============================================================================
-- 2. ProjectMembers foreign-key index
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_projectmembers_projectroleid' AND object_id = OBJECT_ID('dbo.ProjectMembers'))
    CREATE NONCLUSTERED INDEX IX_projectmembers_projectroleid ON dbo.ProjectMembers(ProjectRoleId ASC);
GO

-- ============================================================================
-- 3. Teams foreign-key index
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_teams_leaduserid' AND object_id = OBJECT_ID('dbo.Teams'))
    CREATE NONCLUSTERED INDEX IX_teams_leaduserid ON dbo.Teams(LeadUserId ASC);
GO

-- ============================================================================
-- 4. RefreshToken / AuditLog hot-path indexes (guarded no-ops where present)
-- ============================================================================

IF OBJECT_ID('dbo.RefreshToken') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshToken_TokenHash' AND object_id = OBJECT_ID('dbo.RefreshToken'))
    CREATE NONCLUSTERED INDEX IX_RefreshToken_TokenHash ON dbo.RefreshToken(TokenHash ASC) WHERE IsDeleted = 0;

IF OBJECT_ID('dbo.RefreshToken') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshToken_UserId' AND object_id = OBJECT_ID('dbo.RefreshToken'))
    CREATE NONCLUSTERED INDEX IX_RefreshToken_UserId ON dbo.RefreshToken(UserId ASC);
GO

IF OBJECT_ID('dbo.AuditLog') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_Entity' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_Entity ON dbo.AuditLog(EntityName ASC, EntityId ASC);

IF OBJECT_ID('dbo.AuditLog') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_UserId' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_UserId ON dbo.AuditLog(UserId ASC);
GO

-- ============================================================================
-- 5. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0016' AND Name = 'PerformanceIndexes' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0016', 'PerformanceIndexes', SYSUTCDATETIME(), 1);
END
GO
