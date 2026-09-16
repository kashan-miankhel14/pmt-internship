/*
    0001_InitialSchema.sql

    Creates the PMT schema. This mirrors the schema that the dbo.SP_* stored procedures in
    0003_StoredProcedures.sql query, which is what the Dapper repositories in
    PMT.Infrastructure actually call. Table and column names are PascalCase.

    HISTORY: an earlier version of this file created a different, snake_case schema
    ([USER], [USER_ROLE], [PERMISSION].[KEY], ...) that was never deployed and that no stored
    procedure or repository ever referenced. It has been replaced with the schema that is
    really in use, so this folder can rebuild a working database from scratch.

    Conventions:
      - Every table carries the audit tail: Active, InsertDate, InsertedBy, UpdateDate,
        UpdatedBy, IsDeleted, DeletedDate, DeletedBy.
      - Deletes are soft (IsDeleted = 1); the stored procedures filter on IsDeleted = 0.
      - Uniqueness is enforced by filtered indexes (WHERE IsDeleted = 0) so a soft-deleted
        row does not block reuse of its name/email.
      - Guarded with IF OBJECT_ID so the script is safe to re-run.

    Run order: 0001 -> 0002 -> 0003 -> 0004.
*/

SET QUOTED_IDENTIFIER ON;  /* required by the filtered indexes below */
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;
GO

/* ===========================================================================
   Reference tables (no dependencies)
   =========================================================================== */

IF OBJECT_ID('dbo.Department', 'U') IS NULL
CREATE TABLE dbo.Department (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_department PRIMARY KEY,
    Name        VARCHAR(150) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_department_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_department_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL
);
GO

IF OBJECT_ID('dbo.Role', 'U') IS NULL
CREATE TABLE dbo.Role (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_role PRIMARY KEY,
    Name        VARCHAR(100) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_role_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_role_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL
);
GO

/*
    Permission.Name holds the authorization key itself ('projects.view', 'tasks.manage', ...).
    These strings are the contract with PMT.Application.Common.Security.PermissionRequirement:
    SP_USER @Action='PERMISSIONS' returns this column, AuthService copies it into the JWT's
    "permission" claims, and the policies match on it with RequireClaim. Values here MUST stay
    identical to PermissionRequirement.All or every [Authorize(Policy=...)] endpoint returns 403.
*/
IF OBJECT_ID('dbo.Permission', 'U') IS NULL
CREATE TABLE dbo.Permission (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_permission PRIMARY KEY,
    Name        VARCHAR(100) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_permission_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_permission_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL
);
GO

IF OBJECT_ID('dbo.RolePermission', 'U') IS NULL
CREATE TABLE dbo.RolePermission (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rolepermission PRIMARY KEY,
    RoleId       BIGINT NOT NULL CONSTRAINT FK_rolepermission_role REFERENCES dbo.Role(Id),
    PermissionId BIGINT NOT NULL CONSTRAINT FK_rolepermission_permission REFERENCES dbo.Permission(Id),
    Active       BIT NOT NULL CONSTRAINT DF_rolepermission_active DEFAULT((1)),
    InsertDate   DATETIME NULL,
    InsertedBy   BIGINT NULL,
    UpdateDate   DATETIME NULL,
    UpdatedBy    BIGINT NULL,
    IsDeleted    BIT NOT NULL CONSTRAINT DF_rolepermission_isdeleted DEFAULT((0)),
    DeletedDate  DATETIME NULL,
    DeletedBy    BIGINT NULL
);
GO

/*
    A user holds exactly one role (User.RoleId). There is deliberately no UserRole join table:
    SP_USER @Action='ROLES'/'SETROLE' and PMT.Domain.Entities.User both assume the single-role model.
*/
IF OBJECT_ID('dbo.[User]', 'U') IS NULL
CREATE TABLE dbo.[User] (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_user PRIMARY KEY,
    FullName     VARCHAR(150) NOT NULL,
    Email        VARCHAR(200) NOT NULL,
    PasswordHash VARCHAR(500) NOT NULL,
    RoleId       BIGINT NOT NULL CONSTRAINT FK_user_role REFERENCES dbo.Role(Id),
    DepartmentId BIGINT NOT NULL CONSTRAINT FK_user_department REFERENCES dbo.Department(Id),
    Active       BIT NOT NULL CONSTRAINT DF_user_active DEFAULT((1)),
    InsertDate   DATETIME NULL,
    InsertedBy   BIGINT NULL,
    UpdateDate   DATETIME NULL,
    UpdatedBy    BIGINT NULL,
    IsDeleted    BIT NOT NULL CONSTRAINT DF_user_isdeleted DEFAULT((0)),
    DeletedDate  DATETIME NULL,
    DeletedBy    BIGINT NULL
);
GO

