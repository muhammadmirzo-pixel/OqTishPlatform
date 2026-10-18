namespace OqTish.Domain.Staff;

/// <summary>
/// A branch granted to a <see cref="MembershipScope.Branches"/> membership. Composite FKs
/// (clinic_id, membership_id) and (clinic_id, branch_id) make cross-clinic grants impossible.
/// </summary>
public sealed class BranchAccess
{
    private BranchAccess()
    {
    }

    public Guid ClinicId { get; private set; }

    public Guid MembershipId { get; private set; }

    public Guid BranchId { get; private set; }

    public static BranchAccess Create(Guid clinicId, Guid membershipId, Guid branchId) => new()
    {
        ClinicId = clinicId,
        MembershipId = membershipId,
        BranchId = branchId,
    };
}
