using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Application.Tasks;

public static class TaskStatusRules
{
    public static bool IsAllowed(TaskStatus current, TaskStatus next)
    {
        return true;
    }
}
