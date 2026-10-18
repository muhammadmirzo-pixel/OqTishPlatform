using System.Text;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;

namespace OqTish.UnitTests.Staff;

/// <summary>
/// The role → permission map is the single source of truth (SECURITY.md §8). Any change must update
/// Snapshots/role-permissions.txt (and SECURITY.md §8) in the same change.
/// </summary>
public class PermissionsSnapshotTests
{
    public static string Render()
    {
        var sb = new StringBuilder();
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            sb.Append(role).Append(Permissions.TotpRequiredRoles.Contains(role) ? " [totp]" : string.Empty).Append('\n');
            foreach (var (permission, reach) in Permissions.For(role).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append("  ").Append(permission).Append(reach == PermissionReach.OwnOnly ? " (own)" : string.Empty).Append('\n');
            }
        }

        sb.Append("clinic-wide-only: ").Append(string.Join(", ", Permissions.ClinicWideOnly.Order(StringComparer.Ordinal))).Append('\n');
        sb.Append("platform: ").Append(string.Join(", ", PlatformPermissions.All)).Append('\n');
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            sb.Append("manages ").Append(role).Append(": ").Append(string.Join(", ", Permissions.RolesManageableBy(role))).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public void Role_permission_map_matches_snapshot()
    {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Snapshots", "role-permissions.txt")).ReplaceLineEndings("\n");
        var actual = Render();
        Assert.True(expected == actual, "Role map changed. New snapshot:\n" + actual);
    }

    [Fact]
    public void Every_mapped_permission_is_in_the_catalogue() =>
        Assert.All(Permissions.RoleMap.Values.SelectMany(m => m.Keys), p => Assert.Contains(p, Permissions.All));
}
