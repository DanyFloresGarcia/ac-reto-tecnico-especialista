using Microsoft.Extensions.Logging;
using MimeKit;
using NotificationService.Application.Notifications;

namespace NotificationService.Infrastructure.Notifications;

/// <summary>
/// Builds a real MimeMessage but never sends it over SMTP — this MVP simulates the
/// notification by printing it to the console (Constitucion Assumption: "simular la
/// comunicación saliente satisface el requisito de notificar").
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(ILogger<MailKitEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEventCreatedNotificationAsync(
        Guid eventId, string eventName, DateTime occurredAt, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Plataforma de Eventos", "no-reply@plataforma-eventos.local"));
        message.To.Add(new MailboxAddress("Suscriptor de demo", "demo@plataforma-eventos.local"));
        message.Subject = $"Nuevo evento creado: {eventName}";
        message.Body = new TextPart("plain")
        {
            Text = $"Se creó el evento \"{eventName}\" (id {eventId}) el {occurredAt:u}.",
        };

        _logger.LogInformation(
            "Simulando envío de correo (no se envía por SMTP real). Asunto: {Subject} | Cuerpo: {Body}",
            message.Subject,
            message.Body);

        return Task.CompletedTask;
    }
}
