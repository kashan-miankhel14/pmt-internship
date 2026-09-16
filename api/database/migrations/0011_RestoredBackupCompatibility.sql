/*
    0011_RestoredBackupCompatibility.sql

    Brings older UDL_PMT backups up to the current API contract. This migration
    is idempotent and must run after 0009_DeployCompleteSchema.sql and
    0010_CriticalFixes.sql.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Permission') AND name = 'Module')
    ALTER TABLE dbo.Permission ADD Module varchar(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RefreshToken') AND name = 'JwtId')
    ALTER TABLE dbo.RefreshToken ADD JwtId varchar(100) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RefreshToken') AND name = 'ReplacedByTokenHash')
    ALTER TABLE dbo.RefreshToken ADD ReplacedByTokenHash varchar(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RefreshToken') AND name = 'CreatedByIp')
    ALTER TABLE dbo.RefreshToken ADD CreatedByIp varchar(50) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.RefreshToken') AND name = 'RevokedByIp')
    ALTER TABLE dbo.RefreshToken ADD RevokedByIp varchar(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Attachment') AND name = 'ProjectId')
    ALTER TABLE dbo.Attachment ADD ProjectId bigint NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Attachment') AND name = 'ContentType')
    ALTER TABLE dbo.Attachment ADD ContentType varchar(255) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Department') AND name = 'Code')
    ALTER TABLE dbo.Department ADD Code varchar(50) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Department') AND name = 'Description')
    ALTER TABLE dbo.Department ADD Description varchar(MAX) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GitLink') AND name = 'ProjectId')
    ALTER TABLE dbo.GitLink ADD ProjectId bigint NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GitLink') AND name = 'RepositoryUrl')
    ALTER TABLE dbo.GitLink ADD RepositoryUrl varchar(500) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.GitLink') AND name = 'ReferenceType')
    ALTER TABLE dbo.GitLink ADD ReferenceType varchar(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Comment') AND name = 'UserStoryId')
    ALTER TABLE dbo.Comment ADD UserStoryId bigint NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Comment') AND name = 'ProjectId')
    ALTER TABLE dbo.Comment ADD ProjectId bigint NULL;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_comment_parentrequired' AND parent_object_id = OBJECT_ID('dbo.Comment'))
    ALTER TABLE dbo.Comment DROP CONSTRAINT CK_comment_parentrequired;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_comment_parentrequired' AND parent_object_id = OBJECT_ID('dbo.Comment'))
    ALTER TABLE dbo.Comment ADD CONSTRAINT CK_comment_parentrequired CHECK (ProjectId IS NOT NULL OR UserStoryId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL);
GO

-- Older backups predate the reference-data columns and contain the seeded IT
-- department without its canonical code/description.
UPDATE dbo.Department
SET Code = CASE WHEN NULLIF(LTRIM(RTRIM(Code)), '') IS NULL AND Name = 'IT' THEN 'IT' ELSE Code END,
    Description = CASE WHEN NULLIF(LTRIM(RTRIM(Description)), '') IS NULL AND Name = 'IT' THEN 'Information Technology' ELSE Description END
WHERE IsDeleted = 0 AND Name = 'IT';
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_attachment_parentrequired' AND parent_object_id = OBJECT_ID('dbo.Attachment'))
    ALTER TABLE dbo.Attachment DROP CONSTRAINT CK_attachment_parentrequired;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_attachment_parentrequired' AND parent_object_id = OBJECT_ID('dbo.Attachment'))
    ALTER TABLE dbo.Attachment ADD CONSTRAINT CK_attachment_parentrequired CHECK (ProjectId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL);

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_gitlink_parentrequired' AND parent_object_id = OBJECT_ID('dbo.GitLink'))
    ALTER TABLE dbo.GitLink DROP CONSTRAINT CK_gitlink_parentrequired;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_gitlink_parentrequired' AND parent_object_id = OBJECT_ID('dbo.GitLink'))
    ALTER TABLE dbo.GitLink ADD CONSTRAINT CK_gitlink_parentrequired CHECK (ProjectId IS NOT NULL OR TaskId IS NOT NULL OR IssueId IS NOT NULL);
GO

-- Project parents for the columns added above. 0009 already creates these on a
-- database that ran the full chain; repeat them here (guarded, and only when no
-- orphan row would fail validation) for databases restored from an old backup.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_attachment_project')
   AND NOT EXISTS (SELECT 1 FROM dbo.Attachment A WHERE A.ProjectId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.Project P WHERE P.Id = A.ProjectId))
    ALTER TABLE dbo.Attachment ADD CONSTRAINT FK_attachment_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_gitlink_project')
   AND NOT EXISTS (SELECT 1 FROM dbo.GitLink G WHERE G.ProjectId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM dbo.Project P WHERE P.Id = G.ProjectId))
    ALTER TABLE dbo.GitLink ADD CONSTRAINT FK_gitlink_project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(Id);
GO

CREATE OR ALTER PROCEDURE dbo.SP_ATTACHMENT
  @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@FileName varchar(255)=NULL,@FilePath varchar(500)=NULL,@ContentType varchar(255)=NULL,@FileSizeKb int=NULL,@UploadedByUserId bigint=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.Attachment WHERE IsDeleted=0 AND ((@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate DESC;
  ELSE IF @Action='FETCH_BY_ID' SELECT TOP (1) * FROM dbo.Attachment WHERE Id=@Id AND IsDeleted=0;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Attachment(ProjectId,TaskId,IssueId,FileName,FilePath,ContentType,FileSizeKb,UploadedByUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@IssueId,@FileName,@FilePath,@ContentType,@FileSizeKb,@UploadedByUserId,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Attachment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_GITLINK
  @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@Provider varchar(30)=NULL,@CommitSha varchar(100)=NULL,@PullRequestUrl varchar(500)=NULL,@RepositoryUrl varchar(500)=NULL,@ReferenceType varchar(50)=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='FETCH' SELECT * FROM dbo.GitLink WHERE IsDeleted=0 AND ((@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY Id DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.GitLink(ProjectId,TaskId,IssueId,Provider,CommitSha,PullRequestUrl,RepositoryUrl,ReferenceType,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@IssueId,@Provider,@CommitSha,@PullRequestUrl,@RepositoryUrl,@ReferenceType,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='DELETE' BEGIN UPDATE dbo.GitLink SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_COMMENT
 @Id bigint=NULL,@ProjectId bigint=NULL,@UserStoryId bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@UserId bigint=NULL,@Content varchar(max)=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH_BY_ID' SELECT TOP (1) * FROM dbo.Comment WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='FETCH' SELECT * FROM dbo.Comment WHERE IsDeleted=0 AND (@EntityType IS NULL OR (@EntityType='Project' AND ProjectId=@EntityId) OR (@EntityType='UserStory' AND UserStoryId=@EntityId) OR (@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Comment(ProjectId,UserStoryId,TaskId,IssueId,UserId,Content,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@UserStoryId,@TaskId,@IssueId,@UserId,@Content,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
  ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Comment SET Content=@Content,UpdateDate=GETDATE(),UpdatedBy=@User WHERE Id=@Id AND IsDeleted=0 AND UserId=@UserId; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Comment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

IF OBJECT_ID(N'dbo.SchemaMigration') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Version='0011' AND Name='RestoredBackupCompatibility.sql' AND IsDeleted=0)
    INSERT dbo.SchemaMigration(Version, Name, AppliedAt, Success) VALUES('0011','RestoredBackupCompatibility.sql',GETDATE(),1);
GO
