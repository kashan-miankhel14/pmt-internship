using PMT.Domain.Enums;

namespace PMT.Application.UserStories;

public static class UserStoryStatusRules
{
    public static bool IsAllowed(StoryStatus current, StoryStatus next)
    {
        return true;
    }
}
