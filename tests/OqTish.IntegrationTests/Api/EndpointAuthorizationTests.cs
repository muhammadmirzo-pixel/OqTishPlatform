using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Api;

/// <summary>
/// S17: every endpoint declares AllowAnonymous or an authorization requirement, and every endpoint that
/// requires authorization rejects an anonymous caller with 401 (generic negative authorization test that
/// automatically covers new endpoints).
/// </summary>
public class EndpointAuthorizationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IReadOnlyList<string> EndpointsWithoutAuthMetadata(IEnumerable<Endpoint> endpoints) =>
        [.. endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any()
                && e.Metadata.GetMetadata<AuthorizationPolicy>() is null)
            .Select(Describe)];

    [Fact]
    public async Task Every_endpoint_declares_allow_anonymous_or_authorization()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        Assert.NotEmpty(endpoints);
        Assert.Empty(EndpointsWithoutAuthMetadata(endpoints));
    }

    [Fact]
    public void Checker_flags_an_endpoint_without_metadata()
    {
        var bare = new RouteEndpoint(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/api/new-thing"), 0, EndpointMetadataCollection.Empty, "new");
        var anon = new RouteEndpoint(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/api/open"), 0, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "open");
        Assert.Equal(["/api/new-thing"], EndpointsWithoutAuthMetadata([bare, anon]).Select(s => s.Split(' ')[1]));
    }

    [Fact]
    public async Task Every_protected_endpoint_rejects_anonymous_callers()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();
        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { HttpMethods.Count: > 0 } mm ? mm.HttpMethods[0] : "GET";
            var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(s => string.Concat(s.Parts.Select(p => p switch
            {
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart l => l.Content,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart => Guid.NewGuid().ToString(),
                _ => string.Empty,
            }))));
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request, Ct);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} answered {(int)response.StatusCode} to an anonymous caller");
            await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
        }
    }

    [Fact]
    public async Task Garbage_session_cookie_is_401_and_cleared()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Cookie", "__Host-oqtish=forged-token");
        var response = await client.SendAsync(request, Ct);
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    private static string Describe(RouteEndpoint e) =>
        $"{(e.Metadata.GetMetadata<HttpMethodMetadata>() is { HttpMethods.Count: > 0 } mm ? mm.HttpMethods[0] : "*")} {e.RoutePattern.RawText}";
}