IF OBJECT_ID('dbo.RefreshToken', 'U') IS NULL
CREATE TABLE dbo.RefreshToken (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_refreshtoken PRIMARY KEY,
    UserId      BIGINT NOT NULL CONSTRAINT FK_refreshtoken_user REFERENCES dbo.[User](Id),
    TokenHash   VARCHAR(500) NOT NULL,
    ExpiryDate  DATETIME NOT NULL,
    IsRevoked   BIT NOT NULL CONSTRAINT DF_refreshtoken_isrevoked DEFAULT((0)),
    Active      BIT NOT NULL CONSTRAINT DF_refreshtoken_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_refreshtoken_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL
);
GO

/* ===========================================================================
   Delivery tables
   =========================================================================== */

IF OBJECT_ID('dbo.Project', 'U') IS NULL
CREATE TABLE dbo.Project (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_project PRIMARY KEY,
    Name         VARCHAR(200) NOT NULL,
    Description  VARCHAR(MAX) NULL,
    Status       VARCHAR(30) NOT NULL CONSTRAINT DF_project_status DEFAULT('Planning'),
    DepartmentId BIGINT NOT NULL CONSTRAINT FK_project_department REFERENCES dbo.Department(Id),
    OwnerUserId  BIGINT NOT NULL CONSTRAINT FK_project_user REFERENCES dbo.[User](Id),
    Active       BIT NOT NULL CONSTRAINT DF_project_active DEFAULT((1)),
    InsertDate   DATETIME NULL,
    InsertedBy   BIGINT NULL,
    UpdateDate   DATETIME NULL,
    UpdatedBy    BIGINT NULL,
    IsDeleted    BIT NOT NULL CONSTRAINT DF_project_isdeleted DEFAULT((0)),
    DeletedDate  DATETIME NULL,
    DeletedBy    BIGINT NULL,
    CONSTRAINT CK_project_status CHECK (Status IN ('Planning','Active','OnHold','Completed','Archived'))
);
GO

IF OBJECT_ID('dbo.UserStory', 'U') IS NULL
CREATE TABLE dbo.UserStory (
    Id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_userstory PRIMARY KEY,
    ProjectId          BIGINT NOT NULL CONSTRAINT FK_userstory_project REFERENCES dbo.Project(Id),
    Title              VARCHAR(250) NOT NULL,
    Description        VARCHAR(MAX) NULL,
    AcceptanceCriteria VARCHAR(MAX) NULL,
    Status             VARCHAR(30) NOT NULL CONSTRAINT DF_userstory_status DEFAULT('Backlog'),
    Active             BIT NOT NULL CONSTRAINT DF_userstory_active DEFAULT((1)),
    InsertDate         DATETIME NULL,
    InsertedBy         BIGINT NULL,
    UpdateDate         DATETIME NULL,
    UpdatedBy          BIGINT NULL,
    IsDeleted          BIT NOT NULL CONSTRAINT DF_userstory_isdeleted DEFAULT((0)),
    DeletedDate        DATETIME NULL,
    DeletedBy          BIGINT NULL,
    CONSTRAINT CK_userstory_status CHECK (Status IN ('Backlog','Ready','InProgress','Review','Done'))
);
GO

