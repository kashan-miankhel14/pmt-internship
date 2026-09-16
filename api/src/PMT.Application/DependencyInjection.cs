using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PMT.Application.Auth;
using PMT.Application.Attachments;
using PMT.Application.Boards;
using PMT.Application.Comments;
using PMT.Application.Common.Realtime;
using PMT.Application.Departments;
using PMT.Application.GitLinks;
using PMT.Application.Issues;
using PMT.Application.Notifications;
using PMT.Application.Projects;
using PMT.Application.Reports;
using PMT.Application.Security;
using PMT.Application.Sprints;
using PMT.Application.Tasks;
using PMT.Application.Teams;
using PMT.Application.Users;
using PMT.Application.UserStories;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;

namespace PMT.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining(typeof(DependencyInjection));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<DepartmentService>();
        services.AddScoped<UserService>();
        services.AddScoped<ProjectService>();
        // Repository implementations live in PMT.Infrastructure and are registered there;
        // this layer only owns the interfaces and the services that consume them.
        services.AddScoped<ProjectAccessService>();
        services.AddScoped<TeamService>();
        services.AddScoped<SprintService>();
        services.AddScoped<BoardColumnService>();
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddScoped<UserStoryService>();
        services.AddScoped<TaskService>();
        services.AddScoped<IssueService>();
        services.AddScoped<CommentService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<GitLinkService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ReportService>();

        // Publishes the project-scoped "entityChanged" feed after a write. Registered once and
        // shared by every write service so the group name and payload have a single definition.
        services.AddScoped<EntityChangeBroadcaster>();

        // Workflow engine: data-driven status transitions with condition/validator handlers.
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<IConditionHandler, SubTasksResolvedCondition>();
        services.AddScoped<IValidatorHandler, CommentRequiredValidator>();
        services.AddScoped<IValidatorHandler, RequiredFieldValidator>();

        return services;
    }
}
