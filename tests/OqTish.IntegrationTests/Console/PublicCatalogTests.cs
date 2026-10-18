using System.Net;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Public;
using OqTish.Application.Tenancy;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Console;

/// <summary>Public search, detail and facets over the demo seed (3 approved Tashkent clinics + 1 pending).</summary>
public class PublicCatalogTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Tabassum = DemoData.Clinics[0].Name;
    private static readonly string Marvarid = DemoData.Clinics[1].Name;
    private static readonly string Soglom = DemoData.Clinics[2].Name;

    private static async Task<(ApiFactory Factory, HttpClient Anonymous)> SeededAsync(PostgresFixture postgres)
    {
        var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        (await client.PostAsync("/api/dev/seed", null, Ct)).AssertStatus(HttpStatusCode.OK);
        return (factory, client);
    }

    private static async Task<List<string>> NamesAsync(HttpClient client, string query) =>
        [.. (await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics" + query)).Items.Select(c => c.Name)];

    [Fact]
    public async Task Search_lists_only_approved_clinics_and_supports_filters()
    {
        var (factory, client) = await SeededAsync(postgres);
        await using var _ = factory;

        var all = await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics");
        Assert.Equal(3, all.TotalCount);
        Assert.DoesNotContain(all.Items, c => c.Name == DemoData.Clinics[3].Name);
        var tabassum = all.Items.Single(c => c.Name == Tabassum);
        // BE2 demo seed: one published 5-star review for the first clinic.
        Assert.Equal((2, 5.00m as decimal?, 1), (tabassum.BranchCount, tabassum.Rating, tabassum.ReviewCount));
        Assert.Equal(["Chilonzor"], tabassum.Districts);
        Assert.Equal("50000.00", tabassum.MinPrice!.Value.ToAmountString());
        Assert.NotNull(tabassum.NextFreeSlot); // demo doctors work Mon–Fri, so a slot exists within 7 days
        Assert.Null(tabassum.DistanceKm);
        Assert.Equal(3, (await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?availableWithinDays=7")).TotalCount);

        Assert.Equal([Marvarid], await NamesAsync(client, "?q=marvarid"));
        Assert.Equal([Marvarid], await NamesAsync(client, "?q=BREKET"));
        Assert.Equal([Marvarid], await NamesAsync(client, "?serviceCategory=orthodontics"));
        Assert.Equal([Marvarid, Tabassum], (await NamesAsync(client, "?serviceCategory=whitening")).Order(StringComparer.Ordinal));
        Assert.Equal([Marvarid], await NamesAsync(client, "?district=yunusobod"));
        Assert.Empty(await NamesAsync(client, "?district=Sergeli"));
        Assert.Equal([Marvarid, Soglom, Tabassum], await NamesAsync(client, "?sort=name"));
        Assert.Equal([Soglom, Tabassum, Marvarid], await NamesAsync(client, "?sort=price"));

        var implants = await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?serviceCategory=implants&sort=price");
        Assert.Equal(["4050000.00", "4500000.00", "5400000.00"], implants.Items.Select(c => c.MinPrice!.Value.ToAmountString()));

        await using var db = factory.CreateDbContext();
        var serviceId = await db.Services.Where(s => s.Name == "Demo Tish oqartirish").Join(db.Clinics.Where(c => c.Name == Tabassum), s => s.ClinicId, c => c.Id, (s, _) => s.Id).SingleAsync(Ct);
        Assert.Equal([Tabassum], await NamesAsync(client, $"?serviceId={serviceId}"));
    }

    [Fact]
    public async Task Distance_sort_and_pagination()
    {
        var (factory, client) = await SeededAsync(postgres);
        await using var _ = factory;

        // At the Yunusobod branch.
        var near = await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?lat=41.364&lng=69.287");
        Assert.Equal([Marvarid, Soglom, Tabassum], near.Items.Select(c => c.Name));
        Assert.Equal(0m, near.Items[0].DistanceKm);
        Assert.InRange(near.Items[2].DistanceKm!.Value, 9m, 13m); // Qatortol branch ≈ 10.5 km, Chilonzor ≈ 12 km
        Assert.True(near.Items[1].DistanceKm < near.Items[2].DistanceKm);

        var page1 = await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?sort=name&pageSize=2");
        var page2 = await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?sort=name&pageSize=2&page=2");
        Assert.Equal((2, 3, 1), (page1.Items.Count, page1.TotalCount, page2.Items.Count));
        Assert.Equal(Tabassum, page2.Items[0].Name);

        (await client.GetAsync("/api/public/clinics?availableWithinDays=7", Ct)).AssertStatus(HttpStatusCode.OK);
        await AssertProblemAsync(await client.GetAsync("/api/public/clinics?sort=distance", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await client.GetAsync("/api/public/clinics?lat=41.3", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await client.GetAsync("/api/public/clinics?lat=91&lng=69", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await client.GetAsync("/api/public/clinics?pageSize=101", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await client.GetAsync("/api/public/clinics?sort=cheap", Ct), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await client.GetAsync("/api/public/clinics?serviceCategory=magic", Ct), HttpStatusCode.BadRequest, "common.validation");
    }

    [Fact]
    public async Task Detail_shows_active_branches_and_offers_and_hides_unapproved_or_suspended_clinics()
    {
        var (factory, client) = await SeededAsync(postgres);
        await using var _ = factory;
        await using var db = factory.CreateDbContext();
        var tabassum = await db.Clinics.SingleAsync(c => c.Name == Tabassum, Ct);
        var pending = await db.Clinics.SingleAsync(c => c.Name == DemoData.Clinics[3].Name, Ct);

        var detail = await client.GetOkAsync<ClinicDetailDto>($"/api/public/clinics/{tabassum.Id}");
        Assert.Equal(2, detail.Branches.Count);
        Assert.Equal(6, detail.Branches[0].Hours.Count);
        Assert.Equal(6, detail.Branches[0].Services.Count);
        await AssertProblemAsync(await client.GetAsync($"/api/public/clinics/{pending.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await client.GetAsync($"/api/public/clinics/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, "common.not_found");

        // Inactive branch and inactive offer disappear.
        var branchId = detail.Branches.Single(b => b.Name == "Demo Qatortol filiali").Id;
        await db.Database.ExecuteSqlAsync($"UPDATE tenancy.branches SET is_active = false WHERE id = {branchId}", Ct);
        var offerService = detail.Branches[0].Services[0].ServiceId;
        await db.Database.ExecuteSqlAsync($"UPDATE catalog.branch_services SET is_active = false WHERE service_id = {offerService}", Ct);
        var after = await client.GetOkAsync<ClinicDetailDto>($"/api/public/clinics/{tabassum.Id}");
        Assert.Single(after.Branches);
        Assert.Equal(5, after.Branches[0].Services.Count);
        Assert.Equal(1, (await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics?q=tabassum")).Items[0].BranchCount);

        // Suspension hides the clinic and disables its staff.
        var owner = await SignInAsStaffAsync(factory, tabassum.Id, StaffRole.ClinicOwner);
        (await owner.Client.GetAsync($"/api/clinics/{tabassum.Id}", Ct)).AssertStatus(HttpStatusCode.OK);
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(factory);
        (await admin.Client.PostAsync($"/api/admin/clinics/{tabassum.Id}/suspend", new { reason = "Tekshiruv" })).AssertStatus(HttpStatusCode.OK);
        await AssertProblemAsync(await client.GetAsync($"/api/public/clinics/{tabassum.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(2, (await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics")).TotalCount);
        await AssertProblemAsync(await owner.Client.GetAsync($"/api/clinics/{tabassum.Id}", Ct), HttpStatusCode.NotFound, "common.not_found");
        (await admin.Client.PostAsync($"/api/admin/clinics/{tabassum.Id}/reinstate", (object?)null)).AssertStatus(HttpStatusCode.OK);
        (await owner.Client.GetAsync($"/api/clinics/{tabassum.Id}", Ct)).AssertStatus(HttpStatusCode.OK);
        Assert.Equal(3, (await client.GetOkAsync<Page<ClinicCardDto>>("/api/public/clinics")).TotalCount);
    }

    [Fact]
    public async Task Facets_list_public_districts_categories_and_services()
    {
        var (factory, client) = await SeededAsync(postgres);
        await using var _ = factory;
        var facets = await client.GetOkAsync<FacetsDto>("/api/public/facets");
        Assert.Equal(["Chilonzor", "Mirzo Ulug'bek", "Yunusobod"], facets.Districts);
        Assert.Contains(facets.Categories, c => c.Key == "orthodontics");
        Assert.Equal(6 + 7 + 5, facets.Services.Count);
        Assert.DoesNotContain(facets.Districts, d => d == "Sergeli");
    }
}
