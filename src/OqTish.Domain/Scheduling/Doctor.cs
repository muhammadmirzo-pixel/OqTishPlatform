using OqTish.Domain.Common;

namespace OqTish.Domain.Scheduling;

/// <summary>
/// A doctor of one clinic (A8). <see cref="UserId"/> optionally links the record to a staff account: that
/// user gets Doctor-own semantics (own appointments, own time off). A user is a doctor at most once per
/// clinic (<c>UNIQUE(clinic_id, user_id)</c>). <c>UNIQUE(clinic_id, id)</c> is the composite-FK target.
/// </summary>
public sealed class Doctor
{
    public const int MaxNameLength = 200;

    private Doctor()
    {
        DisplayName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid? UserId { get; private set; }

    public string DisplayName { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Doctor Create(Guid clinicId, string displayName, bool isActive, Guid? userId, DateTimeOffset now)
    {
        var doctor = new Doctor { Id = IdGenerator.New(), ClinicId = clinicId, CreatedAt = now };
        doctor.Update(displayName, isActive, userId, now);
        return doctor;
    }

    public void Update(string displayName, bool isActive, Guid? userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        IsActive = isActive;
        UserId = userId;
        UpdatedAt = now;
    }
}

/// <summary>The doctor works at the branch. Key (doctor_id, branch_id); composite FKs to doctor and branch.</summary>
public sealed class DoctorBranch
{
    private DoctorBranch()
    {
    }

    public Guid ClinicId { get; private set; }

    public Guid DoctorId { get; private set; }

    public Guid BranchId { get; private set; }

    public static DoctorBranch Create(Guid clinicId, Guid doctorId, Guid branchId) =>
        new() { ClinicId = clinicId, DoctorId = doctorId, BranchId = branchId };
}

/// <summary>The doctor provides the service. Key (doctor_id, service_id); composite FKs to doctor and service.</summary>
public sealed class DoctorService
{
    private DoctorService()
    {
    }

    public Guid ClinicId { get; private set; }

    public Guid DoctorId { get; private set; }

    public Guid ServiceId { get; private set; }

    public static DoctorService Create(Guid clinicId, Guid doctorId, Guid serviceId) =>
        new() { ClinicId = clinicId, DoctorId = doctorId, ServiceId = serviceId };
}
