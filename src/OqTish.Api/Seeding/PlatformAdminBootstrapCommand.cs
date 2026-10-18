using OqTish.Application.Admin;

namespace OqTish.Api.Seeding;

/// <summary>
/// <c>dotnet OqTish.Api.dll create-platform-admin --email &lt;e-mail&gt; --phone &lt;+998…&gt;</c> — the only way
/// to create the first production platform admin; works in every environment. The password is read from
/// the environment variable <see cref="PasswordVariable"/> or, if unset, from standard input (first line;
/// typed without echo on an interactive terminal). It is never accepted as an argument. Exit codes: 0 done
/// (idempotent), 1 invalid input/weak password, 3 conflict (identifier owned by another account, suspended).
/// </summary>
public static partial class PlatformAdminBootstrapCommand
{
    public const string Verb = "create-platform-admin";
    public const string PasswordVariable = "OQTISH_ADMIN_PASSWORD";

    public static bool IsRequested(string[] args) =>
        args is { Length: > 0 } && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, Func<string?> readPassword, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(readPassword);
        var email = Option(args, "--email");
        var phone = Option(args, "--phone");
        if (args.Any(a => a.StartsWith("--password", StringComparison.OrdinalIgnoreCase)))
        {
            LogUsage(logger, $"Passwords are never accepted as arguments; set {PasswordVariable} or pipe it on stdin.");
            return 1;
        }

        if (email is null || phone is null)
        {
            LogUsage(logger, $"Usage: {Verb} --email <e-mail> --phone <+998XXXXXXXXX> (password from {PasswordVariable} or stdin)");
            return 1;
        }

        var password = readPassword();
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<CreatePlatformAdminHandler>()
            .HandleAsync(new CreatePlatformAdminCommand(email, phone, password), ct);
        if (!result.IsSuccess)
        {
            var fields = result.Error.FieldErrors is { } f ? string.Join("; ", f.Select(e => $"{e.Key}: {string.Join(' ', e.Value)}")) : result.Error.Message;
            LogFailed(logger, result.Error.Code, fields);
            return result.Error.Kind == Application.Common.ErrorKind.Validation ? 1 : 3;
        }

        var o = result.Value;
        LogDone(logger, o.UserId, o.Created, o.PhoneLinked, o.GrantedPermissions.Count);
        return 0;
    }

    /// <summary>Password source for the real process: env var, else stdin (no echo on a terminal).</summary>
    public static string? ReadPasswordFromEnvironmentOrStdin()
    {
        var fromEnv = Environment.GetEnvironmentVariable(PasswordVariable);
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return fromEnv;
        }

        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine();
        }

        Console.Error.Write("Password: ");
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return buffer.ToString();
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        var inline = args.Skip(1).FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        return inline?[(name.Length + 1)..];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "create-platform-admin: {Message}")]
    private static partial void LogUsage(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "create-platform-admin failed ({Code}): {Detail}")]
    private static partial void LogFailed(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "create-platform-admin done: user {UserId}, created {Created}, phone linked {PhoneLinked}, {Granted} permission(s) granted. Enable TOTP before the permissions take effect")]
    private static partial void LogDone(ILogger logger, Guid userId, bool created, bool phoneLinked, int granted);
}
