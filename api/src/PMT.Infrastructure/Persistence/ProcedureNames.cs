using PMT.Domain.Enums;

namespace PMT.Infrastructure.Persistence;

public static class ProcedureNames
{
    // Special / Domain Specific Stored Procedures
    public const string SprintStart = "dbo.usp_Sprint_Start";
    public const string SprintComplete = "dbo.usp_Sprint_Complete";
    public const string SprintWorkflow = "dbo.usp_Sprint_Workflow";
    public const string ProjectCreateFromTemplate = "dbo.usp_Project_CreateFromTemplate";
    public const string ProjectEffectiveRole = "dbo.usp_Project_EffectiveRole";
    public const string AuthRotateRefreshToken = "dbo.usp_Auth_RotateRefreshToken";

    // AI Session & Tool Call Stored Procedures
    public const string AiChatSessionCreate = "dbo.usp_AiChatSession_Create";
    public const string AiChatSessionListByUser = "dbo.usp_AiChatSession_ListByUser";
    public const string AiChatSessionGetById = "dbo.usp_AiChatSession_GetById";
    public const string AiChatSessionDelete = "dbo.usp_AiChatSession_Delete";
    public const string AiChatMessageCreate = "dbo.usp_AiChatMessage_Create";
    public const string AiChatMessageListBySession = "dbo.usp_AiChatMessage_ListBySession";
    public const string AiAgentToolCallCreate = "dbo.usp_AiAgentToolCall_Create";
    public const string AiAgentToolCallComplete = "dbo.usp_AiAgentToolCall_Complete";
    public const string AiAgentToolCallListBySession = "dbo.usp_AiAgentToolCall_ListBySession";
    public const string AiDocumentChunkUpsert = "dbo.usp_AiDocumentChunk_Upsert";
    public const string AiDocumentChunkDelete = "dbo.usp_AiDocumentChunk_Delete";
    public const string AiDocumentChunkSearch = "dbo.usp_AiDocumentChunk_Search";
    public const string AiDocumentChunkSourceSnapshot = "dbo.usp_AiDocumentChunk_SourceSnapshot";

    public static string Get(StoredProcedure procedure) => procedure switch
    {
        StoredProcedure.UserStory => "dbo.SP_USER_STORY",
        StoredProcedure.RefreshToken => "dbo.SP_REFRESH_TOKEN",
        StoredProcedure.RolePermission => "dbo.SP_ROLE_PERMISSION",
        StoredProcedure.NotificationPreference => "dbo.SP_NOTIFICATION_PREFERENCE",
        StoredProcedure.JobQueue => "dbo.SP_JOB_QUEUE",
        StoredProcedure.AuditLog => "dbo.SP_AUDIT_LOG",
        StoredProcedure.IpWhitelist => "dbo.SP_IP_WHITELIST",
        StoredProcedure.TeamMember => "dbo.SP_TEAM_MEMBER",
        StoredProcedure.ProjectMember => "dbo.SP_PROJECT_MEMBER",
        StoredProcedure.ProjectTeam => "dbo.SP_PROJECT_TEAM",
        StoredProcedure.ProjectRole => "dbo.SP_PROJECT_ROLE",
        StoredProcedure.BoardColumn => "dbo.SP_BOARD_COLUMN",
        StoredProcedure.Workflow => "dbo.SP_WORKFLOW",
        StoredProcedure.WorkflowStatus => "dbo.SP_WORKFLOW_STATUS",
        StoredProcedure.WorkflowTransition => "dbo.SP_WORKFLOW_TRANSITION",
        StoredProcedure.WorkflowScheme => "dbo.SP_WORKFLOW_SCHEME",
        StoredProcedure.IssueHistory => "dbo.SP_ISSUE_HISTORY",
        _ => $"dbo.SP_{procedure.ToString().ToUpperInvariant()}"
    };

    public static string Action(ProcedureAction action) => action switch
    {
        ProcedureAction.UpdatePassword => "UPDATEPASSWORD",
        ProcedureAction.LoginSuccess => "LOGINSUCCESS",
        ProcedureAction.LoginFailure => "LOGINFAILURE",
        ProcedureAction.CheckChildren => "CHECKCHILDREN",
        ProcedureAction.FetchByKey => "FETCHBYKEY",
        ProcedureAction.ResetPassword => "RESETPASSWORD",
        ProcedureAction.Rotate => "ROTATE",
        ProcedureAction.RevokeAll => "REVOKEALL",
        ProcedureAction.Cleanup => "CLEANUP",
        _ => action.ToString().ToUpperInvariant()
    };
}
