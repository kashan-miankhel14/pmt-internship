using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Tools;
using PMT.Application.Attachments;
using PMT.Application.Auth;
using PMT.Application.Boards;
using PMT.Application.Comments;
using PMT.Application.Common.Caching;
using PMT.Application.Common.Interfaces;
using PMT.Application.Departments;
using PMT.Application.GitLinks;
using PMT.Application.Issues;
using PMT.Application.Notifications;
using PMT.Application.Projects;
using PMT.Application.Reports;
using PMT.Application.Tasks;
using PMT.Application.Sprints;
using PMT.Application.Teams;
using PMT.Application.Users;
using PMT.Application.UserStories;
using PMT.Application.Workflow;
using PMT.Infrastructure.Ai;
using PMT.Infrastructure.Ai.Tools;
using PMT.Infrastructure.BackgroundJobs;
using PMT.Infrastructure.Caching;
using PMT.Infrastructure.Email;
using PMT.Infrastructure.GitIntegration;
using PMT.Infrastructure.Identity;
using PMT.Infrastructure.Persistence;
using PMT.Infrastructure.Persistence.Repositories;
using PMT.Infrastructure.RealTime;
using PMT.Infrastructure.Security;
using PMT.Domain.Enums;

namespace PMT.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        // WorkflowTransition.ConditionJson / ValidatorJson / PostFunctionJson are JsonNode
        // properties over nvarchar(max) columns; Dapper cannot materialise them without this.
        SqlMapper.AddTypeHandler(new JsonNodeTypeHandler());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<ProjectStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<StoryStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<SprintStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<PMT.Domain.Enums.TaskStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<IssueStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<WorkflowStatusCategory>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<IssueSeverity>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<GitProvider>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<JobStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<AiChatRole>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<AiToolStatus>());
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<AiAutoReindexOptions>(configuration.GetSection(AiAutoReindexOptions.SectionName));
        services.AddDistributedMemoryCache();
        // Backing store for ILookupCache. In-process and unserialized, so lookup hits avoid
        // both the round trip and the JSON encode/decode that IDistributedCache imposes.
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddSignalR();

        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IUserStoryRepository, UserStoryRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IIssueRepository, IssueRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IGitLinkRepository, GitLinkRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IJobQueueRepository, JobQueueRepository>();
        services.AddScoped<IAiChatRepository, AiChatRepository>();
        services.AddScoped<IAiIndexRepository, AiIndexRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IProjectAccessRepository, ProjectAccessRepository>();
        services.AddScoped<IProjectRoleRepository, ProjectRoleRepository>();
        services.AddScoped<ISprintRepository, SprintRepository>();
        services.AddScoped<IBoardColumnRepository, BoardColumnRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<AdminBootstrapper>();
        services.AddScoped<IJobQueueService, JobQueueService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton<ICacheService, DistributedCacheService>();
        services.AddSingleton<ILookupCache, LookupCache>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IDummyHashProvider, DummyHashProvider>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IEncryptionService, DataProtectionEncryptionService>();
        services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
        services.AddScoped<IpWhitelistProvider>();
        services.AddHostedService<JobQueueWorker>();
        services.AddHostedService<TokenCleanupWorker>();

        services.AddHttpClient<GitHubClient>(client => client.BaseAddress = new Uri("https://api.github.com/"));
        services.AddHttpClient<GitLabClient>(client => client.BaseAddress = new Uri("https://gitlab.com/api/v4/"));
        services.AddKeyedScoped<IGitProviderClient, GitHubClient>(GitProvider.GitHub);
        services.AddKeyedScoped<IGitProviderClient, GitLabClient>(GitProvider.GitLab);
        services.AddAiAgent(configuration);
        return services;
    }

    /// <summary>
    /// Registers the AI agent stack: the chat backend selected by <c>Ai:ChatProvider</c>,
    /// Ollama for embeddings, the RAG indexer, the chat services and the tool registry.
    /// </summary>
    /// <remarks>
    /// Only chat completions are switchable. Embeddings and the legacy <see cref="IAiAssistant"/>
    /// helpers stay on Ollama whichever provider is active, because the vectors already stored in
    /// AiDocumentChunk belong to the model that produced them: swapping the embedding backend
    /// would degrade retrieval silently until a full reindex. Keeping them put is also what makes
    /// the switch reversible — flipping <c>Ai:ChatProvider</c> back to "Ollama" restores the
    /// previous behaviour with no data migration.
    /// </remarks>
    private static IServiceCollection AddAiAgent(this IServiceCollection services, IConfiguration configuration)
    {
        // =========================================================
        // Ollama — local chat completions + embeddings
        // =========================================================
        services.AddHttpClient<OllamaClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = options.RequestTimeout;
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                ConnectTimeout = options.ConnectTimeout,
                MaxConnectionsPerServer = 32,
                AutomaticDecompression = DecompressionMethods.All
            };
        })
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // =========================================================
        // Gemini — cloud chat completions (no embeddings)
        // =========================================================
        // The host is fixed; the model is a path segment and the API key an x-goog-api-key
        // request header, both applied per request by the client. The key is deliberately kept
        // out of the URI so it cannot leak through proxy logs or exception messages. Registered
        // unconditionally so switching provider is configuration-only, and so a misconfigured
        // Gemini section fails inside the client (as a 503 the operator can read) rather than at
        // container build time.
        services.AddHttpClient<GeminiChatClient>((sp, client) =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = ReadGeminiRequestTimeout(sp);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            // Reaching a public TLS endpoint is fast even when generation is slow, so a long
            // connect means the network is the problem and should fail early.
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 32,
            AutomaticDecompression = DecompressionMethods.All
        })
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // Chat completions follow Ai:ChatProvider; anything other than an explicit "Gemini"
        // (including a missing section) stays on Ollama. The raw configuration string is read
        // rather than the bound options object so an unrelated malformed value in the same
        // section cannot stop the application from starting.
        var chatProvider = AiOptions.Parse(configuration[$"{AiOptions.SectionName}:{nameof(AiOptions.ChatProvider)}"]);

        if (chatProvider == AiChatProvider.Gemini)
            services.AddTransient<IAiChatCompletionClient>(sp => sp.GetRequiredService<GeminiChatClient>());
        else
            services.AddTransient<IAiChatCompletionClient>(sp => sp.GetRequiredService<OllamaClient>());

        // Embeddings and the legacy assistant helpers are Ollama's regardless of the chat
        // provider: see the remarks above.
        services.AddTransient<IAiEmbeddingClient>(sp => sp.GetRequiredService<OllamaClient>());
        services.AddTransient<IAiAssistant>(sp => sp.GetRequiredService<OllamaClient>());

        // Step and wall-clock budgets for one turn, taken from whichever provider is active.
        services.AddSingleton<IAiTurnPolicy, AiTurnPolicy>();

        services.AddScoped<IAiChatService, AiChatService>();
        services.AddScoped<IAiIndexingService, AiIndexingService>();
        services.AddScoped<IAiAgentOrchestrator, AiAgentOrchestrator>();

        // Singleton so the single-flight guard and last-run status survive across requests.
        services.AddSingleton<AiReindexRunner>();

        // Scheduled refresh of the retrieval index. Registered unconditionally so enabling it is
        // a configuration change rather than a deployment change: the service reads
        // Ai:AutoReindex at startup and returns immediately when it is off (the default). It
        // shares AiReindexRunner's single-flight gate, so a scheduled run and the admin endpoint
        // can never rebuild at the same time.
        services.AddHostedService<AiAutoReindexService>();

        // Singleton so a confirmation issued by one request can be redeemed by the next. Entries
        // are short-lived, single-use and capped; see AgentConfirmationStore for the trade-offs.
        services.AddSingleton<AgentConfirmationStore>();

        // Describes a parked destructive action in plain language, reading titles from the
        // records themselves so the user approves what will actually be deleted.
        services.AddScoped<AgentActionSummarizer>();

        // Shared scope/validation helper used by every entity tool.
        services.AddScoped<AgentToolScope>();

        // Tools are resolved as IEnumerable<IAgentTool> by the orchestrator; adding a new
        // capability is a single registration here. Tool names must stay unique: the
        // orchestrator keys them with ToDictionary and a duplicate throws at construction.
        services.AddScoped<IAgentTool, SearchKnowledgeTool>();

        // Conversation tool. Executes nothing: it is how Anna asks the user for a value she is
        // missing instead of inventing one, so it must be registered alongside the write tools
        // rather than as an alternative to them.
        services.AddScoped<IAgentTool, AskForFieldsTool>();

        // Read tools.
        services.AddScoped<IAgentTool, SearchProjectsTool>();
        services.AddScoped<IAgentTool, SearchStoriesTool>();
        services.AddScoped<IAgentTool, SearchTasksTool>();
        services.AddScoped<IAgentTool, SearchIssuesTool>();
        services.AddScoped<IAgentTool, SearchUsersTool>();
        services.AddScoped<IAgentTool, SearchTeamsTool>();
        services.AddScoped<IAgentTool, SearchSprintsTool>();
        services.AddScoped<IAgentTool, ListBoardColumnsTool>();
        services.AddScoped<IAgentTool, GetEntityTool>();
        services.AddScoped<IAgentTool, SearchDepartmentsTool>();

        // Reporting read tools. Both are scoped by the caller's project access and gated on
        // reports.view, so Anna can answer "how are we doing" questions without a second hop
        // through the REST layer.
        services.AddScoped<IAgentTool, GetVelocityReportTool>();
        services.AddScoped<IAgentTool, GetWorkloadReportTool>();

        // Collaboration read tools. These back the follow-up questions people ask once they have
        // found an entity: who said what, what is attached, what changed and when.
        services.AddScoped<IAgentTool, ListCommentsTool>();
        services.AddScoped<IAgentTool, ListAttachmentsTool>();
        services.AddScoped<IAgentTool, ListGitLinksTool>();
        services.AddScoped<IAgentTool, GetEntityHistoryTool>();
        services.AddScoped<IAgentTool, GetMyNotificationsTool>();

        // Access read tools. list_project_roles is what lets Anna discover the current role ids
        // instead of guessing them, so it must stay registered alongside the membership writes.
        services.AddScoped<IAgentTool, ListProjectMembersTool>();
        services.AddScoped<IAgentTool, ListProjectTeamGrantsTool>();
        services.AddScoped<IAgentTool, ListProjectRolesTool>();
        services.AddScoped<IAgentTool, GetMyProjectRoleTool>();
        services.AddScoped<IAgentTool, ListTeamMembersTool>();

        // Workflow read tools. The board and the transition graph are the two things Anna has to
        // read before she can propose a status change that the workflow will actually accept.
        services.AddScoped<IAgentTool, ListWorkflowStatusesTool>();
        services.AddScoped<IAgentTool, ListWorkflowTransitionsTool>();

        // Write tools. These are marked non-destructive, so the orchestrator will let the model
        // call them without parking the turn; Anna is instructed to gather the required fields
        // with ask_for_fields and to get the user's yes before she calls one. See the note in
        // AiAgentOrchestrator.
        services.AddScoped<IAgentTool, CreateProjectTool>();
        services.AddScoped<IAgentTool, UpdateProjectTool>();
        services.AddScoped<IAgentTool, CreateStoryTool>();
        services.AddScoped<IAgentTool, UpdateStoryTool>();
        services.AddScoped<IAgentTool, CreateTaskTool>();
        services.AddScoped<IAgentTool, UpdateTaskTool>();
        services.AddScoped<IAgentTool, CreateIssueTool>();
        services.AddScoped<IAgentTool, UpdateIssueTool>();
        services.AddScoped<IAgentTool, AddCommentTool>();
        services.AddScoped<IAgentTool, UpdateCommentTool>();
        services.AddScoped<IAgentTool, CreateDepartmentTool>();
        services.AddScoped<IAgentTool, UpdateDepartmentTool>();
        services.AddScoped<IAgentTool, CreateGitLinkTool>();

        // Notification tools. Creating one is a write rather than a delete, but it is visible to
        // someone else the moment it lands, so Anna is expected to read the recipient and the
        // message back to the caller first — same discipline as the membership tools below.
        services.AddScoped<IAgentTool, CreateNotificationTool>();
        services.AddScoped<IAgentTool, MarkNotificationReadTool>();

        // Scaffolding tool. create_project_from_template writes a project plus its board in one
        // call, so it is registered with the writes rather than the planning tools it produces.
        services.AddScoped<IAgentTool, CreateProjectFromTemplateTool>();

        // Planning tools. Sprints and board columns are addressed by the project's public key
        // rather than by its surrogate id, matching their REST routes and the way people name a
        // project out loud; ProjectKeyResolver turns the key into the id the repositories take.
        // complete_sprint is a one-way transition, so Anna is expected to name the sprint back to
        // the user and get a yes before she calls it, exactly as she does for the access tools.
        services.AddScoped<IAgentTool, CreateSprintTool>();
        services.AddScoped<IAgentTool, UpdateSprintTool>();
        services.AddScoped<IAgentTool, StartSprintTool>();
        services.AddScoped<IAgentTool, CompleteSprintTool>();
        services.AddScoped<IAgentTool, CreateBoardColumnTool>();
        services.AddScoped<IAgentTool, UpdateBoardColumnTool>();

        // Teams & Access tools. Membership is the one area where a wrong call hands someone
        // access rather than mistyping a title, so Anna is expected to name the user, the team
        // and the role back to the caller before she invokes any of these.
        services.AddScoped<IAgentTool, CreateTeamTool>();
        services.AddScoped<IAgentTool, UpdateTeamTool>();
        services.AddScoped<IAgentTool, AddTeamMemberTool>();
        services.AddScoped<IAgentTool, RemoveTeamMemberTool>();
        services.AddScoped<IAgentTool, AddProjectMemberTool>();
        services.AddScoped<IAgentTool, RemoveProjectMemberTool>();
        services.AddScoped<IAgentTool, AddProjectTeamTool>();
        services.AddScoped<IAgentTool, RemoveProjectTeamTool>();

        // Destructive tools. The model may ask for these, but the orchestrator parks the call and
        // only runs it after the user approves it through /ai/agent/confirm.
        services.AddScoped<IAgentTool, DeleteStoryTool>();
        services.AddScoped<IAgentTool, DeleteTaskTool>();
        services.AddScoped<IAgentTool, DeleteIssueTool>();
        services.AddScoped<IAgentTool, DeleteCommentTool>();
        services.AddScoped<IAgentTool, DeleteTeamTool>();
        services.AddScoped<IAgentTool, DeleteSprintTool>();
        services.AddScoped<IAgentTool, DeleteBoardColumnTool>();

        return services;
    }

    /// <summary>
    /// Per-request HTTP timeout for the Gemini client, falling back to the compiled default when
    /// the section cannot be bound.
    /// </summary>
    /// <remarks>
    /// This delegate runs while the typed client is being constructed, so an exception here would
    /// escape as a 500 from whatever request happened to resolve the orchestrator first. The
    /// fallback keeps construction working; the client re-reads the same options and reports the
    /// binding failure as a 503 with a message that names the section.
    /// </remarks>
    private static TimeSpan ReadGeminiRequestTimeout(IServiceProvider serviceProvider)
    {
        try
        {
            return serviceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value.RequestTimeout;
        }
        catch
        {
            return new GeminiOptions().RequestTimeout;
        }
    }
}
