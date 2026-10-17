using System.Reflection;
using OqTish.Application;
using OqTish.Domain;
using OqTish.Infrastructure;

namespace OqTish.ArchitectureTests;

public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(DomainAssembly).Assembly;
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;

    [Fact]
    public void Domain_depends_on_no_other_layer_or_framework()
    {
        AssertNoReferences(Domain,
            "OqTish.Application", "OqTish.Infrastructure", "OqTish.Api", "OqTish.Worker",
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_hosts()
    {
        AssertNoReferences(Application,
            "OqTish.Infrastructure", "OqTish.Api", "OqTish.Worker",
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        AssertNoReferences(Infrastructure, "OqTish.Api", "OqTish.Worker");
    }

    [Fact]
    public void Domain_and_application_never_use_binary_floating_point()
    {
        // Money must be decimal (docs/FINANCIAL_RULES.md §2). No float/double anywhere in these layers.
        var forbidden = new[] { typeof(float), typeof(double), typeof(float?), typeof(double?) };
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var offenders = new[] { Domain, Application }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetProperties(all).Select(p => (t, p.Name, p.PropertyType))
                .Concat(t.GetFields(all).Select(f => (t, f.Name, f.FieldType))))
            .Where(m => forbidden.Contains(m.Item3))
            .Select(m => $"{m.t.FullName}.{m.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static void AssertNoReferences(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var offending = assembly.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offending.Count == 0,
            $"{assembly.GetName().Name} must not reference: {string.Join(", ", offending)}");
    }
}
