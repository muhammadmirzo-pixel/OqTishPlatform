using System.Net;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Api;

public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ready_is_healthy_when_database_is_migrated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(await postgres.CreateMigratedDatabaseAsync(ct));
        using var client = factory.CreateClient();

        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), ct);
        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Ready_is_unhealthy_while_migrations_are_pending()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(await postgres.CreateEmptyDatabaseAsync(ct));
        using var client = factory.CreateClient();

        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_without_internals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(await postgres.CreateMigratedDatabaseAsync(ct), environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task Api_documentation_is_not_exposed_in_production(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = CreateFactory(await postgres.CreateMigratedDatabaseAsync(ct), environment: "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static ApiFactory CreateFactory(string connectionString, string environment = "Development") =>
        new(connectionString, environment);
}
