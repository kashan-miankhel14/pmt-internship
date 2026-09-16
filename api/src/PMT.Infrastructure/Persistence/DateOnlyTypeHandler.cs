using System.Data;
using Dapper;

namespace PMT.Infrastructure.Persistence;

public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value)
        => value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            string text when DateOnly.TryParse(text, out var date) => date,
            _ => throw new DataException($"Cannot convert {value.GetType().Name} to DateOnly.")
        };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }
}
