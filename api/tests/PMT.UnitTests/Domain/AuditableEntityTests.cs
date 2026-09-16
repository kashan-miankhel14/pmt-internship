using PMT.Domain.Entities;
namespace PMT.UnitTests.Domain;
public sealed class AuditableEntityTests
{
    [Fact] public void New_entity_has_safe_audit_defaults()
    {
        var entity=new Department();
        Assert.True(entity.Active); Assert.False(entity.IsDeleted); Assert.True(entity.InsertDate<=DateTime.UtcNow);
    }
}
