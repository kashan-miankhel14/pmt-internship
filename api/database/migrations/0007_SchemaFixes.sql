/*
    0007_SchemaFixes.sql

    Resolves schema drift by adding missing columns to match Domain entities and fixing enums/checks.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF DB_NAME() IS NULL THROW 50000, 'Select the target PMT database before running this migration.', 1;

-- 1. Department
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Code' AND Object_ID = OBJECT_ID('dbo.Department'))
    ALTER TABLE dbo.Department ADD Code VARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Description' AND Object_ID = OBJECT_ID('dbo.Department'))
    ALTER TABLE dbo.Department ADD Description VARCHAR(MAX) NULL;

-- 2. User
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'IsLocked' AND Object_ID = OBJECT_ID('dbo.[User]'))
    ALTER TABLE dbo.[User] ADD IsLocked BIT NOT NULL CONSTRAINT DF_user_islocked DEFAULT((0));

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'FailedLoginAttempts' AND Object_ID = OBJECT_ID('dbo.[User]'))
    ALTER TABLE dbo.[User] ADD FailedLoginAttempts INT NOT NULL CONSTRAINT DF_user_failedlogins DEFAULT((0));

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'LockoutEnd' AND Object_ID = OBJECT_ID('dbo.[User]'))
    ALTER TABLE dbo.[User] ADD LockoutEnd DATETIME NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'LastLoginDate' AND Object_ID = OBJECT_ID('dbo.[User]'))
    ALTER TABLE dbo.[User] ADD LastLoginDate DATETIME NULL;

-- 3. Role
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Description' AND Object_ID = OBJECT_ID('dbo.Role'))
    ALTER TABLE dbo.Role ADD Description VARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'IsSystem' AND Object_ID = OBJECT_ID('dbo.Role'))
    ALTER TABLE dbo.Role ADD IsSystem BIT NOT NULL CONSTRAINT DF_role_issystem DEFAULT((0));

-- 4. Project
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Key' AND Object_ID = OBJECT_ID('dbo.Project'))
    ALTER TABLE dbo.Project ADD [Key] VARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'StartDate' AND Object_ID = OBJECT_ID('dbo.Project'))
    ALTER TABLE dbo.Project ADD StartDate DATE NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'TargetDate' AND Object_ID = OBJECT_ID('dbo.Project'))
    ALTER TABLE dbo.Project ADD TargetDate DATE NULL;

-- Fix Project Status Check
ALTER TABLE dbo.Project DROP CONSTRAINT CK_project_status;
ALTER TABLE dbo.Project ADD CONSTRAINT CK_project_status CHECK (Status IN ('Planning','Active','OnHold','Completed','Cancelled','Archived'));

-- 5. UserStory
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Priority' AND Object_ID = OBJECT_ID('dbo.UserStory'))
    ALTER TABLE dbo.UserStory ADD Priority VARCHAR(20) NOT NULL CONSTRAINT DF_userstory_priority DEFAULT('Medium');

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'StoryPoints' AND Object_ID = OBJECT_ID('dbo.UserStory'))
    ALTER TABLE dbo.UserStory ADD StoryPoints INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'AssigneeUserId' AND Object_ID = OBJECT_ID('dbo.UserStory'))
    ALTER TABLE dbo.UserStory ADD AssigneeUserId BIGINT NULL CONSTRAINT FK_userstory_assignee REFERENCES dbo.[User](Id);

-- Fix UserStory Status Check
ALTER TABLE dbo.UserStory DROP CONSTRAINT CK_userstory_status;
ALTER TABLE dbo.UserStory ADD CONSTRAINT CK_userstory_status CHECK (Status IN ('Backlog','Ready','InProgress','Review','Done','Cancelled'));

-- 6. Task
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ProjectId' AND Object_ID = OBJECT_ID('dbo.Task'))
    ALTER TABLE dbo.Task ADD ProjectId BIGINT NULL CONSTRAINT FK_task_project REFERENCES dbo.Project(Id);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Priority' AND Object_ID = OBJECT_ID('dbo.Task'))
    ALTER TABLE dbo.Task ADD Priority VARCHAR(20) NOT NULL CONSTRAINT DF_task_priority DEFAULT('Medium');

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'CompletionDate' AND Object_ID = OBJECT_ID('dbo.Task'))
    ALTER TABLE dbo.Task ADD CompletionDate DATETIME NULL;

-- Fix Task Status Check
ALTER TABLE dbo.Task DROP CONSTRAINT CK_task_status;
ALTER TABLE dbo.Task ADD CONSTRAINT CK_task_status CHECK (Status IN ('ToDo','InProgress','Review','Done','Cancelled','Blocked'));

-- 7. Issue
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'AssigneeUserId' AND Object_ID = OBJECT_ID('dbo.Issue'))
    ALTER TABLE dbo.Issue ADD AssigneeUserId BIGINT NULL CONSTRAINT FK_issue_assignee REFERENCES dbo.[User](Id);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ResolvedDate' AND Object_ID = OBJECT_ID('dbo.Issue'))
    ALTER TABLE dbo.Issue ADD ResolvedDate DATETIME NULL;

-- Fix Issue Status Check
ALTER TABLE dbo.Issue DROP CONSTRAINT CK_issue_status;
ALTER TABLE dbo.Issue ADD CONSTRAINT CK_issue_status CHECK (Status IN ('Open','InProgress','Resolved','Closed','Reopened','Rejected'));

-- 8. Notification
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Title' AND Object_ID = OBJECT_ID('dbo.Notification'))
    ALTER TABLE dbo.Notification ADD Title VARCHAR(250) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Link' AND Object_ID = OBJECT_ID('dbo.Notification'))
    ALTER TABLE dbo.Notification ADD Link VARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ReadDate' AND Object_ID = OBJECT_ID('dbo.Notification'))
    ALTER TABLE dbo.Notification ADD ReadDate DATETIME NULL;

-- 9. RefreshToken
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ReplacedByTokenHash' AND Object_ID = OBJECT_ID('dbo.RefreshToken'))
    ALTER TABLE dbo.RefreshToken ADD ReplacedByTokenHash VARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'CreatedByIp' AND Object_ID = OBJECT_ID('dbo.RefreshToken'))
    ALTER TABLE dbo.RefreshToken ADD CreatedByIp VARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'RevokedByIp' AND Object_ID = OBJECT_ID('dbo.RefreshToken'))
    ALTER TABLE dbo.RefreshToken ADD RevokedByIp VARCHAR(50) NULL;

-- 10. JobQueue
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'MaxAttempts' AND Object_ID = OBJECT_ID('dbo.JobQueue'))
    ALTER TABLE dbo.JobQueue ADD MaxAttempts INT NOT NULL CONSTRAINT DF_jobqueue_maxattempts DEFAULT(3);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'AvailableDate' AND Object_ID = OBJECT_ID('dbo.JobQueue'))
    ALTER TABLE dbo.JobQueue ADD AvailableDate DATETIME NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'StartDate' AND Object_ID = OBJECT_ID('dbo.JobQueue'))
    ALTER TABLE dbo.JobQueue ADD StartDate DATETIME NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'CompletionDate' AND Object_ID = OBJECT_ID('dbo.JobQueue'))
    ALTER TABLE dbo.JobQueue ADD CompletionDate DATETIME NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'LastError' AND Object_ID = OBJECT_ID('dbo.JobQueue'))
    ALTER TABLE dbo.JobQueue ADD LastError VARCHAR(MAX) NULL;

-- 11. GitLink Fix
ALTER TABLE dbo.GitLink DROP CONSTRAINT CK_gitlink_provider;
ALTER TABLE dbo.GitLink ADD CONSTRAINT CK_gitlink_provider CHECK (Provider IN ('GitHub','GitLab','Bitbucket','AzureDevOps','Other'));

COMMIT;
GO