IF OBJECT_ID('dbo.Task', 'U') IS NULL
CREATE TABLE dbo.Task (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_task PRIMARY KEY,
    StoryId        BIGINT NOT NULL CONSTRAINT FK_task_userstory REFERENCES dbo.UserStory(Id),
    AssigneeUserId BIGINT NULL CONSTRAINT FK_task_user REFERENCES dbo.[User](Id),
    Title          VARCHAR(250) NOT NULL,
    Description    VARCHAR(MAX) NULL,
    EstimateHours  DECIMAL(6,2) NULL,
    ActualHours    DECIMAL(6,2) NULL,
    Status         VARCHAR(30) NOT NULL CONSTRAINT DF_task_status DEFAULT('ToDo'),
    DueDate        DATE NULL,
    Active         BIT NOT NULL CONSTRAINT DF_task_active DEFAULT((1)),
    InsertDate     DATETIME NULL,
    InsertedBy     BIGINT NULL,
    UpdateDate     DATETIME NULL,
    UpdatedBy      BIGINT NULL,
    IsDeleted      BIT NOT NULL CONSTRAINT DF_task_isdeleted DEFAULT((0)),
    DeletedDate    DATETIME NULL,
    DeletedBy      BIGINT NULL,
    CONSTRAINT CK_task_status CHECK (Status IN ('ToDo','InProgress','Review','Done','Blocked'))
);
GO

IF OBJECT_ID('dbo.Issue', 'U') IS NULL
CREATE TABLE dbo.Issue (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_issue PRIMARY KEY,
    ProjectId        BIGINT NOT NULL CONSTRAINT FK_issue_project REFERENCES dbo.Project(Id),
    TaskId           BIGINT NULL CONSTRAINT FK_issue_task REFERENCES dbo.Task(Id),
    Title            VARCHAR(250) NOT NULL,
    Description      VARCHAR(MAX) NULL,
    Severity         VARCHAR(20) NOT NULL CONSTRAINT DF_issue_severity DEFAULT('Medium'),
    Status           VARCHAR(30) NOT NULL CONSTRAINT DF_issue_status DEFAULT('Open'),
    ReportedByUserId BIGINT NOT NULL CONSTRAINT FK_issue_user REFERENCES dbo.[User](Id),
    Active           BIT NOT NULL CONSTRAINT DF_issue_active DEFAULT((1)),
    InsertDate       DATETIME NULL,
    InsertedBy       BIGINT NULL,
    UpdateDate       DATETIME NULL,
    UpdatedBy        BIGINT NULL,
    IsDeleted        BIT NOT NULL CONSTRAINT DF_issue_isdeleted DEFAULT((0)),
    DeletedDate      DATETIME NULL,
    DeletedBy        BIGINT NULL,
    CONSTRAINT CK_issue_severity CHECK (Severity IN ('Low','Medium','High','Critical')),
    CONSTRAINT CK_issue_status CHECK (Status IN ('Open','InProgress','Resolved','Closed','Reopened'))
);
GO

/* ===========================================================================
   Collaboration tables

   Comment, Attachment and GitLink each hang off either a Task or an Issue; the
   CK_*_parentrequired checks enforce that at least one parent is present.
   =========================================================================== */

IF OBJECT_ID('dbo.Comment', 'U') IS NULL
CREATE TABLE dbo.Comment (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_comment PRIMARY KEY,
    TaskId      BIGINT NULL CONSTRAINT FK_comment_task REFERENCES dbo.Task(Id),
    IssueId     BIGINT NULL CONSTRAINT FK_comment_issue REFERENCES dbo.Issue(Id),
    UserId      BIGINT NOT NULL CONSTRAINT FK_comment_user REFERENCES dbo.[User](Id),
    Content     VARCHAR(MAX) NOT NULL,
    Active      BIT NOT NULL CONSTRAINT DF_comment_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_comment_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL,
    CONSTRAINT CK_comment_parentrequired CHECK (ProjectId IS NOT NULL OR UserStoryId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL)
);
GO

IF OBJECT_ID('dbo.Attachment', 'U') IS NULL
CREATE TABLE dbo.Attachment (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_attachment PRIMARY KEY,
    TaskId           BIGINT NULL CONSTRAINT FK_attachment_task REFERENCES dbo.Task(Id),
    IssueId          BIGINT NULL CONSTRAINT FK_attachment_issue REFERENCES dbo.Issue(Id),
    FileName         VARCHAR(255) NOT NULL,
    FilePath         VARCHAR(500) NOT NULL,
    FileSizeKb       INT NULL,
    UploadedByUserId BIGINT NOT NULL CONSTRAINT FK_attachment_user REFERENCES dbo.[User](Id),
    Active           BIT NOT NULL CONSTRAINT DF_attachment_active DEFAULT((1)),
    InsertDate       DATETIME NULL,
    InsertedBy       BIGINT NULL,
    UpdateDate       DATETIME NULL,
    UpdatedBy        BIGINT NULL,
    IsDeleted        BIT NOT NULL CONSTRAINT DF_attachment_isdeleted DEFAULT((0)),
    DeletedDate      DATETIME NULL,
    DeletedBy        BIGINT NULL,
    CONSTRAINT CK_attachment_parentrequired CHECK (TaskId IS NOT NULL OR IssueId IS NOT NULL)
);
GO

