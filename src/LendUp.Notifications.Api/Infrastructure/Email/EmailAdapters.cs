using LendUp.Notifications.Api.Application;
using LendUp.Notifications.Api.Infrastructure.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace LendUp.Notifications.Api.Infrastructure.Email;

/// <summary>EmailAdapter (Fig. 20) sobre el SDK oficial de SendGrid.</summary>
public sealed class SendGridEmailAdapter(ISendGridClient client, SendGridSettings settings) : IEmailSender
{
    public bool RequiresAddress => true;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.ToAddress)) throw new DependencyUnavailableException("Destinatario sin correo");
        var mail = MailHelper.CreateSingleEmail(new EmailAddress(settings.FromEmail, settings.FromName), new EmailAddress(message.ToAddress),
            message.Subject, $"{message.PlainText}\n\n— LendUp", null);
        var response = await client.SendEmailAsync(mail, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync(ct);
            throw new DependencyUnavailableException($"SendGrid respondió {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }
    }
}

/// <summary>Desarrollo local sin SENDGRID_API_KEY: el correo se registra en el log en vez de enviarse.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public bool RequiresAddress => false;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogInformation("Correo (solo log, sin SENDGRID_API_KEY) para usuario {UserId} <{Address}>: {Subject} - {Body}",
            message.RecipientUserId, message.ToAddress ?? "correo no resuelto", message.Subject, message.PlainText);
        return Task.CompletedTask;
    }
}
