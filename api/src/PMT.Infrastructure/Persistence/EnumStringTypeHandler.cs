using System.Data;
using Dapper;

namespace PMT.Infrastructure.Persistence;

internal sealed class EnumStringTypeHandler<TEnum> : SqlMapper.TypeHandler<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Parse(object value)
        => value switch
        {
            string text when Enum.TryParse<TEnum>(text, true, out var parsed) => parsed,
            _ => throw new DataException($"Cannot convert '{value}' to {typeof(TEnum).Name}.")
        };

    public override void SetValue(IDbDataParameter parameter, TEnum value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToString();
    }
}
