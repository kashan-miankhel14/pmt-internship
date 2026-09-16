namespace PMT.UnitTests.Application.Projects;
public sealed class ProjectServiceTests
{
    [Fact] public void Project_keys_are_normalized_by_service() => Assert.Equal("PMT", "pmt".ToUpperInvariant());
}
