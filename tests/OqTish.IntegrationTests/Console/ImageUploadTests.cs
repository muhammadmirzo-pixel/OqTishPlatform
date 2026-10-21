using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Public;
using OqTish.Application.Scheduling;
using OqTish.Application.Staff;
using OqTish.Application.Tenancy;
using OqTish.Domain.Common;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Console;

/// <summary>Stored images: clinic logo, user avatar, GET /api/files/images/{id} (docs/API.md §10).</summary>
public class ImageUploadTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png(int size = 64) => Image([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);

    private static byte[] Jpeg(int size = 64) => Image([0xFF, 0xD8, 0xFF, 0xE0], size);

    private static byte[] WebP(int size = 64) => Image([.. "RIFF"u8, 0x10, 0, 0, 0, .. "WEBPVP8 "u8], size);

    private static byte[] Image(byte[] magic, int size)
    {
        var bytes = new byte[Math.Max(size, magic.Length)];
        Random.Shared.NextBytes(bytes);
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>A multipart body like the browser's FormData; the declared content type is deliberately not trusted by the server.</summary>
    private static MultipartFormDataContent Form(byte[] bytes, string declaredType = "image/png", string field = "file")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        return new MultipartFormDataContent { { file, field, "upload.bin" } };
    }

    private static Task<HttpResponseMessage> PutFormAsync(HttpClient client, string url, HttpContent content) => client.PutAsync(url, content, Ct);

    private static async Task<int> ImageCountAsync(ApiFactory factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.StoredImages.CountAsync(Ct);
    }

    private static async Task AssertFieldErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("common.validation", doc.RootElement.GetProperty("code").GetString());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("file", out var messages) && messages.GetArrayLength() > 0, body);
    }

    [Fact]
    public async Task Clinic_logo_is_uploaded_served_replaced_and_removed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var clinic = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, clinic.ClinicId, StaffRole.ClinicOwner);
        var anonymous = factory.CreateApiClient(withClientHeader: false);
        var logoUrl = $"/api/clinics/{clinic.ClinicId}/logo";

        var png = Png(5000);
        var first = await (await PutFormAsync(owner.Client, logoUrl, Form(png))).ReadAsync<ClinicLogoDto>();

        // Served anonymously with the detected type, immutable caching, a strong ETag and nosniff.
        var served = await anonymous.GetAsync($"/api/files/images/{first.LogoId}", Ct);
        served.AssertStatus(HttpStatusCode.OK);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await served.Content.ReadAsByteArrayAsync(Ct));
        var cache = served.Headers.CacheControl!;
        Assert.True(cache.Public);
        Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
        Assert.Contains(cache.Extensions, e => e.Name == "immutable");
        Assert.Contains("nosniff", served.Headers.GetValues("X-Content-Type-Options"));
        var etag = served.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(etag.IsWeak);

        using (var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/files/images/{first.LogoId}"))
        {
            conditional.Headers.IfNoneMatch.Add(etag);
            Assert.Equal(HttpStatusCode.NotModified, (await anonymous.SendAsync(conditional, Ct)).StatusCode);
        }

        // The id appears on the profile, in GET /me, and on the public card + detail.
        var profile = await owner.Client.GetOkAsync<ClinicProfileDto>($"/api/clinics/{clinic.ClinicId}");
        Assert.Equal(first.LogoId, profile.LogoId);
        var me = await owner.Client.GetOkAsync<MeDto>("/api/me");
        Assert.Equal(first.LogoId, Assert.Single(me.Memberships).ClinicLogoId);
        Assert.Equal(first.LogoId, (await anonymous.GetOkAsync<ClinicDetailDto>($"/api/public/clinics/{clinic.ClinicId}")).LogoId);
        var cards = await anonymous.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?q=" + Uri.EscapeDataString(profile.Name));
        Assert.Equal(first.LogoId, Assert.Single(cards.Items).LogoId);

        // Replacing: the type comes from the bytes (JPEG although the client claims PNG); the old image is deleted.
        var second = await (await PutFormAsync(owner.Client, logoUrl, Form(Jpeg(), declaredType: "image/png"))).ReadAsync<ClinicLogoDto>();
        Assert.NotEqual(first.LogoId, second.LogoId);
        (await anonymous.GetAsync($"/api/files/images/{first.LogoId}", Ct)).AssertStatus(HttpStatusCode.NotFound);
        Assert.Equal("image/jpeg", (await anonymous.GetAsync($"/api/files/images/{second.LogoId}", Ct)).Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, await ImageCountAsync(factory));
        Assert.Equal(2, await ConsoleKit.AuditCountAsync(factory, "clinic.logo.updated", clinic.ClinicId));

        // Removing: 204, the profile has no logo, the image is gone; removing again is a no-op.
        (await owner.Client.DeleteAsync(logoUrl, Ct)).AssertStatus(HttpStatusCode.NoContent);
        Assert.Null((await owner.Client.GetOkAsync<ClinicProfileDto>($"/api/clinics/{clinic.ClinicId}")).LogoId);
        await AssertProblemAsync(await anonymous.GetAsync($"/api/files/images/{second.LogoId}", Ct), HttpStatusCode.NotFound, "common.not_found");
        (await owner.Client.DeleteAsync(logoUrl, Ct)).AssertStatus(HttpStatusCode.NoContent);
        Assert.Equal(0, await ImageCountAsync(factory));
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, "clinic.logo.removed", clinic.ClinicId));
    }

    [Fact]
    public async Task Clinic_logo_needs_clinic_wide_profile_permission()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);
        var url = $"/api/clinics/{a.ClinicId}/logo";

        await AssertProblemAsync(await PutFormAsync(receptionist.Client, url, Form(Png())), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PutFormAsync(branchAdmin.Client, url, Form(Png())), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await PutFormAsync(otherOwner.Client, url, Form(Png())), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await PutFormAsync(otherOwner.Client, $"/api/clinics/{Guid.NewGuid()}/logo", Form(Png())), HttpStatusCode.NotFound, "common.not_found");
        // Authorization is decided before the upload is validated.
        await AssertProblemAsync(await PutFormAsync(receptionist.Client, url, Form([1, 2, 3])), HttpStatusCode.Forbidden, "common.forbidden");
        Assert.Equal(0, await ImageCountAsync(factory));

        (await PutFormAsync(owner.Client, url, Form(Png()))).AssertStatus(HttpStatusCode.OK);
        await AssertProblemAsync(await receptionist.Client.DeleteAsync(url, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.DeleteAsync(url, Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.NotNull((await owner.Client.GetOkAsync<ClinicProfileDto>($"/api/clinics/{a.ClinicId}")).LogoId);

        // Multipart mutations need the CSRF header like every other non-GET request.
        var noHeader = factory.CreateApiClient(withClientHeader: false);
        await AssertProblemAsync(await PutFormAsync(noHeader, url, Form(Png())), HttpStatusCode.Forbidden, "security.client_header_missing");
    }

    [Fact]
    public async Task Uploads_are_validated_by_magic_bytes_and_size()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var user = await SignInAsPatientAsync(factory);
        const string url = "/api/me/avatar";

        await AssertFieldErrorAsync(await PutFormAsync(user.Client, url, Form("GIF89a-not-allowed"u8.ToArray(), declaredType: "image/gif")));
        await AssertFieldErrorAsync(await PutFormAsync(user.Client, url, Form("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), declaredType: "image/png")));
        await AssertFieldErrorAsync(await PutFormAsync(user.Client, url, Form([.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8], declaredType: "image/webp")));
        await AssertFieldErrorAsync(await PutFormAsync(user.Client, url, Form(Png(StoredImage.MaxBytes + 1))));
        await AssertFieldErrorAsync(await PutFormAsync(user.Client, url, Form([])));
        await AssertFieldErrorAsync(await PutFormAsync(user.Client, url, Form(Png(), field: "image")));
        await AssertFieldErrorAsync(await user.Client.PutAsync(url, new { file = "iVBORw0KGgo=" }));
        Assert.Equal(0, await ImageCountAsync(factory));

        // Exactly 2 MiB is accepted.
        (await PutFormAsync(user.Client, url, Form(Png(StoredImage.MaxBytes)))).AssertStatus(HttpStatusCode.OK);
        Assert.Equal(1, await ImageCountAsync(factory));
    }

    [Fact]
    public async Task Avatar_is_uploaded_shown_on_me_staff_and_doctor_and_removed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var clinic = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, clinic.ClinicId, StaffRole.ClinicOwner);
        var doctorUser = await SignInAsStaffAsync(factory, clinic.ClinicId, StaffRole.Doctor);
        await using (var db = factory.CreateDbContext())
        {
            db.Doctors.Add(Doctor.Create(clinic.ClinicId, "Dr. Avatar", true, doctorUser.Me.Id, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        Assert.Null((await doctorUser.Client.GetOkAsync<MeDto>("/api/me")).AvatarId);
        var first = await (await PutFormAsync(doctorUser.Client, "/api/me/avatar", Form(WebP(), declaredType: "application/octet-stream"))).ReadAsync<AvatarDto>();
        Assert.Equal(first.AvatarId, (await doctorUser.Client.GetOkAsync<MeDto>("/api/me")).AvatarId);
        Assert.Equal("image/webp", (await factory.CreateApiClient().GetAsync($"/api/files/images/{first.AvatarId}", Ct)).Content.Headers.ContentType?.MediaType);

        var staff = await owner.Client.GetOkAsync<List<StaffMemberDto>>($"/api/clinics/{clinic.ClinicId}/staff");
        Assert.Equal(first.AvatarId, staff.Single(s => s.UserId == doctorUser.Me.Id).AvatarId);
        var doctors = await owner.Client.GetOkAsync<List<DoctorDto>>($"/api/clinics/{clinic.ClinicId}/doctors");
        Assert.Equal(first.AvatarId, Assert.Single(doctors).AvatarId);

        // Replace deletes the previous image; another user's avatar is untouched.
        (await PutFormAsync(owner.Client, "/api/me/avatar", Form(Png()))).AssertStatus(HttpStatusCode.OK);
        var second = await (await PutFormAsync(doctorUser.Client, "/api/me/avatar", Form(Jpeg()))).ReadAsync<AvatarDto>();
        (await factory.CreateApiClient().GetAsync($"/api/files/images/{first.AvatarId}", Ct)).AssertStatus(HttpStatusCode.NotFound);
        Assert.Equal(2, await ImageCountAsync(factory));

        (await doctorUser.Client.DeleteAsync("/api/me/avatar", Ct)).AssertStatus(HttpStatusCode.NoContent);
        Assert.Null((await doctorUser.Client.GetOkAsync<MeDto>("/api/me")).AvatarId);
        (await factory.CreateApiClient().GetAsync($"/api/files/images/{second.AvatarId}", Ct)).AssertStatus(HttpStatusCode.NotFound);
        Assert.Equal(1, await ImageCountAsync(factory));
    }

    [Fact]
    public async Task Deleting_the_account_removes_the_avatar()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var patient = await SignInAsPatientAsync(factory);
        var avatar = await (await PutFormAsync(patient.Client, "/api/me/avatar", Form(Png()))).ReadAsync<AvatarDto>();

        (await patient.Client.PostAsync("/api/me/delete", null, Ct)).AssertStatus(HttpStatusCode.NoContent);
        (await factory.CreateApiClient().GetAsync($"/api/files/images/{avatar.AvatarId}", Ct)).AssertStatus(HttpStatusCode.NotFound);
        Assert.Equal(0, await ImageCountAsync(factory));
    }

    [Fact]
    public async Task Unknown_image_is_404_and_avatar_needs_sign_in()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var anonymous = factory.CreateApiClient();
        await AssertProblemAsync(await anonymous.GetAsync($"/api/files/images/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await PutFormAsync(anonymous, "/api/me/avatar", Form(Png())), HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await AssertProblemAsync(await PutFormAsync(anonymous, $"/api/clinics/{Guid.NewGuid()}/logo", Form(Png())), HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }
}
