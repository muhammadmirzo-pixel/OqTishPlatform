namespace OqTish.Domain.Staff;

public enum StaffRole
{
    ClinicOwner = 1,
    ClinicAdmin = 2,
    Receptionist = 3,
    Doctor = 4,
    FinanceManager = 5,
}

/// <summary><c>Clinic</c>: every branch, including future ones. <c>Branches</c>: only granted branches.</summary>
public enum MembershipScope
{
    Clinic = 1,
    Branches = 2,
}

public enum MembershipStatus
{
    Active = 1,
    Suspended = 2,
    Removed = 3,
}
