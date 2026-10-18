using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OqTish.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Stores an enum as text and adds a CHECK constraint listing its names.</summary>
    public static PropertyBuilder<TEnum> AsEnumText<TEntity, TEnum>(
        this PropertyBuilder<TEnum> property,
        EntityTypeBuilder<TEntity> entity,
        string table,
        string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var values = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        entity.ToTable(t => t.HasCheckConstraint($"ck_{table}_{column}", $"{column} IN ({values})"));
        return property.HasConversion<string>().HasMaxLength(32);
    }

    public static PropertyBuilder<TEnum?> AsEnumText<TEntity, TEnum>(
        this PropertyBuilder<TEnum?> property,
        EntityTypeBuilder<TEntity> entity,
        string table,
        string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var values = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        entity.ToTable(t => t.HasCheckConstraint($"ck_{table}_{column}", $"{column} IS NULL OR {column} IN ({values})"));
        return property.HasConversion<string>().HasMaxLength(32);
    }
}
