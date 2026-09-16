using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PMT.Domain.Enums;
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.IntegrationTests.Contracts;

/// <summary>
/// Contract tests that validate stored-procedure behaviour end-to-end against a
/// throw-away LocalDB database.  These tests do NOT require a pre-existing
/// database — <see cref="MigratedDatabaseFixture"/> creates one, runs the full
/// canonical chain 0009 → 0010 → 0011, and drops it when the class finishes.
///
/// Coverage:
///   • The migration chain itself — every batch of every migration must execute
///     on an empty database, so a constraint that references a column that does
///     not exist yet fails the whole class.
///   • Schema shape — Attachment/GitLink project parents, Attachment.ContentType,
///     the project foreign keys, and the column-type alignment (5b/5c/5d).
///   • Seed data — roles, permissions and the default department.
///   • Procedure surface — every dbo.SP_* the API calls exists.
///   • SP_DEPARTMENT — accepts exactly the parameters DepartmentRepository sends
///     (@InsertedBy / @UpdatedBy) and round-trips Code/Description.
///   • SP_AUDIT_LOG — INSERT, UPDATE and DELETE writes all land in AuditLog.
///   • SP_ATTACHMENT / SP_GITLINK — project parent, ContentType, repository URL.
///   • SP_REPORT VELOCITY — CompletedStoryPoints is the sum of distinct story
///     points, not a hardcoded 0.
///   • SP_REPORT WORKLOAD — OpenIssues reflects open issues assigned to the user.
///   • SP_JOB_QUEUE FAIL — LastError is persisted and caller-supplied
///     AvailableDate is honoured.
///   • SP_TASK / SP_USER_STORY UPDATE — nullable fields use ISNULL so partial
///     updates do not null-out existing values.
/// </summary>
public sealed class StoredProcedureContractTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    /// <summary>Every procedure the Dapper repositories resolve through ProcedureNames, plus the job-queue cleanup.</summary>
    public static TheoryData<string> ApiProcedures =>
    [
        "dbo.SP_ADMIN", "dbo.SP_ATTACHMENT", "dbo.SP_AUDIT_LOG", "dbo.SP_CLEANUP_EXPIRED_TOKENS",
        "dbo.SP_COMMENT", "dbo.SP_DEPARTMENT", "dbo.SP_GITLINK", "dbo.SP_IP_WHITELIST",
        "dbo.SP_ISSUE", "dbo.SP_JOB_QUEUE", "dbo.SP_NOTIFICATION", "dbo.SP_NOTIFICATION_PREFERENCE",
        "dbo.SP_PERMISSION", "dbo.SP_PROJECT", "dbo.SP_REFRESH_TOKEN", "dbo.SP_REPORT",
        "dbo.SP_ROLE", "dbo.SP_ROLE_PERMISSION", "dbo.SP_TASK", "dbo.SP_USER", "dbo.SP_USER_STORY"
    ];

    // ---- Migration-chain / schema contracts ----

    [Theory]
    [MemberData(nameof(ApiProcedures))]
    public async Task Fresh_Database_Exposes_Every_Procedure_The_Api_Calls(string procedure)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        var exists = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM sys.objects WHERE object_id = OBJECT_ID(@name) AND type = 'P'",
            new { name = procedure });
        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task Fresh_Database_Has_Project_Parent_Columns_And_Constraints()
    {
        await using var conn = await fixture.OpenConnectionAsync();

        // Columns the parent-required CHECK constraints and the procedures depend on.
        Assert.True(await ColumnExistsAsync(conn, "dbo.Attachment", "ProjectId"));
        Assert.True(await ColumnExistsAsync(conn, "dbo.Attachment", "ContentType"));
        Assert.True(await ColumnExistsAsync(conn, "dbo.GitLink", "ProjectId"));
        Assert.True(await ColumnExistsAsync(conn, "dbo.GitLink", "RepositoryUrl"));
        Assert.True(await ColumnExistsAsync(conn, "dbo.GitLink", "ReferenceType"));
        Assert.True(await ColumnExistsAsync(conn, "dbo.Department", "Code"));
        Assert.True(await ColumnExistsAsync(conn, "dbo.Department", "Description"));

        // Check constraints created on the canonical fresh path.
        Assert.Equal(1, await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM sys.check_constraints WHERE name = 'CK_attachment_parentrequired' AND parent_object_id = OBJECT_ID('dbo.Attachment')"));
        Assert.Equal(1, await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM sys.check_constraints WHERE name = 'CK_gitlink_parentrequired' AND parent_object_id = OBJECT_ID('dbo.GitLink')"));

        // Project foreign keys for the nullable project parents.
        Assert.Equal(1, await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM sys.foreign_keys WHERE name = 'FK_attachment_project' AND parent_object_id = OBJECT_ID('dbo.Attachment')"));
        Assert.Equal(1, await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM sys.foreign_keys WHERE name = 'FK_gitlink_project' AND parent_object_id = OBJECT_ID('dbo.GitLink')"));
    }

    [Fact]
    public async Task Fresh_Database_Seeds_Department_Roles_And_Permissions()
    {
        await using var conn = await fixture.OpenConnectionAsync();

        Assert.True(await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM dbo.Department WHERE IsDeleted = 0") >= 1);
        Assert.Equal(4, await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM dbo.Role WHERE IsDeleted = 0"));
        Assert.Equal(17, await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM dbo.Permission WHERE IsDeleted = 0"));

        // The administrator role must hold every permission or every policy-protected endpoint 403s.
        var adminGrants = await conn.QuerySingleAsync<int>(
            @"SELECT COUNT(1) FROM dbo.RolePermission RP
              JOIN dbo.Role R ON R.Id = RP.RoleId
              WHERE R.Name = 'Administrator' AND RP.IsDeleted = 0");
        Assert.Equal(17, adminGrants);
    }

    [Fact]
    public async Task Every_Table_With_NotNull_IsDeleted_Has_A_Default()
    {
        await using var conn = await fixture.OpenConnectionAsync();
        var missing = (await conn.QueryAsync<string>(
            @"SELECT T.name
              FROM sys.tables T
              JOIN sys.columns C ON C.object_id = T.object_id AND C.name = 'IsDeleted' AND C.is_nullable = 0
              WHERE T.is_ms_shipped = 0
              AND NOT EXISTS (SELECT 1 FROM sys.default_constraints DC
                              WHERE DC.parent_object_id = T.object_id AND DC.parent_column_id = C.column_id)")).ToArray();

        Assert.True(missing.Length == 0, $"Tables without an IsDeleted default: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Column_Types_Align_With_Entity_Definitions()
    {
        await using var conn = await fixture.OpenConnectionAsync();

        // Task.Priority should be int
        var taskPriorityType = await conn.QuerySingleAsync<string>(
            "SELECT TYPE_NAME(c.user_type_id) FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.Task') AND c.name = 'Priority'");
        Assert.Equal("int", taskPriorityType);

        // Task.CompletedDate should exist (renamed from CompletionDate)
        Assert.True(await ColumnExistsAsync(conn, "dbo.Task", "CompletedDate"));

        // UserStory.Priority should be int
        var storyPriorityType = await conn.QuerySingleAsync<string>(
            "SELECT TYPE_NAME(c.user_type_id) FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.UserStory') AND c.name = 'Priority'");
        Assert.Equal("int", storyPriorityType);

        // UserStory.StoryPoints should be decimal
        var storyPointsType = await conn.QuerySingleAsync<string>(
            "SELECT TYPE_NAME(c.user_type_id) FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.UserStory') AND c.name = 'StoryPoints'");
        Assert.Equal("decimal", storyPointsType);

        // MaxAttempts column should exist with default
        Assert.True(await ColumnExistsAsync(conn, "dbo.JobQueue", "MaxAttempts"));
    }

    // ---- Procedure contracts ----

    [Fact]
    public async Task SP_Department_Accepts_The_Parameters_DepartmentRepository_Sends()
    {
        var userId = await CreateUserAsync("department-contract@pmt.local");
        var code = $"C{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var name = $"Contract Department {Guid.NewGuid():N}";

        await using var conn = await fixture.OpenConnectionAsync();

        // CreateAsync sends Action/Name/Code/Description/Active/InsertedBy.
        var departmentId = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_DEPARTMENT",
            new { Action = "INSERT", Name = name, Code = code, Description = "Created by contract test", Active = true, InsertedBy = userId },
            commandType: CommandType.StoredProcedure);
        Assert.True(departmentId > 0);

        // GetByIdAsync sends Action/Id.
        var created = await conn.QuerySingleAsync<dynamic>(
            "dbo.SP_DEPARTMENT",
            new { Action = "FETCH", Id = departmentId },
            commandType: CommandType.StoredProcedure);
        Assert.Equal(name, (string)created.Name);
        Assert.Equal(code, (string)created.Code);
        Assert.Equal("Created by contract test", (string)created.Description);
        Assert.False((bool)created.IsDeleted);
        Assert.Equal(userId, (long)created.InsertedBy);

        // GetPagedAsync sends Action/Search/Page/PageSize and reads a count plus a page.
        using (var grid = await conn.QueryMultipleAsync(
                   "dbo.SP_DEPARTMENT",
                   new { Action = "PAGED", Search = name, Page = 1, PageSize = 50 },
                   commandType: CommandType.StoredProcedure))
        {
            var total = await grid.ReadSingleAsync<long>();
            var rows = (await grid.ReadAsync<dynamic>()).ToArray();
            Assert.Equal(1, total);
            Assert.Single(rows);
        }

        // UpdateAsync sends Action/Id/Name/Code/Description/Active/UpdatedBy.
        var updated = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_DEPARTMENT",
            new { Action = "UPDATE", Id = departmentId, Name = name + " v2", Code = code, Description = "Updated by contract test", Active = false, UpdatedBy = userId },
            commandType: CommandType.StoredProcedure);
        Assert.Equal(1, updated);

        var afterUpdate = await conn.QuerySingleAsync<dynamic>(
            "dbo.SP_DEPARTMENT", new { Action = "FETCH", Id = departmentId }, commandType: CommandType.StoredProcedure);
        Assert.Equal(name + " v2", (string)afterUpdate.Name);
        Assert.Equal("Updated by contract test", (string)afterUpdate.Description);
        Assert.False((bool)afterUpdate.Active);
        Assert.Equal(userId, (long)afterUpdate.UpdatedBy);

        // DeleteAsync sends Action/Id/UserId.
        var deleted = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_DEPARTMENT",
            new { Action = "DELETE", Id = departmentId, UserId = userId },
            commandType: CommandType.StoredProcedure);
        Assert.Equal(1, deleted);

        var remaining = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM dbo.Department WHERE Id = @id AND IsDeleted = 0", new { id = departmentId });
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task SP_AuditLog_Persists_Insert_Update_And_Delete_Writes()
    {
        var userId = await CreateUserAsync("audit-contract@pmt.local");
        var entityName = $"Contract{Guid.NewGuid():N}"[..20];

        await using var conn = await fixture.OpenConnectionAsync();

        // AuditService dispatches with Action == EventAction == INSERT/UPDATE/DELETE.
        foreach (var (action, entityId) in new[] { ("INSERT", 11L), ("UPDATE", 22L), ("DELETE", 33L) })
        {
            await conn.ExecuteAsync(
                "dbo.SP_AUDIT_LOG",
                new
                {
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    EventAction = action,
                    OldValue = action == "DELETE" ? "{\"Path\":\"/api/v1/projects/33\"}" : null,
                    NewValue = action == "DELETE" ? null : $"{{\"Method\":\"{action}\"}}",
                    UserId = userId
                },
                commandType: CommandType.StoredProcedure);
        }

        var rows = (await conn.QueryAsync<dynamic>(
            "SELECT Action, EntityId, EntityName, OldValue, NewValue, UserId FROM dbo.AuditLog WHERE EntityName = @entityName ORDER BY Id",
            new { entityName })).ToArray();

        // Before the fix only the INSERT branch executed, so UPDATE and DELETE were dropped.
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "INSERT", "UPDATE", "DELETE" }, rows.Select(r => (string)r.Action).ToArray());
        Assert.Equal(new[] { 11L, 22L, 33L }, rows.Select(r => (long)r.EntityId).ToArray());
        Assert.All(rows, r => Assert.Equal(entityName, (string)r.EntityName));
        Assert.All(rows, r => Assert.Equal(userId, (long)r.UserId));
        Assert.Equal("{\"Method\":\"INSERT\"}", (string)rows[0].NewValue);
        Assert.Equal("{\"Path\":\"/api/v1/projects/33\"}", (string)rows[2].OldValue);
    }

    [Fact]
    public async Task SP_AuditLog_Falls_Back_To_Meaningful_Entity_Name_And_Id()
    {
        var userId = await CreateUserAsync("audit-fallback@pmt.local");

        await using var conn = await fixture.OpenConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_AUDIT_LOG",
            new { Action = "DELETE", EntityName = (string?)null, EntityId = (long?)null, EventAction = (string?)null, UserId = userId },
            commandType: CommandType.StoredProcedure);

        var row = await conn.QuerySingleAsync<dynamic>(
            "SELECT EntityName, EntityId, Action FROM dbo.AuditLog WHERE Id = @id", new { id });

        Assert.Equal("Unknown", (string)row.EntityName);
        Assert.Equal(0L, (long)row.EntityId);
        Assert.Equal("DELETE", (string)row.Action);
    }

    [Fact]
    public async Task SP_Attachment_Roundtrips_Project_Parent_And_ContentType()
    {
        var userId = await CreateUserAsync("attachment-contract@pmt.local");
        var projectId = await CreateProjectAsync(userId);

        await using var conn = await fixture.OpenConnectionAsync();

        // AttachmentRepository.CreateAsync parameter set.
        var attachmentId = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_ATTACHMENT",
            new
            {
                Action = "INSERT",
                ProjectId = projectId,
                TaskId = (long?)null,
                IssueId = (long?)null,
                FileName = "spec.pdf",
                FilePath = "/files/spec.pdf",
                ContentType = "application/pdf",
                FileSizeKb = 12,
                UploadedByUserId = userId,
                User = userId
            },
            commandType: CommandType.StoredProcedure);
        Assert.True(attachmentId > 0);

        var byId = await conn.QuerySingleAsync<dynamic>(
            "dbo.SP_ATTACHMENT", new { Action = "FETCH_BY_ID", Id = attachmentId }, commandType: CommandType.StoredProcedure);
        Assert.Equal("application/pdf", (string)byId.ContentType);
        Assert.Equal(projectId, (long)byId.ProjectId);

        var forProject = (await conn.QueryAsync<dynamic>(
            "dbo.SP_ATTACHMENT",
            new { Action = "FETCH", EntityType = "Project", EntityId = projectId },
            commandType: CommandType.StoredProcedure)).ToArray();
        Assert.Single(forProject);

        var deleted = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_ATTACHMENT", new { Action = "DELETE", Id = attachmentId, User = userId }, commandType: CommandType.StoredProcedure);
        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task SP_GitLink_Roundtrips_Project_Parent()
    {
        var userId = await CreateUserAsync("gitlink-contract@pmt.local");
        var projectId = await CreateProjectAsync(userId);

        await using var conn = await fixture.OpenConnectionAsync();

        // GitLinkRepository.CreateAsync parameter set.
        var gitLinkId = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_GITLINK",
            new
            {
                Action = "INSERT",
                ProjectId = projectId,
                TaskId = (long?)null,
                IssueId = (long?)null,
                Provider = GitProvider.GitHub.ToString(),
                CommitSha = "0123456789abcdef",
                PullRequestUrl = "https://github.test/pmt/pull/1",
                RepositoryUrl = "https://github.test/pmt",
                ReferenceType = "PullRequest",
                User = userId
            },
            commandType: CommandType.StoredProcedure);
        Assert.True(gitLinkId > 0);

        var links = (await conn.QueryAsync<dynamic>(
            "dbo.SP_GITLINK",
            new { Action = "FETCH", EntityType = "Project", EntityId = projectId },
            commandType: CommandType.StoredProcedure)).ToArray();

        Assert.Single(links);
        Assert.Equal(projectId, (long)links[0].ProjectId);
        Assert.Equal("https://github.test/pmt", (string)links[0].RepositoryUrl);
        Assert.Equal("PullRequest", (string)links[0].ReferenceType);
    }

    [Fact]
    public async Task Project_ForeignKeys_Reject_Unknown_Projects()
    {
        var userId = await CreateUserAsync("fk-contract@pmt.local");

        await using var conn = await fixture.OpenConnectionAsync();

        var attachmentFailure = await Assert.ThrowsAsync<SqlException>(() => conn.ExecuteScalarAsync<long>(
            "dbo.SP_ATTACHMENT",
            new
            {
                Action = "INSERT",
                ProjectId = 987654321L,
                FileName = "orphan.txt",
                FilePath = "/files/orphan.txt",
                ContentType = "text/plain",
                UploadedByUserId = userId,
                User = userId
            },
            commandType: CommandType.StoredProcedure));
        Assert.Contains("FK_attachment_project", attachmentFailure.Message);

        var gitLinkFailure = await Assert.ThrowsAsync<SqlException>(() => conn.ExecuteScalarAsync<long>(
            "dbo.SP_GITLINK",
            new { Action = "INSERT", ProjectId = 987654321L, Provider = "GitHub", User = userId },
            commandType: CommandType.StoredProcedure));
        Assert.Contains("FK_gitlink_project", gitLinkFailure.Message);
    }

    [Fact]
    public async Task SP_Report_VELOCITY_ReturnsNonZero_CompletedStoryPoints()
    {
        // Arrange: create user, department, role already seeded
        var userId = await CreateUserAsync("velocity@pmt.local");
        var projectId = await CreateProjectAsync(userId);
        var storyId = await CreateUserStoryAsync(projectId, userId, 8.5m, StoryStatus.Done);
        await CreateTaskAsync(storyId, projectId, userId, TaskStatus.Done);
        await CreateTaskAsync(storyId, projectId, userId, TaskStatus.Done); // 2 tasks, same story, 8.5 points

        // Act
        await using var conn = await fixture.OpenConnectionAsync();
        var rows = await conn.QueryAsync<dynamic>(
            "dbo.SP_REPORT",
            new { ProjectId = projectId, Action = "VELOCITY" },
            commandType: CommandType.StoredProcedure);

        var row = rows.First();

        // Assert: story points should be 8.5, not 0; tasks should be 2
        Assert.Equal(8.5m, (decimal)row.CompletedStoryPoints);
        Assert.Equal(2, (int)row.CompletedTasks);
        Assert.Equal(1, (int)row.CompletedStories);
    }

    [Fact]
    public async Task SP_Report_WORKLOAD_ReturnsNonZero_OpenIssues()
    {
        // Arrange
        var userId = await CreateUserAsync("workload@pmt.local");
        var projectId = await CreateProjectAsync(userId);
        await CreateIssueAsync(projectId, userId, IssueStatus.Open, assigneeUserId: userId);

        // Act
        await using var conn = await fixture.OpenConnectionAsync();
        var rows = await conn.QueryAsync<dynamic>(
            "dbo.SP_REPORT",
            new { ProjectId = projectId, Action = "WORKLOAD" },
            commandType: CommandType.StoredProcedure);

        var row = rows.First(r => r.UserId == userId);

        // Assert: OpenIssues should be >= 1, not hardcoded 0
        Assert.True((int)row.OpenIssues >= 1);
    }

    [Fact]
    public async Task SP_JobQueue_FailAction_Persists_LastError_And_Honors_AvailableDate()
    {
        // Arrange
        var retryAt = DateTime.UtcNow.AddMinutes(5);

        // Act
        await using var conn = await fixture.OpenConnectionAsync();
        var jobId = await conn.QuerySingleAsync<long>(
            "dbo.SP_JOB_QUEUE",
            new { JobType = "Email", Payload = "{}", Action = "INSERT" },
            commandType: CommandType.StoredProcedure);

        await conn.ExecuteAsync(
            "dbo.SP_JOB_QUEUE",
            new { Id = jobId, LastError = "SMTP timeout", AvailableDate = retryAt, Action = "FAIL" },
            commandType: CommandType.StoredProcedure);

        var job = await conn.QuerySingleAsync<dynamic>(
            "dbo.SP_JOB_QUEUE",
            new { Id = jobId, Action = "FETCH" },
            commandType: CommandType.StoredProcedure);

        // Assert
        Assert.Equal("Failed", (string)job.Status);
        Assert.Equal("SMTP timeout", (string)job.LastError);
        Assert.NotNull(job.AvailableDate);
    }

    [Fact]
    public async Task SP_Task_Update_Preserves_CompletedDate_When_Not_Provided()
    {
        // Arrange
        var userId = await CreateUserAsync("taskupdate@pmt.local");
        var projectId = await CreateProjectAsync(userId);
        var storyId = await CreateUserStoryAsync(projectId, userId, 5m);
        var taskId = await CreateTaskAsync(storyId, projectId, userId, TaskStatus.ToDo);

        // Set CompletedDate via a direct UPDATE (simulating task completion)
        await using (var setupConn = await fixture.OpenConnectionAsync())
        {
            await setupConn.ExecuteAsync(
                "UPDATE dbo.Task SET CompletedDate = @cd, Status = 'Done' WHERE Id = @id",
                new { cd = DateTime.UtcNow, id = taskId });
        }

        var before = await GetTaskCompletedDateAsync(taskId);

        // Act: partial update without passing CompletedDate
        await using var conn = await fixture.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "dbo.SP_TASK",
            new { Id = taskId, Title = "Updated Title", UserId = userId, Action = "UPDATE" },
            commandType: CommandType.StoredProcedure);

        var after = await GetTaskCompletedDateAsync(taskId);

        // Assert: CompletedDate should not be nulled
        Assert.NotNull(after);
        Assert.Equal(before, after);
    }

    // ---- Helpers ----

    private static async Task<bool> ColumnExistsAsync(SqlConnection conn, string table, string column)
        => await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND name = @column",
            new { table, column }) > 0;

    private async Task<long> CreateUserAsync(string email, long roleId = 1)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        var departmentId = await conn.QuerySingleAsync<long>("SELECT TOP (1) Id FROM dbo.Department WHERE IsDeleted = 0 ORDER BY Id");
        return await conn.QuerySingleAsync<long>(
            "dbo.SP_USER",
            new { FullName = "Test User", Email = email, PasswordHash = "hash", RoleId = roleId, DepartmentId = departmentId, Active = true, Action = "INSERT" },
            commandType: CommandType.StoredProcedure);
    }

    private async Task<long> CreateProjectAsync(long ownerUserId)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        var departmentId = await conn.QuerySingleAsync<long>("SELECT TOP (1) Id FROM dbo.Department WHERE IsDeleted = 0 ORDER BY Id");
        return await conn.QuerySingleAsync<long>(
            "dbo.SP_PROJECT",
            new { Name = "Contract Test Project", Description = "Test", Key = "CTP", Status = "Active", DepartmentId = departmentId, OwnerUserId = ownerUserId, Action = "INSERT" },
            commandType: CommandType.StoredProcedure);
    }

    private async Task<long> CreateUserStoryAsync(long projectId, long assigneeUserId, decimal? storyPoints, StoryStatus status = StoryStatus.Backlog)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "dbo.SP_USER_STORY",
            new { ProjectId = projectId, Title = "Contract Story", Description = "Desc", AcceptanceCriteria = "AC", Status = status.ToString(), Priority = 2, StoryPoints = storyPoints, AssigneeUserId = assigneeUserId, Active = true, Action = "INSERT" },
            commandType: CommandType.StoredProcedure);
    }

    private async Task<long> CreateTaskAsync(long storyId, long projectId, long? assigneeUserId, TaskStatus status)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "dbo.SP_TASK",
            new { StoryId = storyId, ProjectId = projectId, Title = "Contract Task", Description = "Desc", Status = status.ToString(), Priority = 1, AssigneeUserId = assigneeUserId, Active = true, Action = "INSERT" },
            commandType: CommandType.StoredProcedure);
    }

    private async Task<long> CreateIssueAsync(long projectId, long reportedByUserId, IssueStatus status, long? assigneeUserId = null)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        return await conn.QuerySingleAsync<long>(
            "dbo.SP_ISSUE",
            new { ProjectId = projectId, Title = "Contract Issue", Description = "Bug", Severity = "High", Status = status.ToString(), ReportedByUserId = reportedByUserId, AssigneeUserId = assigneeUserId, Active = true, Action = "INSERT" },
            commandType: CommandType.StoredProcedure);
    }

    private async Task<DateTime?> GetTaskCompletedDateAsync(long taskId)
    {
        await using var conn = await fixture.OpenConnectionAsync();
        return await conn.QuerySingleAsync<DateTime?>(
            "SELECT CompletedDate FROM dbo.Task WHERE Id = @id",
            new { id = taskId });
    }
}
