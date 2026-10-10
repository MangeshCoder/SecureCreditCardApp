namespace SecureEmiCard.Application.Abstractions.Messaging;

/// <summary>
/// Sends SMS and e-mail (Module 7). In development a simulator keeps the messages in memory (phone
/// simulator page); production plugs in a real provider (an SMS gateway, SMTP / an e-mail service).
/// </summary>
public interface IMessageSender
{
    Task SendSmsAsync(string phoneNumber, string text, CancellationToken ct = default);
    Task SendEmailAsync(string email, string subject, string body, CancellationToken ct = default);
}