IF OBJECT_ID('dbo.GitLink', 'U') IS NULL
CREATE TABLE dbo.GitLink (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_gitlink PRIMARY KEY,
    TaskId         BIGINT NULL CONSTRAINT FK_gitlink_task REFERENCES dbo.Task(Id),
    IssueId        BIGINT NULL CONSTRAINT FK_gitlink_issue REFERENCES dbo.Issue(Id),
    Provider       VARCHAR(30) NOT NULL,
    CommitSha      VARCHAR(100) NULL,
    PullRequestUrl VARCHAR(500) NULL,
    Active         BIT NOT NULL CONSTRAINT DF_gitlink_active DEFAULT((1)),
    InsertDate     DATETIME NULL,
    InsertedBy     BIGINT NULL,
    UpdateDate     DATETIME NULL,
    UpdatedBy      BIGINT NULL,
    IsDeleted      BIT NOT NULL CONSTRAINT DF_gitlink_isdeleted DEFAULT((0)),
    DeletedDate    DATETIME NULL,
    DeletedBy      BIGINT NULL,
    CONSTRAINT CK_gitlink_parentrequired CHECK (TaskId IS NOT NULL OR IssueId IS NOT NULL),
    CONSTRAINT CK_gitlink_provider CHECK (Provider IN ('GitHub','GitLab','Bitbucket'))
);
GO

/* ===========================================================================
   Notification tables
   =========================================================================== */

IF OBJECT_ID('dbo.Notification', 'U') IS NULL
CREATE TABLE dbo.Notification (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_notification PRIMARY KEY,
    UserId      BIGINT NOT NULL CONSTRAINT FK_notification_user REFERENCES dbo.[User](Id),
    EventType   VARCHAR(50) NOT NULL,
    Message     VARCHAR(500) NOT NULL,
    IsRead      BIT NOT NULL CONSTRAINT DF_notification_isread DEFAULT((0)),
    Active      BIT NOT NULL CONSTRAINT DF_notification_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_notification_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL
);
GO

IF OBJECT_ID('dbo.NotificationPreference', 'U') IS NULL
CREATE TABLE dbo.NotificationPreference (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_notificationpreference PRIMARY KEY,
    UserId       BIGINT NOT NULL CONSTRAINT FK_notificationpreference_user REFERENCES dbo.[User](Id),
    EventType    VARCHAR(50) NOT NULL,
    InAppEnabled BIT NOT NULL CONSTRAINT DF_notificationpreference_inappenabled DEFAULT((1)),
    EmailEnabled BIT NOT NULL CONSTRAINT DF_notificationpreference_emailenabled DEFAULT((1)),
    Active       BIT NOT NULL CONSTRAINT DF_notificationpreference_active DEFAULT((1)),
    InsertDate   DATETIME NULL,
    InsertedBy   BIGINT NULL,
    UpdateDate   DATETIME NULL,
    UpdatedBy    BIGINT NULL,
    IsDeleted    BIT NOT NULL CONSTRAINT DF_notificationpreference_isdeleted DEFAULT((0)),
    DeletedDate  DATETIME NULL,
    DeletedBy    BIGINT NULL
);
GO

/* ===========================================================================
   Operational tables
   =========================================================================== */

