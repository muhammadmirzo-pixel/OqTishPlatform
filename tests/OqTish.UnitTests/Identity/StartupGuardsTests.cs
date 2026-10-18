using Microsoft.Extensions.Configuration;
using OqTish.Infrastructure;

namespace OqTish.UnitTests.Identity;

public class StartupGuardsTests
{
    private const string GoodPepper = "0123456789abcdef0123456789abcdef-x";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Fake_sms_is_refused_in_production()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.Validate(Config(("Security:OtpPepper", GoodPepper), ("Sms:Provider", "Fake")), new TestHostEnvironment("Production")));
        Assert.Contains("Fake", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Missing_or_weak_pepper_is_refused_outside_development(string environment)
    {
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Validate(Config(("Sms:Provider", "Fake")), new TestHostEnvironment(environment)));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Validate(Config(("Security:OtpPepper", "short"), ("Sms:Provider", "Fake")), new TestHostEnvironment(environment)));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Validate(
            Config(("Security:OtpPepper", StartupGuards.DevelopmentOtpPepper), ("Sms:Provider", "Fake")), new TestHostEnvironment(environment)));
    }

    [Fact]
    public void Development_defaults_to_fake_sms_and_dev_pepper()
    {
        var (pepper, provider) = StartupGuards.Validate(Config(), new TestHostEnvironment("Development"));
        Assert.Equal(StartupGuards.DevelopmentOtpPepper, pepper);
        Assert.Equal("Fake", provider);
    }

    [Fact]
    public void Eskiz_requires_credentials_and_unknown_provider_fails()
    {
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Validate(
            Config(("Security:OtpPepper", GoodPepper), ("Sms:Provider", "Eskiz")), new TestHostEnvironment("Production")));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.Validate(
            Config(("Security:OtpPepper", GoodPepper), ("Sms:Provider", "Carrier")), new TestHostEnvironment("Production")));
        var ok = StartupGuards.Validate(
            Config(("Security:OtpPepper", GoodPepper), ("Sms:Provider", "Eskiz"), ("Sms:Eskiz:Email", "a@b.c"), ("Sms:Eskiz:Password", "p")),
            new TestHostEnvironment("Production"));
        Assert.Equal("Eskiz", ok.SmsProvider);
    }
}
