namespace PMT.Application.Common.Security;

public static class PermissionRequirement
{
    public const string ClaimType = "permission";
    public const string DepartmentsView = "departments.view";
    public const string DepartmentsManage = "departments.manage";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string ProjectsView = "projects.view";
    public const string ProjectsManage = "projects.manage";
    public const string StoriesView = "stories.view";
    public const string StoriesManage = "stories.manage";
    public const string TasksView = "tasks.view";
    public const string TasksManage = "tasks.manage";
    public const string IssuesView = "issues.view";
    public const string IssuesManage = "issues.manage";
    public const string CommentsManage = "comments.manage";
    public const string AttachmentsManage = "attachments.manage";
    public const string GitManage = "git.manage";
    public const string NotificationsManage = "notifications.manage";
    public const string ReportsView = "reports.view";

    public static IReadOnlyCollection<string> All { get; } =
    [
        DepartmentsView, DepartmentsManage, UsersView, UsersManage, ProjectsView, ProjectsManage,
        StoriesView, StoriesManage, TasksView, TasksManage, IssuesView, IssuesManage,
        CommentsManage, AttachmentsManage, GitManage, NotificationsManage, ReportsView
    ];
}
