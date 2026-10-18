namespace OqTish.Infrastructure.Sms;

/// <summary>Configuration section <c>Sms</c>.</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary><c>Fake</c> (Development/tests only; refused in Production) or <c>Eskiz</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    public EskizOptions Eskiz { get; set; } = new();
}

/// <summary>Eskiz.uz gateway (<c>Sms:Eskiz:*</c>). Email/Password are secrets (env only).</summary>
public sealed class EskizOptions
{
    public Uri BaseUrl { get; set; } = new("https://notify.eskiz.uz");

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Sender id ("nickname"); 4546 is Eskiz's default test sender.</summary>
    public string From { get; set; } = "4546";

    public int TimeoutSeconds { get; set; } = 15;
}

public static class SmsProviders
{
    public const string Fake = "Fake";
    public const string Eskiz = "Eskiz";
}
