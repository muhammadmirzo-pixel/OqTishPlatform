using System.Net;
using System.Net.Http.Json;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>HTTP and DB helpers for the clinic console / admin / public tests (real API, real sign-in).</summary>
public static class ConsoleKit
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, object? body) =>
        client.PostAsJsonAsync(url, body ?? new { }, TestAuth.Json, Ct);

    public static Task<HttpResponseMessage> PutAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, TestAuth.Json, Ct);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(text, TestAuth.Json)!;
    }

    public static async Task<T> GetOkAsync<T>(this HttpClient client, string url) => await (await client.GetAsync(url, Ct)).ReadAsync<T>();

    public static void AssertStatus(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = response.Content.ReadAsStringAsync(Ct).GetAwaiter().GetResult();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
    }

    /// <summary>Signs in a new user (OTP) and grants the platform permissions directly in the DB (TOTP rule is off in Development).</summary>
    public static async Task<SignedInClient> SignInAsPlatformAdminAsync(ApiFactory factory, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var admin = await TestAuth.SignInAsPatientAsync(factory);
        await using var db = factory.CreateDbContext();
        foreach (var p in permissions.Length == 0 ? PlatformPermissions.All : permissions)
        {
            db.PlatformAdminGrants.Add(PlatformAdminGrant.Create(admin.Me.Id, p, null, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(Ct);
        return admin;
    }

    public static async Task<int> AuditCountAsync(ApiFactory factory, string action, Guid? clinicId = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var db = factory.CreateDbContext();
        return db.AuditLog.Count(a => a.Action == action && (clinicId == null || a.ClinicId == clinicId));
    }
}
