namespace OqTish.Application.Common.Seeding;

/// <summary>
/// Adds fictional demo data. Contributors run in ascending <see cref="Order"/> and must be idempotent
/// (running the seed twice changes nothing). Register with
/// <c>services.AddScoped&lt;ISeedContributor, MySeed&gt;()</c>. Order bands: 100 identity, 200 tenancy/staff,
/// 300 catalog/scheduling, 400 booking, 500 finance, 600 notifications.
/// </summary>
public interface ISeedContributor
{
    int Order { get; }

    string Name { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>Outcome of one contributor run.</summary>
public sealed record SeedStepResult(string Name, int Order, TimeSpan Elapsed);
