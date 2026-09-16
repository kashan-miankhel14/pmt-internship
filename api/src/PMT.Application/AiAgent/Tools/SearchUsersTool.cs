using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Users;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Resolves a person's name or email to a user id, for assignment.
/// </summary>
/// <remarks>
/// This tool returns email addresses, so it is gated behind users.view and requires a query.
/// A blank query would turn it into a "dump the staff directory" call, which is exactly the
/// kind of bulk PII read an agent should not be able to make by accident.
/// </remarks>
public sealed class SearchUsersTool(IUserRepository repository) : EntitySearchToolBase
{
    private const int MinimumQueryLength = 2;

    public override string Name => "search_users";

    public override string Description =>
        "Find people by name or email to resolve the userId used for assignment. "
        + "A search term is required.";

    public override string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Name or email fragment to search for. Required." },
            "limit": { "type": "integer", "description": "Maximum records to return (1-25). Defaults to 10." }
          },
          "required": ["query"]
        }
        """;

    public override string? RequiredPermission => PermissionRequirement.UsersView;

    protected override async Task<AgentToolResult> SearchAsync(
        AgentToolContext context, string? query, int limit, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (query is null || query.Length < MinimumQueryLength)
            return AgentToolResult.Fail($"'query' is required and must be at least {MinimumQueryLength} characters.");

        var pool = await repository.GetPagedAsync(1, AgentToolScope.CandidatePoolSize, query, cancellationToken);

        var results = pool.Items
            .Where(x => !x.IsDeleted)
            .Take(limit)
            .Select(x => new
            {
                id = x.Id,
                displayName = x.DisplayName,
                email = x.Email,
                departmentId = x.DepartmentId
            })
            .ToArray();

        return Results(results, pool.TotalCount);
    }
}
