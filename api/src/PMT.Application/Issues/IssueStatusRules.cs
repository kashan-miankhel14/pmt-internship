using PMT.Domain.Enums;

namespace PMT.Application.Issues;

public static class IssueStatusRules
{
    public static bool IsAllowed(IssueStatus current, IssueStatus next)
    {
        return true;
    }
}
