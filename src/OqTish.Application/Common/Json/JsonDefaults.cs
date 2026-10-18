using System.Text.Json;
using System.Text.Json.Serialization;

namespace OqTish.Application.Common.Json;

/// <summary>Serializer settings shared by the API, outbox payloads, audit details and idempotency snapshots.</summary>
public static class JsonDefaults
{
    /// <summary>camelCase, string enums, <see cref="MoneyJsonConverter"/>.</summary>
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applies the shared settings to an existing options instance (e.g. ASP.NET Core's).</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new MoneyJsonConverter());
        return options;
    }
}
