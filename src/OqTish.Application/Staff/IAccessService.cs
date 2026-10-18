namespace OqTish.Application.Staff;

/// <summary>Computes the caller's <see cref="AccessScope"/> from the database, once per request (cached in the DI scope).</summary>
public interface IAccessService
{
    Task<AccessScope> GetScopeAsync(CancellationToken cancellationToken);

    /// <summary>Scope for an explicit user (no caching). Used by GET /me, the Worker and tests.</summary>
    Task<AccessScope> GetScopeForUserAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Hook for Doctor-own semantics: maps the user to their doctor record per clinic. Implemented by the
/// Scheduling module (<c>DoctorIdentityResolver</c>: <c>scheduling.doctors.user_id</c>).
/// </summary>
public interface IDoctorIdentityResolver
{
    Task<IReadOnlyDictionary<Guid, Guid>> GetDoctorIdsAsync(Guid userId, IReadOnlyCollection<Guid> clinicIds, CancellationToken cancellationToken);
}
