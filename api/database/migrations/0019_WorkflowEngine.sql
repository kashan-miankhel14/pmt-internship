/*
    0019_WorkflowEngine.sql

    Introduces the real, data-driven workflow engine the way Jira models it: a catalog of
    statuses, a transition graph per workflow, per-project workflow schemes, and a generic
    change-log. The three work-item tables (dbo.Issue, dbo.Task, dbo.UserStory) keep their
    existing status columns and the original hardcoded *StatusRules classes as a fallback; the
    engine becomes the authoritative transition authority whenever a WorkflowScheme row exists
    for the project (every live project is bound to the seeded default in this migration).

    Tables created
    --------------
        1. dbo.Workflow            - a named workflow (e.g. 'Default Jira Workflow').
        2. dbo.WorkflowStatus      - the statuses of a workflow, with a Jira-like category
                                      (ToDo / InProgress / Done) and a unique-ish Code.
        3. dbo.WorkflowTransition  - a directed edge from one status to another, carrying the
                                      optional ConditionJson / ValidatorJson / PostFunctionJson
                                      that makes the engine data-driven.
        4. dbo.WorkflowScheme      - binds a project to a workflow (unique per project).
        5. dbo.IssueHistory        - generic changelog. Despite the name it records changes to
                                      Issues, Tasks and UserStories via (EntityType, EntityId),
                                      which matches the soft/external-key style already used by
                                      dbo.Comment. No enforced FK so the three item types can
                                      share one log without a union table.

    Seed data
    ---------
        - 'Default Jira Workflow' with the 10 status catalog (Backlog, Selected for
          Development, In Progress, Blocked, In Review, Ready for QA, In QA, Done, Closed,
          Reopened) plus a CANCELLED status so the legacy Story/Task/Issue "Cancelled" /
          "Rejected" values map cleanly.
        - The 14 Jira transitions from the spec, plus a documented set of "legacy
          compatibility" transitions that preserve the previously allowed edges of the three
          hardcoded lifecycles (Story/Task/Issue). These are ordinary rows: a project owner who
          wants the strict QA chain can delete them. See the inline legend below.
        - A WorkflowScheme row for every live project pointing at the default workflow, so the
          engine governs status changes everywhere from the moment this migration is applied.

    Stored procedures
    -----------------
        SP_WORKFLOW          - FETCH_BY_PROJECT (scheme -> workflow, default fallback),
                               FETCH_BY_ID.
        SP_WORKFLOW_STATUS   - FETCH_BY_PROJECT (all statuses of the project's workflow),
                               STAMP (record the resolved catalog status on an item).
        SP_WORKFLOW_TRANSITION - FETCH_AVAILABLE (edges leaving a status code),
                                 FETCH_BY_ID, FETCH_ONE.
        SP_WORKFLOW_SCHEME   - FETCH (project's scheme), INSERT.
        SP_ISSUE_HISTORY     - INSERT (a field change), FETCH (history of one entity).

    Conventions (aligned with 0009-0018): idempotent DDL guarded with IF NOT EXISTS / CREATE OR
    ALTER; each object in its own GO batch; audit tail on every new table; soft delete filtered
    to IsDeleted = 0; records this migration in dbo.SchemaMigration.

    MUST RUN AFTER 0018_ProjectCreationFixes.sql (owns dbo.Project). It also relies on
    dbo.Issue (0009) for the seeded history procedure shape. 0015-0018 must be applied to the
    live database together with this script before the engine can be used.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

-- ============================================================================
-- 1. dbo.Workflow
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'Workflow')
BEGIN
    CREATE TABLE dbo.Workflow(
        Id bigint IDENTITY(1,1) NOT NULL,
        Name nvarchar(100) NOT NULL,
        IsDefault bit NOT NULL CONSTRAINT DF_workflow_isdefault DEFAULT ((0)),
        Active bit NOT NULL CONSTRAINT DF_workflow_active DEFAULT ((1)),
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_workflow_isdeleted DEFAULT ((0)),
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_workflow PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_workflow_name')
    ALTER TABLE dbo.Workflow ADD CONSTRAINT CK_workflow_name CHECK (LEN(Name) BETWEEN 1 AND 100);
GO

-- ============================================================================
-- 2. dbo.WorkflowStatus
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'WorkflowStatus')
BEGIN
    CREATE TABLE dbo.WorkflowStatus(
        Id bigint IDENTITY(1,1) NOT NULL,
        WorkflowId bigint NULL,
        Name nvarchar(60) NOT NULL,
        Code varchar(40) NOT NULL,
        Category varchar(20) NOT NULL CONSTRAINT DF_workflowstatus_category DEFAULT ('ToDo'),
        IsInitial bit NOT NULL CONSTRAINT DF_workflowstatus_isinitial DEFAULT ((0)),
        [Order] int NOT NULL CONSTRAINT DF_workflowstatus_order DEFAULT ((0)),
        Active bit NOT NULL CONSTRAINT DF_workflowstatus_active DEFAULT ((1)),
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_workflowstatus_isdeleted DEFAULT ((0)),
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_workflowstatus PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_workflowstatus_category')
    ALTER TABLE dbo.WorkflowStatus ADD CONSTRAINT CK_workflowstatus_category CHECK (Category IN ('ToDo', 'InProgress', 'Done'));

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_workflowstatus_workflow')
    ALTER TABLE dbo.WorkflowStatus ADD CONSTRAINT FK_workflowstatus_workflow FOREIGN KEY (WorkflowId) REFERENCES dbo.Workflow(Id);

-- Unique on (WorkflowId, Code) for live rows. WorkflowId is nullable, but a unique index
-- treats NULLs as equal, so at most one un-bound status may share a Code with a bound one --
-- acceptable because every status in this migration is bound to a workflow.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_workflowstatus_workflow_code' AND object_id = OBJECT_ID('dbo.WorkflowStatus'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_workflowstatus_workflow_code ON dbo.WorkflowStatus(WorkflowId ASC, Code ASC) WHERE IsDeleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_workflowstatus_workflow_order' AND object_id = OBJECT_ID('dbo.WorkflowStatus'))
    CREATE NONCLUSTERED INDEX IX_workflowstatus_workflow_order ON dbo.WorkflowStatus(WorkflowId ASC, [Order] ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 3. dbo.WorkflowTransition
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'WorkflowTransition')
BEGIN
    CREATE TABLE dbo.WorkflowTransition(
        Id bigint IDENTITY(1,1) NOT NULL,
        WorkflowId bigint NOT NULL,
        Name nvarchar(120) NOT NULL,
        FromStatusId bigint NULL,
        ToStatusId bigint NOT NULL,
        ConditionJson nvarchar(max) NULL,
        ValidatorJson nvarchar(max) NULL,
        PostFunctionJson nvarchar(max) NULL,
        [Order] int NOT NULL CONSTRAINT DF_workflowtransition_order DEFAULT ((0)),
        Active bit NOT NULL CONSTRAINT DF_workflowtransition_active DEFAULT ((1)),
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_workflowtransition_isdeleted DEFAULT ((0)),
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_workflowtransition PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_workflowtransition_workflow')
    ALTER TABLE dbo.WorkflowTransition ADD CONSTRAINT FK_workflowtransition_workflow FOREIGN KEY (WorkflowId) REFERENCES dbo.Workflow(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_workflowtransition_from')
    ALTER TABLE dbo.WorkflowTransition ADD CONSTRAINT FK_workflowtransition_from FOREIGN KEY (FromStatusId) REFERENCES dbo.WorkflowStatus(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_workflowtransition_to')
    ALTER TABLE dbo.WorkflowTransition ADD CONSTRAINT FK_workflowtransition_to FOREIGN KEY (ToStatusId) REFERENCES dbo.WorkflowStatus(Id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_workflowtransition_workflow_tostatus' AND object_id = OBJECT_ID('dbo.WorkflowTransition'))
    CREATE NONCLUSTERED INDEX IX_workflowtransition_workflow_tostatus ON dbo.WorkflowTransition(WorkflowId ASC, ToStatusId ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 4. dbo.WorkflowScheme
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'WorkflowScheme')
BEGIN
    CREATE TABLE dbo.WorkflowScheme(
        Id bigint IDENTITY(1,1) NOT NULL,
        ProjectId bigint NOT NULL,
        WorkflowId bigint NOT NULL,
        Active bit NOT NULL CONSTRAINT DF_workflowscheme_active DEFAULT ((1)),
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_workflowscheme_isdeleted DEFAULT ((0)),
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_workflowscheme PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_workflowscheme_project')
    ALTER TABLE dbo.WorkflowScheme ADD CONSTRAINT FK_workflowscheme_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_workflowscheme_workflow')
    ALTER TABLE dbo.WorkflowScheme ADD CONSTRAINT FK_workflowscheme_workflow FOREIGN KEY (WorkflowId) REFERENCES dbo.Workflow(Id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_workflowscheme_project' AND object_id = OBJECT_ID('dbo.WorkflowScheme'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_workflowscheme_project ON dbo.WorkflowScheme(ProjectId ASC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 5. dbo.IssueHistory
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'dbo' AND t.name = 'IssueHistory')
BEGIN
    CREATE TABLE dbo.IssueHistory(
        Id bigint IDENTITY(1,1) NOT NULL,
        EntityType varchar(20) NOT NULL CONSTRAINT DF_issuehistory_entitytype DEFAULT ('Issue'),
        EntityId bigint NOT NULL,
        WorkflowTransitionId bigint NULL,
        FieldName nvarchar(60) NOT NULL,
        OldValue nvarchar(max) NULL,
        NewValue nvarchar(max) NULL,
        Comment nvarchar(max) NULL,
        ChangedByUser bigint NULL,
        ChangedAtUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertDate datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        InsertedBy bigint NULL,
        UpdateDate datetime2(3) NULL,
        UpdatedBy bigint NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_issuehistory_isdeleted DEFAULT ((0)),
        DeletedDate datetime2(3) NULL,
        DeletedBy bigint NULL,
        CONSTRAINT PK_issuehistory PRIMARY KEY CLUSTERED (Id ASC)
    ) ON [PRIMARY];
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_issuehistory_entitytype')
    ALTER TABLE dbo.IssueHistory ADD CONSTRAINT CK_issuehistory_entitytype CHECK (EntityType IN ('Issue', 'Task', 'UserStory'));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_issuehistory_entity' AND object_id = OBJECT_ID('dbo.IssueHistory'))
    CREATE NONCLUSTERED INDEX IX_issuehistory_entity ON dbo.IssueHistory(EntityType ASC, EntityId ASC, ChangedAtUtc DESC) WHERE IsDeleted = 0;
GO

-- ============================================================================
-- 6. Seed: Workflow + Status catalog
-- ============================================================================

DECLARE @WorkflowId bigint;

IF NOT EXISTS (SELECT 1 FROM dbo.Workflow WHERE Name = 'Default Jira Workflow' AND IsDeleted = 0)
BEGIN
    INSERT dbo.Workflow (Name, IsDefault, Active, IsDeleted, InsertDate, InsertedBy)
    VALUES ('Default Jira Workflow', 1, 1, 0, SYSUTCDATETIME(), NULL);
    SET @WorkflowId = CONVERT(bigint, SCOPE_IDENTITY());
END
ELSE
    SELECT @WorkflowId = Id FROM dbo.Workflow WHERE Name = 'Default Jira Workflow' AND IsDeleted = 0;

IF NOT EXISTS (SELECT 1 FROM dbo.WorkflowStatus WHERE WorkflowId = @WorkflowId AND IsDeleted = 0)
BEGIN
    INSERT dbo.WorkflowStatus (WorkflowId, Name, Code, Category, IsInitial, [Order], Active, IsDeleted, InsertDate, InsertedBy)
    VALUES
        (@WorkflowId, N'Backlog',                 'BACKLOG',       'ToDo',       1, 10, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'Selected for Development', 'SELECTED',      'ToDo',       0, 20, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'In Progress',              'IN_PROGRESS',   'InProgress', 0, 30, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'Blocked',                 'BLOCKED',       'InProgress', 0, 35, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'In Review',               'IN_REVIEW',     'InProgress', 0, 40, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'Ready for QA',            'READY_FOR_QA',  'InProgress', 0, 50, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'In QA',                   'IN_QA',         'InProgress', 0, 60, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'Done',                    'DONE',          'Done',       0, 70, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'Closed',                  'CLOSED',        'Done',       0, 80, 1, 0, SYSUTCDATETIME(), NULL),
        (@WorkflowId, N'Reopened',                'REOPENED',      'InProgress', 0, 90, 1, 0, SYSUTCDATETIME(), NULL),
        -- CANCELLED is a pragmatic 11th status so the legacy Story/Task "Cancelled" and
        -- Issue "Rejected" values map cleanly to the catalog instead of colliding with CLOSED.
        (@WorkflowId, N'Cancelled',               'CANCELLED',     'Done',       0, 100, 1, 0, SYSUTCDATETIME(), NULL);
END
GO

-- ============================================================================
-- 7. Seed: Transitions
--
--    Two groups, all ordinary rows in dbo.WorkflowTransition:
--      * SPEC        - the 14 transitions from the brief.
--      * COMPAT      - legacy edges from the three hardcoded lifecycles so existing status
--                      changes keep working after this migration. Project owners who want the
--                      strict QA chain can delete the COMPAT rows.
--    A transition's validators/conditions are expressed as JSON objects whose "name" keys are
--    resolved by handlers in WorkflowEngine (SubTasksResolved, CommentRequired, RequiredField).
-- ============================================================================

DECLARE @WfId bigint = (SELECT Id FROM dbo.Workflow WHERE Name = 'Default Jira Workflow' AND IsDeleted = 0);

DECLARE @StatusId TABLE (Code varchar(40) PRIMARY KEY, Id bigint NOT NULL);
INSERT @StatusId (Code, Id)
SELECT Code, Id FROM dbo.WorkflowStatus WHERE WorkflowId = @WfId AND IsDeleted = 0;

DECLARE @Seed TABLE (
    GroupName varchar(10) NOT NULL,
    Name nvarchar(120) NOT NULL,
    FromCode varchar(40) NULL,
    ToCode varchar(40) NOT NULL,
    [Order] int NOT NULL,
    ConditionJson nvarchar(max) NULL,
    ValidatorJson nvarchar(max) NULL
);

INSERT @Seed (GroupName, Name, FromCode, ToCode, [Order], ConditionJson, ValidatorJson)
VALUES
    -- ---- SPEC transitions (per the workflow brief) -----------------------
    ('SPEC', N'Start Development',       'BACKLOG',     'SELECTED',    10, NULL, NULL),
    ('SPEC', N'Begin Work',              'SELECTED',    'IN_PROGRESS', 11, NULL, NULL),
    ('SPEC', N'Send to Review',          'IN_PROGRESS', 'IN_REVIEW',   12, NULL, NULL),
    ('SPEC', N'Reopen from Review',      'IN_REVIEW',   'IN_PROGRESS', 13, NULL, NULL),
    ('SPEC', N'Ready for QA',            'IN_REVIEW',   'READY_FOR_QA',14, NULL, NULL),
    ('SPEC', N'Start QA',                'READY_FOR_QA','IN_QA',       15, NULL, NULL),
    ('SPEC', N'QA Passed',               'IN_QA',       'DONE',        16,
        N'{"name":"SubTasksResolved"}',
        N'{"name":"RequiredField","field":"resolution","message":"Resolution is required to complete an item."}'),
    ('SPEC', N'Block',                   'IN_PROGRESS', 'BLOCKED',     17, NULL,
        N'{"name":"CommentRequired","message":"A comment explaining the blocker is required."}'),
    ('SPEC', N'Unblock',                 'BLOCKED',     'IN_PROGRESS', 18, NULL, NULL),
    ('SPEC', N'Close',                   'DONE',        'CLOSED',      19, NULL, NULL),
    ('SPEC', N'Reopen',                  'CLOSED',      'REOPENED',    20, NULL, NULL),
    ('SPEC', N'Reopen from Reopened',    'REOPENED',    'IN_PROGRESS', 21, NULL, NULL),
    ('SPEC', N'QA Scrap / Rework',       'IN_QA',       'READY_FOR_QA',22, NULL, NULL),
    ('SPEC', N'Reject at QA',            'IN_QA',       'IN_REVIEW',   23, NULL, NULL),

    -- ---- COMPAT transitions (preserve legacy Story/Task/Issue lifecycles) --
    -- Story: Backlog <-> Ready, Ready/InProgress -> Cancelled, InProgress -> Ready
    ('COMPAT', N'Backlog to Ready (legacy)',     'BACKLOG',     'SELECTED',    910, NULL, NULL),
    ('COMPAT', N'Ready to Backlog (legacy)',     'SELECTED',    'BACKLOG',     911, NULL, NULL),
    ('COMPAT', N'Ready to Cancelled (legacy)',   'SELECTED',    'CANCELLED',   912, NULL, NULL),
    ('COMPAT', N'In Progress to Ready (legacy)', 'IN_PROGRESS', 'SELECTED',    913, NULL, NULL),
    ('COMPAT', N'In Progress to Cancelled (legacy)','IN_PROGRESS','CANCELLED', 914, NULL, NULL),
    -- Story: In Review -> Done directly (legacy; avoids forcing QA stages on stories)
    ('COMPAT', N'Review to Done (legacy)',       'IN_REVIEW',   'DONE',        915, NULL, NULL),
    -- Story/Task: Cancelled -> Backlog (un-cancel)
    ('COMPAT', N'Cancel to Backlog (legacy)',    'CANCELLED',   'BACKLOG',     916, NULL, NULL),
    -- Task: ToDo/Blocked -> Cancelled, Blocked -> Review, Review -> Blocked
    ('COMPAT', N'ToDo to Cancelled (legacy)',    'BACKLOG',     'CANCELLED',   917, NULL, NULL),
    ('COMPAT', N'Blocked to Review (legacy)',    'BLOCKED',     'IN_REVIEW',   918, NULL, NULL),
    ('COMPAT', N'Review to Blocked (legacy)',    'IN_REVIEW',   'BLOCKED',     919, NULL, NULL),
    -- Issue: Open -> Rejected, Rejected -> Open, Resolved -> Open, Resolved -> Closed
    ('COMPAT', N'Open to Rejected (legacy)',     'BACKLOG',     'CANCELLED',   920, NULL, NULL),
    ('COMPAT', N'Rejected to Open (legacy)',     'CANCELLED',   'BACKLOG',     921, NULL, NULL),
    ('COMPAT', N'Resolved to Open (legacy)',     'DONE',        'BACKLOG',     922, NULL, NULL),
    ('COMPAT', N'Reopen Issue (legacy)',         'CLOSED',      'BACKLOG',     923, NULL, NULL);

INSERT dbo.WorkflowTransition (WorkflowId, Name, FromStatusId, ToStatusId, ConditionJson, ValidatorJson, [Order], Active, IsDeleted, InsertDate, InsertedBy)
SELECT
    @WfId,
    s.Name,
    fs.Id,
    ts.Id,
    s.ConditionJson,
    s.ValidatorJson,
    s.[Order],
    1, 0, SYSUTCDATETIME(), NULL
FROM @Seed s
JOIN @StatusId fs ON fs.Code = s.FromCode
JOIN @StatusId ts ON ts.Code = s.ToCode
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.WorkflowTransition t
    WHERE t.WorkflowId = @WfId AND t.FromStatusId = fs.Id AND t.ToStatusId = ts.Id AND t.IsDeleted = 0
);
GO

-- ============================================================================
-- 8. Seed: WorkflowScheme for every live project
-- ============================================================================

DECLARE @DefaultWorkflowId bigint = (SELECT Id FROM dbo.Workflow WHERE Name = 'Default Jira Workflow' AND IsDeleted = 0);

IF OBJECT_ID('dbo.Project') IS NOT NULL AND @DefaultWorkflowId IS NOT NULL
BEGIN
    INSERT dbo.WorkflowScheme (ProjectId, WorkflowId, Active, IsDeleted, InsertDate, InsertedBy)
    SELECT p.Id, @DefaultWorkflowId, 1, 0, SYSUTCDATETIME(), NULL
    FROM dbo.Project p
    WHERE p.IsDeleted = 0
      AND NOT EXISTS (
        SELECT 1 FROM dbo.WorkflowScheme ws WHERE ws.ProjectId = p.Id AND ws.IsDeleted = 0
      );
END
GO

-- ============================================================================
-- 9. SP_WORKFLOW
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_WORKFLOW
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;

    IF @Action = 'FETCH_BY_PROJECT'
    BEGIN
        -- Project's bound workflow, falling back to the default when no scheme exists.
        SELECT TOP (1) w.Id, w.Name, w.IsDefault
        FROM dbo.Workflow w
        LEFT JOIN dbo.WorkflowScheme ws ON ws.ProjectId = @ProjectId AND ws.IsDeleted = 0
        WHERE w.IsDeleted = 0
          AND (w.Id = ws.WorkflowId OR (ws.WorkflowId IS NULL AND w.IsDefault = 1))
        ORDER BY CASE WHEN w.Id = ws.WorkflowId THEN 0 ELSE 1 END, w.Id;
        RETURN;
    END

    IF @Action = 'FETCH_BY_ID'
    BEGIN
        SELECT Id, Name, IsDefault
        FROM dbo.Workflow
        WHERE Id = @Id AND IsDeleted = 0;
        RETURN;
    END
END
GO

-- ============================================================================
-- 10. Breadcrumb column on the three work-item tables
--
--    WorkflowStatusId is a denormalised, non-enforced link to dbo.WorkflowStatus so the
--    richer catalog status (e.g. In QA) is observable even though the legacy status enum
--    column cannot express it. The authoritative status stays in the enum column; this is
--    updated by the engine's STAMP action. The existing SPs (SP_ISSUE etc.) never select or
--    write it, so adding the column is fully backward compatible.
--
--    IMPORTANT: this must run BEFORE SP_WORKFLOW_STATUS (section 11), whose STAMP branch
--    writes these columns -- SQL Server validates column references in created procedure
--    bodies, so the columns must already exist.
-- ============================================================================

IF OBJECT_ID('dbo.Issue') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Issue') AND name = 'WorkflowStatusId')
    ALTER TABLE dbo.Issue ADD WorkflowStatusId bigint NULL;

IF OBJECT_ID('dbo.Task') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Task') AND name = 'WorkflowStatusId')
    ALTER TABLE dbo.Task ADD WorkflowStatusId bigint NULL;

IF OBJECT_ID('dbo.UserStory') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserStory') AND name = 'WorkflowStatusId')
    ALTER TABLE dbo.UserStory ADD WorkflowStatusId bigint NULL;
GO

-- ============================================================================
-- 11. SP_WORKFLOW_STATUS
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_WORKFLOW_STATUS
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @EntityType varchar(20) = NULL,
    @EntityId bigint = NULL,
    @StatusCode varchar(40) = NULL,
    @WorkflowStatusId bigint = NULL,
    @UserId bigint = NULL,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;

    IF @Action = 'FETCH_BY_PROJECT'
    BEGIN
        SELECT ws.Id, ws.WorkflowId, ws.Name, ws.Code, ws.Category, ws.IsInitial, ws.[Order]
        FROM dbo.WorkflowStatus ws
        JOIN dbo.Workflow w ON w.Id = ws.WorkflowId
        LEFT JOIN dbo.WorkflowScheme sc ON sc.ProjectId = @ProjectId AND sc.IsDeleted = 0
        WHERE ws.IsDeleted = 0
          AND (ws.WorkflowId = sc.WorkflowId OR (sc.WorkflowId IS NULL AND w.IsDefault = 1))
        ORDER BY ws.[Order], ws.Id;
        RETURN;
    END

    IF @Action = 'STAMP'
    BEGIN
        -- Records the resolved catalog status on a work item. No enforced FK: the three item
        -- tables are not altered by this migration, so this is an audit/denormalised breadcrumb
        -- only. A NULL WorkflowStatusId simply means "no catalog status".
        IF @EntityType IS NULL OR @EntityId IS NULL
            RETURN;

        IF @EntityType = 'UserStory' AND OBJECT_ID('dbo.UserStory') IS NOT NULL
            UPDATE dbo.UserStory SET WorkflowStatusId = @WorkflowStatusId, UpdateDate = GETDATE(), UpdatedBy = @UserId WHERE Id = @EntityId AND IsDeleted = 0;
        ELSE IF @EntityType = 'Task' AND OBJECT_ID('dbo.Task') IS NOT NULL
            UPDATE dbo.Task SET WorkflowStatusId = @WorkflowStatusId, UpdateDate = GETDATE(), UpdatedBy = @UserId WHERE Id = @EntityId AND IsDeleted = 0;
        ELSE IF @EntityType = 'Issue' AND OBJECT_ID('dbo.Issue') IS NOT NULL
            UPDATE dbo.Issue SET WorkflowStatusId = @WorkflowStatusId, UpdateDate = GETDATE(), UpdatedBy = @UserId WHERE Id = @EntityId AND IsDeleted = 0;

        SELECT CONVERT(bigint, @@ROWCOUNT);
        RETURN;
    END
END
GO

-- ============================================================================
-- 12. SP_WORKFLOW_TRANSITION
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_WORKFLOW_TRANSITION
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @FromStatusCode varchar(40) = NULL,
    @ToStatusCode varchar(40) = NULL,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;

    IF @Action = 'FETCH_AVAILABLE'
    BEGIN
        -- Every live transition of the project's workflow whose source matches the current
        -- status code. NULL FromStatusId means "from anywhere" and always matches.
        SELECT t.Id, t.WorkflowId, t.Name, t.FromStatusId, t.ToStatusId,
               t.ConditionJson, t.ValidatorJson, t.PostFunctionJson, t.[Order],
               fs.Code AS FromStatusCode, fs.Name AS FromStatusName,
               ts.Code AS ToStatusCode, ts.Name AS ToStatusName, ts.Category AS ToStatusCategory
        FROM dbo.WorkflowTransition t
        JOIN dbo.Workflow w ON w.Id = t.WorkflowId
        LEFT JOIN dbo.WorkflowScheme sc ON sc.ProjectId = @ProjectId AND sc.IsDeleted = 0
        LEFT JOIN dbo.WorkflowStatus fs ON fs.Id = t.FromStatusId
        JOIN dbo.WorkflowStatus ts ON ts.Id = t.ToStatusId
        WHERE t.IsDeleted = 0
          AND (t.WorkflowId = sc.WorkflowId OR (sc.WorkflowId IS NULL AND w.IsDefault = 1))
          AND (fs.Code = @FromStatusCode OR t.FromStatusId IS NULL OR @FromStatusCode IS NULL)
        ORDER BY t.[Order], t.Id;
        RETURN;
    END

    IF @Action = 'FETCH_BY_ID'
    BEGIN
        SELECT t.Id, t.WorkflowId, t.Name, t.FromStatusId, t.ToStatusId,
               t.ConditionJson, t.ValidatorJson, t.PostFunctionJson, t.[Order],
               fs.Code AS FromStatusCode, fs.Name AS FromStatusName,
               ts.Code AS ToStatusCode, ts.Name AS ToStatusName, ts.Category AS ToStatusCategory
        FROM dbo.WorkflowTransition t
        LEFT JOIN dbo.WorkflowStatus fs ON fs.Id = t.FromStatusId
        JOIN dbo.WorkflowStatus ts ON ts.Id = t.ToStatusId
        WHERE t.Id = @Id AND t.IsDeleted = 0;
        RETURN;
    END

    IF @Action = 'FETCH_ONE'
    BEGIN
        SELECT t.Id, t.WorkflowId, t.Name, t.FromStatusId, t.ToStatusId,
               t.ConditionJson, t.ValidatorJson, t.PostFunctionJson, t.[Order],
               fs.Code AS FromStatusCode, fs.Name AS FromStatusName,
               ts.Code AS ToStatusCode, ts.Name AS ToStatusName, ts.Category AS ToStatusCategory
        FROM dbo.WorkflowTransition t
        JOIN dbo.WorkflowStatus fs ON fs.Id = t.FromStatusId
        JOIN dbo.WorkflowStatus ts ON ts.Id = t.ToStatusId
        WHERE t.IsDeleted = 0
          AND fs.Code = @FromStatusCode AND ts.Code = @ToStatusCode
          AND EXISTS (
            SELECT 1 FROM dbo.Workflow w
            LEFT JOIN dbo.WorkflowScheme sc ON sc.ProjectId = @ProjectId AND sc.IsDeleted = 0
            WHERE w.Id = t.WorkflowId
              AND (t.WorkflowId = sc.WorkflowId OR (sc.WorkflowId IS NULL AND w.IsDefault = 1))
          );
        RETURN;
    END
END
GO

-- ============================================================================
-- 12. SP_WORKFLOW_SCHEME
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_WORKFLOW_SCHEME
    @Id bigint = NULL,
    @ProjectId bigint = NULL,
    @WorkflowId bigint = NULL,
    @UserId bigint = NULL,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;

    IF @Action = 'FETCH'
    BEGIN
        SELECT ws.Id, ws.ProjectId, ws.WorkflowId, w.Name AS WorkflowName
        FROM dbo.WorkflowScheme ws
        JOIN dbo.Workflow w ON w.Id = ws.WorkflowId
        WHERE ws.ProjectId = @ProjectId AND ws.IsDeleted = 0;
        RETURN;
    END

    IF @Action = 'INSERT'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Project WHERE Id = @ProjectId AND IsDeleted = 0)
            THROW 50000, 'SP_WORKFLOW_SCHEME INSERT: project not found or deleted.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Workflow WHERE Id = @WorkflowId AND IsDeleted = 0)
            THROW 50000, 'SP_WORKFLOW_SCHEME INSERT: workflow not found or deleted.', 1;
        IF EXISTS (SELECT 1 FROM dbo.WorkflowScheme WHERE ProjectId = @ProjectId AND IsDeleted = 0)
            THROW 50000, 'This project already has a workflow scheme.', 1;

        INSERT dbo.WorkflowScheme (ProjectId, WorkflowId, Active, IsDeleted, InsertDate, InsertedBy)
        VALUES (@ProjectId, @WorkflowId, 1, 0, SYSUTCDATETIME(), @UserId);

        SELECT CONVERT(bigint, SCOPE_IDENTITY());
        RETURN;
    END
END
GO

-- ============================================================================
-- 13. SP_ISSUE_HISTORY
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.SP_ISSUE_HISTORY
    @Id bigint = NULL,
    @EntityType varchar(20) = NULL,
    @EntityId bigint = NULL,
    @WorkflowTransitionId bigint = NULL,
    @FieldName nvarchar(60) = NULL,
    @OldValue nvarchar(max) = NULL,
    @NewValue nvarchar(max) = NULL,
    @Comment nvarchar(max) = NULL,
    @ChangedByUser bigint = NULL,
    @UserId bigint = NULL,
    @Action varchar(20)
AS BEGIN
    SET NOCOUNT ON;

    IF @Action = 'INSERT'
    BEGIN
        IF NULLIF(LTRIM(RTRIM(ISNULL(@FieldName, N''))), N'') IS NULL
            THROW 50000, 'SP_ISSUE_HISTORY INSERT requires @FieldName.', 1;
        IF @EntityId IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(@EntityType, N''))), N'') IS NULL
            THROW 50000, 'SP_ISSUE_HISTORY INSERT requires @EntityType and @EntityId.', 1;

        INSERT dbo.IssueHistory (EntityType, EntityId, WorkflowTransitionId, FieldName, OldValue, NewValue, Comment, ChangedByUser, InsertDate, InsertedBy)
        VALUES (@EntityType, @EntityId, @WorkflowTransitionId, @FieldName, @OldValue, @NewValue, @Comment, @ChangedByUser, SYSUTCDATETIME(), @UserId);

        SELECT CONVERT(bigint, SCOPE_IDENTITY());
        RETURN;
    END

    IF @Action = 'FETCH'
    BEGIN
        SELECT Id, EntityType, EntityId, WorkflowTransitionId, FieldName, OldValue, NewValue, Comment, ChangedByUser, ChangedAtUtc
        FROM dbo.IssueHistory
        WHERE IsDeleted = 0
          AND (@EntityType IS NULL OR EntityType = @EntityType)
          AND (@EntityId IS NULL OR EntityId = @EntityId)
        ORDER BY ChangedAtUtc DESC, Id DESC;
        RETURN;
    END
END
GO

-- ============================================================================
-- 14. Record this migration
-- ============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchemaMigration')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version = '0019' AND Name = 'WorkflowEngine' AND IsDeleted = 0)
        INSERT INTO dbo.SchemaMigration (Version, Name, AppliedAt, Success)
        VALUES ('0019', 'WorkflowEngine', SYSUTCDATETIME(), 1);
END
GO
