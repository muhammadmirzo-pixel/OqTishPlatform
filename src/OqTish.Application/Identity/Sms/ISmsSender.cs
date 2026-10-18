namespace OqTish.Application.Identity.Sms;

/// <summary>
/// Provider-neutral SMS port (OD-09). Implementations: <c>Fake</c> (Development/tests; in-memory
/// mailbox) and <c>Eskiz</c> (Eskiz.uz). Implementations must never log the phone or the text.
/// Throws <see cref="SmsDeliveryException"/> (or any exception) when the provider rejects the message.
/// </summary>
public interface ISmsSender
{
    /// <param name="e164Phone">+998XXXXXXXXX</param>
    Task SendAsync(string e164Phone, string text, CancellationToken cancellationToken);
}

public sealed class SmsDeliveryException : Exception
{
    public SmsDeliveryException()
    {
    }

    public SmsDeliveryException(string message)
        : base(message)
    {
    }

    public SmsDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
