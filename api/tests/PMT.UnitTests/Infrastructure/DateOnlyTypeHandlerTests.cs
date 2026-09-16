using System.Data;
using PMT.Infrastructure.Persistence;

namespace PMT.UnitTests.Infrastructure;

#pragma warning disable CS8769
public sealed class DateOnlyTypeHandlerTests
{
    [Fact]
    public void SetValue_uses_sql_date_and_midnight_value()
    {
        var parameter = new FakeParameter();
        var date = new DateOnly(2026, 8, 3);

        new DateOnlyTypeHandler().SetValue(parameter, date);

        Assert.Equal(DbType.Date, parameter.DbType);
        Assert.Equal(date.ToDateTime(TimeOnly.MinValue), parameter.Value);
    }

    private sealed class FakeParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable => true;
        private string? parameterName;
        private string? sourceColumn;
        string IDataParameter.ParameterName { get => parameterName ?? string.Empty; set => parameterName = value; }
        string IDataParameter.SourceColumn { get => sourceColumn ?? string.Empty; set => sourceColumn = value; }
        public DataRowVersion SourceVersion { get; set; }
        public object? Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }
}
#pragma warning restore CS8769
