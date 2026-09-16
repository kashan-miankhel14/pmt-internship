USE [UDL_PMT]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.SP_DEPARTMENT
 @Id bigint=NULL,@Name varchar(150)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(150)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Department WHERE Id=ISNULL(@Id,Id) AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Department WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%'); SELECT * FROM dbo.Department WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Department(Name,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@Name,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Department SET Name=@Name,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Department SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_USER
 @Id bigint=NULL,@Value varchar(200)=NULL,@FullName varchar(150)=NULL,@Email varchar(200)=NULL,@PasswordHash varchar(500)=NULL,@RoleId bigint=NULL,@DepartmentId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(200)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.[User] WHERE IsDeleted=0 AND ((@Id IS NOT NULL AND Id=@Id) OR (@Id IS NULL AND @Value IS NOT NULL AND (Email=@Value OR FullName=@Value)));
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.[User] WHERE IsDeleted=0 AND (@Search IS NULL OR FullName LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%'); SELECT * FROM dbo.[User] WHERE IsDeleted=0 AND (@Search IS NULL OR FullName LIKE '%'+@Search+'%' OR Email LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
 ELSE IF @Action='INSERT' BEGIN INSERT dbo.[User](FullName,Email,PasswordHash,RoleId,DepartmentId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@FullName,@Email,@PasswordHash,@RoleId,@DepartmentId,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.[User] SET FullName=@FullName,Email=@Email,PasswordHash=@PasswordHash,RoleId=@RoleId,DepartmentId=@DepartmentId,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.[User] SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='ROLES' BEGIN IF @Id IS NULL SELECT * FROM dbo.Role WHERE Active=1 AND IsDeleted=0 ORDER BY Name; ELSE SELECT R.* FROM dbo.[User] U JOIN dbo.Role R ON R.Id=U.RoleId WHERE U.Id=@Id AND U.IsDeleted=0 AND R.IsDeleted=0; END
 ELSE IF @Action='PERMISSIONS' SELECT DISTINCT P.Name FROM dbo.[User] U JOIN dbo.RolePermission RP ON RP.RoleId=U.RoleId JOIN dbo.Permission P ON P.Id=RP.PermissionId WHERE U.Id=@Id AND U.IsDeleted=0 AND RP.IsDeleted=0 AND RP.Active=1 AND P.IsDeleted=0 AND P.Active=1;
 ELSE IF @Action='SETROLE' BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.Role WHERE Id=@RoleId AND Active=1 AND IsDeleted=0) BEGIN SELECT CONVERT(bigint,0); RETURN; END UPDATE dbo.[User] SET RoleId=@RoleId,UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_COMMENT
 @Id bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@UserId bigint=NULL,@Content varchar(max)=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Comment WHERE IsDeleted=0 AND ((@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Comment(TaskId,IssueId,UserId,Content,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@TaskId,@IssueId,@UserId,@Content,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Comment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_ATTACHMENT
 @Id bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@FileName varchar(255)=NULL,@FilePath varchar(500)=NULL,@FileSizeKb int=NULL,@UploadedByUserId bigint=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Attachment WHERE IsDeleted=0 AND ((@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY InsertDate DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Attachment(TaskId,IssueId,FileName,FilePath,FileSizeKb,UploadedByUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@TaskId,@IssueId,@FileName,@FilePath,@FileSizeKb,@UploadedByUserId,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Attachment SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_GITLINK
 @Id bigint=NULL,@TaskId bigint=NULL,@IssueId bigint=NULL,@EntityType varchar(20)=NULL,@EntityId bigint=NULL,@Provider varchar(30)=NULL,@CommitSha varchar(100)=NULL,@PullRequestUrl varchar(500)=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.GitLink WHERE IsDeleted=0 AND ((@EntityType='Task' AND TaskId=@EntityId) OR (@EntityType='Issue' AND IssueId=@EntityId)) ORDER BY Id DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.GitLink(TaskId,IssueId,Provider,CommitSha,PullRequestUrl,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@TaskId,@IssueId,@Provider,@CommitSha,@PullRequestUrl,1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.GitLink SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@User WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_NOTIFICATION
 @Id bigint=NULL,@UserId bigint=NULL,@EventType varchar(50)=NULL,@Message varchar(500)=NULL,@IsRead bit=NULL,@UnreadOnly bit=0,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT TOP(200)* FROM dbo.Notification WHERE UserId=@UserId AND IsDeleted=0 AND (@UnreadOnly=0 OR IsRead=0) ORDER BY InsertDate DESC;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Notification(UserId,EventType,Message,IsRead,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@UserId,@EventType,@Message,ISNULL(@IsRead,0),1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='MARKREAD' BEGIN UPDATE dbo.Notification SET IsRead=1,UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND UserId=@UserId AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_REFRESH_TOKEN @Id bigint=NULL,@UserId bigint=NULL,@TokenHash varchar(500)=NULL,@ExpiryDate datetime=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.RefreshToken WHERE TokenHash=@TokenHash AND IsDeleted=0;
  ELSE IF @Action='INSERT' INSERT dbo.RefreshToken(UserId,TokenHash,ExpiryDate,IsRevoked,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@UserId,@TokenHash,@ExpiryDate,0,1,0,GETDATE(),@UserId);
 ELSE IF @Action='UPDATE' UPDATE dbo.RefreshToken SET IsRevoked=1,Active=0,UpdateDate=GETDATE() WHERE Id=@Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_JOB_QUEUE @Id bigint=NULL,@JobType varchar(50)=NULL,@Payload varchar(max)=NULL,@Status varchar(20)=NULL,@Attempts int=NULL,@User bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
  IF @Action='INSERT' BEGIN INSERT dbo.JobQueue(JobType,Payload,Status,Attempts,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@JobType,@Payload,ISNULL(@Status,'Pending'),ISNULL(@Attempts,0),1,0,GETDATE(),@User); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='CLAIM' BEGIN ;WITH N AS(SELECT TOP(1)* FROM dbo.JobQueue WITH(UPDLOCK,READPAST,ROWLOCK) WHERE IsDeleted=0 AND Status IN('Pending','Failed') ORDER BY Id) UPDATE N SET Status='Processing',Attempts=Attempts+1,UpdateDate=GETDATE() OUTPUT INSERTED.*; END
 ELSE IF @Action='COMPLETE' UPDATE dbo.JobQueue SET Status='Completed',ProcessedDate=GETDATE(),UpdateDate=GETDATE() WHERE Id=@Id;
 ELSE IF @Action='FAIL' UPDATE dbo.JobQueue SET Status='Failed',UpdateDate=GETDATE() WHERE Id=@Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_AUDIT_LOG @EntityName varchar(100),@EntityId bigint,@EventAction varchar(20),@OldValue varchar(max)=NULL,@NewValue varchar(max)=NULL,@UserId bigint,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;   IF @Action='INSERT' INSERT dbo.AuditLog(EntityName,EntityId,Action,OldValue,NewValue,UserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@EntityName,@EntityId,@EventAction,@OldValue,@NewValue,@UserId,1,0,GETDATE(),@UserId); END
GO

CREATE OR ALTER PROCEDURE dbo.SP_IP_WHITELIST @Id bigint=NULL,@CidrRange varchar(50)=NULL,@Description varchar(200)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT CidrRange FROM dbo.IPWhitelist WHERE Active=1 AND IsDeleted=0;
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.IPWhitelist(CidrRange,Description,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@CidrRange,@Description,ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.IPWhitelist SET CidrRange=@CidrRange,Description=@Description,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.IPWhitelist SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_REPORT @ProjectId bigint=NULL,@From datetime=NULL,@To datetime=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='VELOCITY' SELECT CONVERT(char(7),T.UpdateDate,120) Period,COUNT(DISTINCT S.Id) CompletedStories,CAST(0 AS decimal(18,2))CompletedStoryPoints,COUNT(T.Id)CompletedTasks FROM dbo.Task T JOIN dbo.UserStory S ON S.Id=T.StoryId AND S.IsDeleted=0 WHERE T.IsDeleted=0 AND T.Status='Done' AND T.UpdateDate>=@From AND T.UpdateDate<@To AND(@ProjectId IS NULL OR S.ProjectId=@ProjectId)GROUP BY CONVERT(char(7),T.UpdateDate,120)ORDER BY Period;
 ELSE IF @Action='WORKLOAD' SELECT U.Id UserId,U.FullName UserName,COALESCE(T.OpenTasks,0)OpenTasks,CAST(0 AS int)OpenIssues,COALESCE(T.EstimatedHours,0)EstimatedHours FROM dbo.[User]U OUTER APPLY(SELECT COUNT(1)OpenTasks,COALESCE(SUM(T.EstimateHours),0)EstimatedHours FROM dbo.Task T JOIN dbo.UserStory S ON S.Id=T.StoryId WHERE T.AssigneeUserId=U.Id AND T.IsDeleted=0 AND T.Status NOT IN('Done','Cancelled')AND(@ProjectId IS NULL OR S.ProjectId=@ProjectId))T WHERE U.IsDeleted=0 AND U.Active=1 ORDER BY U.FullName;
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_ADMIN @FullName varchar(150),@Email varchar(200),@PasswordHash varchar(500),@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action<>'BOOTSTRAP' THROW 50000,'INVALID ACTION',1;
 IF EXISTS(SELECT 1 FROM dbo.[User]WHERE IsDeleted=0)THROW 50000,'Bootstrap disabled because a user exists.',1;
 DECLARE @RoleId bigint=(SELECT TOP(1)Id FROM dbo.Role WHERE Name='Administrator'AND IsDeleted=0),@DepartmentId bigint=(SELECT TOP(1)Id FROM dbo.Department WHERE Active=1 AND IsDeleted=0 ORDER BY Id);
 IF @RoleId IS NULL OR @DepartmentId IS NULL THROW 50000,'Administrator role and active department are required.',1;
 INSERT dbo.[User](FullName,Email,PasswordHash,RoleId,DepartmentId,Active,IsDeleted,InsertDate)VALUES(@FullName,@Email,@PasswordHash,@RoleId,@DepartmentId,1,0,GETDATE());SELECT CONVERT(bigint,SCOPE_IDENTITY());
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_ROLE @Id bigint=NULL,@Name varchar(100)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON; IF @Action='FETCH'SELECT * FROM dbo.Role WHERE Id=ISNULL(@Id,Id)AND IsDeleted=0; ELSE IF @Action='INSERT'BEGIN INSERT dbo.Role(Name,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@Name,ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END ELSE IF @Action='UPDATE'BEGIN UPDATE dbo.Role SET Name=@Name,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END ELSE IF @Action='DELETE'BEGIN UPDATE dbo.Role SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id;SELECT CONVERT(bigint,@@ROWCOUNT);END END
GO
CREATE OR ALTER PROCEDURE dbo.SP_PERMISSION @Id bigint=NULL,@Name varchar(100)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON; IF @Action='FETCH'SELECT * FROM dbo.Permission WHERE Id=ISNULL(@Id,Id)AND IsDeleted=0; ELSE IF @Action='INSERT'BEGIN INSERT dbo.Permission(Name,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@Name,ISNULL(@Active,1),0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END ELSE IF @Action='UPDATE'BEGIN UPDATE dbo.Permission SET Name=@Name,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0;SELECT CONVERT(bigint,@@ROWCOUNT);END ELSE IF @Action='DELETE'BEGIN UPDATE dbo.Permission SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id;SELECT CONVERT(bigint,@@ROWCOUNT);END END
GO
CREATE OR ALTER PROCEDURE dbo.SP_ROLE_PERMISSION @Id bigint=NULL,@RoleId bigint=NULL,@PermissionId bigint=NULL,@UserId bigint=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;IF @Action='FETCH'SELECT * FROM dbo.RolePermission WHERE(@RoleId IS NULL OR RoleId=@RoleId)AND IsDeleted=0;ELSE IF @Action='INSERT'BEGIN INSERT dbo.RolePermission(RoleId,PermissionId,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@RoleId,@PermissionId,1,0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END ELSE IF @Action='DELETE'BEGIN UPDATE dbo.RolePermission SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id;SELECT CONVERT(bigint,@@ROWCOUNT);END END
GO
CREATE OR ALTER PROCEDURE dbo.SP_NOTIFICATION_PREFERENCE @Id bigint=NULL,@UserId bigint=NULL,@EventType varchar(50)=NULL,@InAppEnabled bit=NULL,@EmailEnabled bit=NULL,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;IF @Action='FETCH'SELECT * FROM dbo.NotificationPreference WHERE UserId=ISNULL(@UserId,UserId)AND IsDeleted=0;ELSE IF @Action='INSERT'BEGIN INSERT dbo.NotificationPreference(UserId,EventType,InAppEnabled,EmailEnabled,Active,IsDeleted,InsertDate,InsertedBy)VALUES(@UserId,@EventType,ISNULL(@InAppEnabled,1),ISNULL(@EmailEnabled,1),1,0,GETDATE(),@UserId);SELECT CONVERT(bigint,SCOPE_IDENTITY());END ELSE IF @Action='UPDATE'BEGIN UPDATE dbo.NotificationPreference SET EventType=@EventType,InAppEnabled=ISNULL(@InAppEnabled,InAppEnabled),EmailEnabled=ISNULL(@EmailEnabled,EmailEnabled),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id;SELECT CONVERT(bigint,@@ROWCOUNT);END END
GO

CREATE OR ALTER PROCEDURE dbo.SP_PROJECT
 @Id bigint=NULL,@Name varchar(200)=NULL,@Description varchar(max)=NULL,@Status varchar(30)=NULL,@DepartmentId bigint=NULL,@OwnerUserId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(200)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Project WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Project WHERE IsDeleted=0 AND (@Search IS NULL OR Name LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Project(Name,Description,Status,DepartmentId,OwnerUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@Name,@Description,@Status,@DepartmentId,@OwnerUserId,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Project SET Name=@Name,Description=@Description,Status=@Status,DepartmentId=@DepartmentId,OwnerUserId=@OwnerUserId,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Project SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_USER_STORY
 @Id bigint=NULL,@ProjectId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@AcceptanceCriteria varchar(max)=NULL,@Status varchar(30)=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.UserStory WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.UserStory WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.UserStory(ProjectId,Title,Description,AcceptanceCriteria,Status,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@Title,@Description,@AcceptanceCriteria,@Status,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.UserStory SET ProjectId=@ProjectId,Title=@Title,Description=@Description,AcceptanceCriteria=@AcceptanceCriteria,Status=@Status,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.UserStory SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_TASK
 @Id bigint=NULL,@StoryId bigint=NULL,@AssigneeUserId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@EstimateHours decimal(18,2)=NULL,@ActualHours decimal(18,2)=NULL,@Status varchar(30)=NULL,@DueDate date=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Task WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Task WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Task WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Task(StoryId,AssigneeUserId,Title,Description,EstimateHours,ActualHours,Status,DueDate,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@StoryId,@AssigneeUserId,@Title,@Description,@EstimateHours,@ActualHours,@Status,@DueDate,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Task SET StoryId=@StoryId,AssigneeUserId=@AssigneeUserId,Title=@Title,Description=@Description,EstimateHours=@EstimateHours,ActualHours=@ActualHours,Status=@Status,DueDate=@DueDate,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Task SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO

CREATE OR ALTER PROCEDURE dbo.SP_ISSUE
 @Id bigint=NULL,@ProjectId bigint=NULL,@TaskId bigint=NULL,@Title varchar(250)=NULL,@Description varchar(max)=NULL,@Severity varchar(20)=NULL,@Status varchar(30)=NULL,@ReportedByUserId bigint=NULL,@Active bit=NULL,@UserId bigint=NULL,@Search varchar(250)=NULL,@Page int=1,@PageSize int=50,@Action varchar(20)
AS BEGIN SET NOCOUNT ON;
 IF @Action='FETCH' SELECT * FROM dbo.Issue WHERE Id=@Id AND IsDeleted=0;
 ELSE IF @Action='PAGED' BEGIN SELECT COUNT_BIG(1) FROM dbo.Issue WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%'); SELECT * FROM dbo.Issue WHERE IsDeleted=0 AND (@Search IS NULL OR Title LIKE '%'+@Search+'%' OR Description LIKE '%'+@Search+'%') ORDER BY Id DESC OFFSET (@Page-1)*@PageSize ROWS FETCH NEXT @PageSize ROWS ONLY; END
  ELSE IF @Action='INSERT' BEGIN INSERT dbo.Issue(ProjectId,TaskId,Title,Description,Severity,Status,ReportedByUserId,Active,IsDeleted,InsertDate,InsertedBy) VALUES(@ProjectId,@TaskId,@Title,@Description,@Severity,@Status,@ReportedByUserId,ISNULL(@Active,1),0,GETDATE(),@UserId); SELECT CONVERT(bigint,SCOPE_IDENTITY()); END
 ELSE IF @Action='UPDATE' BEGIN UPDATE dbo.Issue SET ProjectId=@ProjectId,TaskId=@TaskId,Title=@Title,Description=@Description,Severity=@Severity,Status=@Status,ReportedByUserId=@ReportedByUserId,Active=ISNULL(@Active,Active),UpdateDate=GETDATE(),UpdatedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
 ELSE IF @Action='DELETE' BEGIN UPDATE dbo.Issue SET IsDeleted=1,Active=0,DeletedDate=GETDATE(),DeletedBy=@UserId WHERE Id=@Id AND IsDeleted=0; SELECT CONVERT(bigint,@@ROWCOUNT); END
END
GO
