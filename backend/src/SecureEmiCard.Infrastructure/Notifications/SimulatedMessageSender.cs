using Microsoft.Extensions.Logging;
using SecureEmiCard.Application.Abstractions.Messaging;
using SecureEmiCard.Application.Features.Otp;

namespace SecureEmiCard.Infrastructure.Notifications;

/// <summary>
/// DEVELOPMENT ONLY: "sends" SMS and e-mail into <see cref="DevMessageOutbox"/>. Program.cs refuses to
/// start outside Development / Testing with this sender, because one-time codes would never reach anyone.
/// A real implementation calls an SMS gateway and an e-mail service with the same two methods.
/// </summary>
public class SimulatedMessageSender : IMessageSender
{
    private readonly DevMessageOutbox _outbox;
    private readonly ILogger<SimulatedMessageSender> _logger;

    public SimulatedMessageSender(DevMessageOutbox outbox, ILogger<SimulatedMessageSender> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public Task SendSmsAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        _outbox.Add("Sms", phoneNumber, null, text);
        // Never log the text: it may contain a one-time code.
        _logger.LogInformation("Simulated SMS to {Phone} ({Length} characters)", StepUpAuthenticator.MaskPhone(phoneNumber), text.Length);
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string email, string subject, string body, CancellationToken ct = default)
    {
        _outbox.Add("Email", email, subject, body);
        _logger.LogInformation("Simulated e-mail \"{Subject}\"", subject);
        return Task.CompletedTask;
    }
}
