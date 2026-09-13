using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using NotificationService.Application.Notifications;

namespace NotificationService.Infrastructure.Notifications;

/// <summary>
/// Sends a real email over SMTP when `Smtp:Host` is configured; otherwise falls back to
/// simulating the notification by printing it to the console. The fallback keeps
/// `docker compose up` working with zero email configuration (Constitucion Assumption:
/// "simular la comunicación saliente satisface el requisito de notificar" as the default),
/// while `Smtp:*` env vars let anyone opt into a real SMTP relay (e.g. Gmail + App Password)
/// for a genuine end-to-end validation without touching code.
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IConfiguration configuration, ILogger<MailKitEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEventCreatedNotificationAsync(
        Guid eventId, string eventName, DateTime occurredAt, CancellationToken cancellationToken)
    {
        var fromAddress = _configuration["Smtp:FromAddress"] ?? "no-reply@plataforma-eventos.local";
        var fromName = _configuration["Smtp:FromName"] ?? "Plataforma de Eventos";
        var toAddress = _configuration["Smtp:ToAddress"] ?? "demo@plataforma-eventos.local";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(new MailboxAddress("Suscriptor de demo", toAddress));
        message.Subject = $"Nuevo evento creado: {eventName}";
        message.Body = new TextPart("plain")
        {
            Text = $"Se creó el evento \"{eventName}\" (id {eventId}) el {occurredAt:u}.",
        };

        var host = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation(
                "Simulando envío de correo (Smtp:Host no configurado — sin SMTP real). Asunto: {Subject} | Cuerpo: {Body}",
                message.Subject,
                message.Body);
            return;
        }

        // Left unhandled on purpose: a real SMTP failure here propagates to the consumer,
        // triggering MassTransit's retry/DLQ path (research.md §2) — the same resilience
        // mechanism the challenge asks to demonstrate, now exercised by a real failure mode
        // instead of only an artificially forced one.
        var port = int.TryParse(_configuration["Smtp:Port"], out var parsedPort) ? parsedPort : 587;
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, cancellationToken);

        if (!string.IsNullOrWhiteSpace(username))
        {
            await client.AuthenticateAsync(username, password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Correo enviado vía SMTP real a {ToAddress}. Asunto: {Subject}", toAddress, message.Subject);
    }
}
