using System.Data;using System.Net;using Dapper;using PMT.Application.Common.Interfaces;using PMT.Domain.Enums;using PMT.Infrastructure.Persistence;
namespace PMT.Infrastructure.Security;
public sealed class IpWhitelistProvider(IDbConnectionFactory f,ICacheService cache)
{
 const string Key="security:ip-whitelist";
 public async Task<bool>IsAllowedAsync(IPAddress address,CancellationToken ct=default){var cidrs=await cache.GetAsync<IReadOnlyCollection<string>>(Key,ct);if(cidrs is null){await using var c=f.CreateConnection();cidrs=(await c.QueryAsync<string>(new(ProcedureNames.Get(StoredProcedure.IpWhitelist),new{Action=ProcedureNames.Action(ProcedureAction.Fetch)},commandType:CommandType.StoredProcedure,cancellationToken:ct))).AsList();await cache.SetAsync(Key,cidrs,TimeSpan.FromMinutes(5),ct);}return cidrs.Count==0||cidrs.Any(x=>Contains(x,address));}
 static bool Contains(string cidr,IPAddress address){var p=cidr.Split('/',2);if(!IPAddress.TryParse(p[0],out var network)||network.AddressFamily!=address.AddressFamily)return false;var prefix=p.Length==2&&int.TryParse(p[1],out var parsed)?parsed:network.GetAddressBytes().Length*8;var n=network.GetAddressBytes();var a=address.GetAddressBytes();for(var i=0;i<n.Length;i++){var bits=Math.Clamp(prefix-i*8,0,8);if(bits==0)break;var mask=(byte)(0xFF<<(8-bits));if((n[i]&mask)!=(a[i]&mask))return false;}return true;}
}
