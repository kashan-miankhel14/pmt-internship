using TaskStatus=PMT.Domain.Enums.TaskStatus;
using PMT.Application.Tasks;
using PMT.Application.Issues;
using PMT.Domain.Enums;
namespace PMT.UnitTests.Application.Tasks;
public sealed class TaskServiceTests
{
    [Fact] public void Done_has_expected_persisted_value() => Assert.Equal(4,(int)TaskStatus.Done);

    [Fact]
    public void ToDo_can_move_directly_to_done() => Assert.True(TaskStatusRules.IsAllowed(TaskStatus.ToDo, TaskStatus.Done));

    [Fact]
    public void Review_can_move_to_done() => Assert.True(TaskStatusRules.IsAllowed(TaskStatus.Review, TaskStatus.Done));

    [Fact]
    public void Done_can_be_reopened() => Assert.True(TaskStatusRules.IsAllowed(TaskStatus.Done, TaskStatus.ToDo));

    [Fact]
    public void Resolved_issue_can_be_closed() => Assert.True(IssueStatusRules.IsAllowed(IssueStatus.Resolved, IssueStatus.Closed));
}