IF OBJECT_ID('dbo.AuditLog', 'U') IS NULL
CREATE TABLE dbo.AuditLog (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_auditlog PRIMARY KEY,
    EntityName  VARCHAR(100) NOT NULL,
    EntityId    BIGINT NOT NULL,
    Action      VARCHAR(20) NOT NULL,
    OldValue    VARCHAR(MAX) NULL,
    NewValue    VARCHAR(MAX) NULL,
    UserId      BIGINT NOT NULL CONSTRAINT FK_auditlog_user REFERENCES dbo.[User](Id),
    Active      BIT NOT NULL CONSTRAINT DF_auditlog_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_auditlog_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL,
    CONSTRAINT CK_auditlog_action CHECK (Action IN ('INSERT','UPDATE','DELETE'))
);
GO

IF OBJECT_ID('dbo.JobQueue', 'U') IS NULL
CREATE TABLE dbo.JobQueue (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_jobqueue PRIMARY KEY,
    JobType       VARCHAR(50) NOT NULL,
    Payload       VARCHAR(MAX) NULL,
    Status        VARCHAR(20) NOT NULL CONSTRAINT DF_jobqueue_status DEFAULT('Pending'),
    Attempts      INT NOT NULL CONSTRAINT DF_jobqueue_attempts DEFAULT((0)),
    ProcessedDate DATETIME NULL,
    Active        BIT NOT NULL CONSTRAINT DF_jobqueue_active DEFAULT((1)),
    InsertDate    DATETIME NULL,
    InsertedBy    BIGINT NULL,
    UpdateDate    DATETIME NULL,
    UpdatedBy     BIGINT NULL,
    IsDeleted     BIT NOT NULL CONSTRAINT DF_jobqueue_isdeleted DEFAULT((0)),
    DeletedDate   DATETIME NULL,
    DeletedBy     BIGINT NULL,
    CONSTRAINT CK_jobqueue_status CHECK (Status IN ('Pending','Processing','Completed','Failed'))
);
GO

/*
    Read by PMT.Infrastructure.Security.IpWhitelistProvider. An empty table means
    "allow everything" -- see IsAllowedAsync. Only consulted when Security:EnableIpWhitelist
    is true.
*/
IF OBJECT_ID('dbo.IPWhitelist', 'U') IS NULL
CREATE TABLE dbo.IPWhitelist (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ipwhitelist PRIMARY KEY,
    CidrRange   VARCHAR(50) NOT NULL,
    Description VARCHAR(200) NULL,
    Active      BIT NOT NULL CONSTRAINT DF_ipwhitelist_active DEFAULT((1)),
    InsertDate  DATETIME NULL,
    InsertedBy  BIGINT NULL,
    UpdateDate  DATETIME NULL,
    UpdatedBy   BIGINT NULL,
    IsDeleted   BIT NOT NULL CONSTRAINT DF_ipwhitelist_isdeleted DEFAULT((0)),
    DeletedDate DATETIME NULL,
    DeletedBy   BIGINT NULL
);
GO

/* ===========================================================================
   Filtered unique indexes

   Filtered on IsDeleted = 0 so a soft-deleted row releases its name/email/pairing.
   =========================================================================== */

