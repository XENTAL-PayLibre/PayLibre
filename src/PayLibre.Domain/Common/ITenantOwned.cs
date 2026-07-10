namespace PayLibre.Domain.Common;

/// <summary>
/// Marks an entity as owned by a tenant (a <c>School</c>). The persistence layer applies a global
/// query filter on <see cref="TenantId"/> so a request can only ever read its own school's rows.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
