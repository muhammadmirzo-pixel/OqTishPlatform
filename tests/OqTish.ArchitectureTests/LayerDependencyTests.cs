using System.Reflection;
using OqTish.Application;
using OqTish.Domain;
using OqTish.Domain.Common;
using OqTish.Infrastructure;

namespace OqTish.ArchitectureTests;

public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(DomainAssembly).Assembly;
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;
    private static readonly string[] AllowedEfAssemblies = ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Abstractions"];
    private static readonly Assembly Infrastructure = typeof(OqTish.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var offending = Domain.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(n => !(n == "netstandard" || n == "mscorlib" || n.StartsWith("System", StringComparison.Ordinal)))
            .ToList();
        Assert.True(offending.Count == 0, $"Domain must reference nothing but the BCL: {string.Join(", ", offending)}");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_npgsql_or_aspnetcore()
    {
        AssertNoReferences(Application,
            "OqTish.Infrastructure", "OqTish.Api", "OqTish.Worker",
            "Npgsql", "Microsoft.AspNetCore", "Microsoft.Extensions.Identity",
            "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Design");
    }

    [Fact]
    public void Application_uses_only_core_ef_assemblies()
    {
        var ef = Application.GetReferencedAssemblies()
            .Select(r => r.Name ?? string.Empty)
            .Where(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToList();
        Assert.All(ef, n => Assert.Contains(n, AllowedEfAssemblies));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        AssertNoReferences(Infrastructure, "OqTish.Api", "OqTish.Worker");
    }

    [Fact]
    public void Domain_and_application_never_use_binary_floating_point()
    {
        // Money must be decimal (docs/FINANCIAL_RULES.md §2). No float/double in members or signatures.
        var forbidden = new[] { typeof(float), typeof(double), typeof(float?), typeof(double?) };
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var offenders = new[] { Domain, Application }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetProperties(all).Select(p => (t, p.Name, p.PropertyType))
                .Concat(t.GetFields(all).Select(f => (t, f.Name, f.FieldType)))
                .Concat(t.GetMethods(all).Select(m => (t, m.Name, m.ReturnType)))
                .Concat(t.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => (t, m.Name + "(" + p.Name + ")", p.ParameterType)))))
            .Where(m => forbidden.Contains(m.Item3))
            .Select(m => $"{m.t.FullName}.{m.Item2}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Money_is_backed_by_decimal_only()
    {
        var fields = typeof(Money).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.Contains(fields, f => f.FieldType == typeof(decimal));
        Assert.All(fields, f => Assert.True(f.FieldType == typeof(decimal) || f.FieldType == typeof(Currency), f.Name));
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