IF INDEXPROPERTY(OBJECT_ID('dbo.Department'), 'UQ_department_name', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_department_name ON dbo.Department(Name) WHERE IsDeleted = 0;
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Role'), 'UQ_role_name', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_role_name ON dbo.Role(Name) WHERE IsDeleted = 0;
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Permission'), 'UQ_permission_name', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_permission_name ON dbo.Permission(Name) WHERE IsDeleted = 0;
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.RolePermission'), 'UQ_rolepermission_roleid_permissionid', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_rolepermission_roleid_permissionid ON dbo.RolePermission(RoleId, PermissionId) WHERE IsDeleted = 0;
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.[User]'), 'UQ_user_email', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_user_email ON dbo.[User](Email) WHERE IsDeleted = 0;
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.IPWhitelist'), 'UQ_ipwhitelist_cidrrange', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_ipwhitelist_cidrrange ON dbo.IPWhitelist(CidrRange) WHERE IsDeleted = 0;
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.NotificationPreference'), 'UQ_notificationpreference_userid_eventtype', 'IndexId') IS NULL
    CREATE UNIQUE INDEX UQ_notificationpreference_userid_eventtype ON dbo.NotificationPreference(UserId, EventType) WHERE IsDeleted = 0;
GO

/* ===========================================================================
   Foreign-key supporting indexes
   =========================================================================== */

IF INDEXPROPERTY(OBJECT_ID('dbo.[User]'), 'IX_User_RoleId', 'IndexId') IS NULL
    CREATE INDEX IX_User_RoleId ON dbo.[User](RoleId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.[User]'), 'IX_User_DepartmentId', 'IndexId') IS NULL
    CREATE INDEX IX_User_DepartmentId ON dbo.[User](DepartmentId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.RefreshToken'), 'IX_RefreshToken_UserId', 'IndexId') IS NULL
    CREATE INDEX IX_RefreshToken_UserId ON dbo.RefreshToken(UserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Project'), 'IX_Project_DepartmentId', 'IndexId') IS NULL
    CREATE INDEX IX_Project_DepartmentId ON dbo.Project(DepartmentId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Project'), 'IX_Project_OwnerUserId', 'IndexId') IS NULL
    CREATE INDEX IX_Project_OwnerUserId ON dbo.Project(OwnerUserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.UserStory'), 'IX_UserStory_ProjectId', 'IndexId') IS NULL
    CREATE INDEX IX_UserStory_ProjectId ON dbo.UserStory(ProjectId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Task'), 'IX_Task_StoryId', 'IndexId') IS NULL
    CREATE INDEX IX_Task_StoryId ON dbo.Task(StoryId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Task'), 'IX_Task_AssigneeUserId', 'IndexId') IS NULL
    CREATE INDEX IX_Task_AssigneeUserId ON dbo.Task(AssigneeUserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Issue'), 'IX_Issue_ProjectId', 'IndexId') IS NULL
    CREATE INDEX IX_Issue_ProjectId ON dbo.Issue(ProjectId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Issue'), 'IX_Issue_TaskId', 'IndexId') IS NULL
    CREATE INDEX IX_Issue_TaskId ON dbo.Issue(TaskId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Issue'), 'IX_Issue_ReportedByUserId', 'IndexId') IS NULL
    CREATE INDEX IX_Issue_ReportedByUserId ON dbo.Issue(ReportedByUserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Comment'), 'IX_Comment_TaskId', 'IndexId') IS NULL
    CREATE INDEX IX_Comment_TaskId ON dbo.Comment(TaskId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Comment'), 'IX_Comment_IssueId', 'IndexId') IS NULL
    CREATE INDEX IX_Comment_IssueId ON dbo.Comment(IssueId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Comment'), 'IX_Comment_UserId', 'IndexId') IS NULL
    CREATE INDEX IX_Comment_UserId ON dbo.Comment(UserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Attachment'), 'IX_Attachment_TaskId', 'IndexId') IS NULL
    CREATE INDEX IX_Attachment_TaskId ON dbo.Attachment(TaskId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Attachment'), 'IX_Attachment_IssueId', 'IndexId') IS NULL
    CREATE INDEX IX_Attachment_IssueId ON dbo.Attachment(IssueId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.GitLink'), 'IX_GitLink_TaskId', 'IndexId') IS NULL
    CREATE INDEX IX_GitLink_TaskId ON dbo.GitLink(TaskId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.GitLink'), 'IX_GitLink_IssueId', 'IndexId') IS NULL
    CREATE INDEX IX_GitLink_IssueId ON dbo.GitLink(IssueId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Notification'), 'IX_Notification_UserId', 'IndexId') IS NULL
    CREATE INDEX IX_Notification_UserId ON dbo.Notification(UserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.Notification'), 'IX_Notification_UserId_IsRead', 'IndexId') IS NULL
    CREATE INDEX IX_Notification_UserId_IsRead ON dbo.Notification(UserId, IsRead);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.AuditLog'), 'IX_AuditLog_UserId', 'IndexId') IS NULL
    CREATE INDEX IX_AuditLog_UserId ON dbo.AuditLog(UserId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.AuditLog'), 'IX_AuditLog_Entity', 'IndexId') IS NULL
    CREATE INDEX IX_AuditLog_Entity ON dbo.AuditLog(EntityName, EntityId);
GO
IF INDEXPROPERTY(OBJECT_ID('dbo.JobQueue'), 'IX_JobQueue_Status', 'IndexId') IS NULL
    CREATE INDEX IX_JobQueue_Status ON dbo.JobQueue(Status);
GO
