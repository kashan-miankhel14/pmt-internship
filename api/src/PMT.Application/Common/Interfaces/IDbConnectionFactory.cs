using System.Data.Common;

namespace PMT.Application.Common.Interfaces;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}
