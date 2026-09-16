namespace PMT.Application.Common.Caching;

/// <summary>
/// Region names and time-to-live values used with <see cref="ILookupCache"/>.
/// </summary>
/// <remarks>
/// TTLs are deliberately short. They are the only staleness bound when a write happens on a
/// different instance than the one serving the read, because <see cref="ILookupCache"/> is
/// per-process.
/// </remarks>
public static class CacheRegions
{
    /// <summary>Department reads. Evicted on any department create/update/delete.</summary>
    public const string Departments = "lookup:departments";

    /// <summary>User reads. Evicted on any user create/update/delete/role/password change.</summary>
    public const string Users = "lookup:users";

    /// <summary>The assignable-role list. No endpoint mutates roles, so this only expires.</summary>
    public const string Roles = "lookup:roles";

    /// <summary>Aggregate report queries. Time-expired only; no write path invalidates them.</summary>
    public const string Reports = "reports";

    /// <summary>TTL for entity lookups that are also invalidated explicitly on write.</summary>
    public static readonly TimeSpan LookupTtl = TimeSpan.FromSeconds(60);

    /// <summary>TTL for the role list, which is effectively static reference data.</summary>
    public static readonly TimeSpan RolesTtl = TimeSpan.FromMinutes(5);

    /// <summary>TTL for velocity/workload reports.</summary>
    public static readonly TimeSpan ReportsTtl = TimeSpan.FromSeconds(60);
}
