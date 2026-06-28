using System.Net;
using System.Net.Mail;

namespace GeneradorTurnos.Services;

public interface IEmailSender
{
    /// <summary>True si hay un SMTP configurado y se envían correos reales.</summary>
    bool Enabled { get; }
    Task SendAsync(string to, string subject, string htmlBody);
}

/// <summary>
/// Envía correos por SMTP si está configurado en appsettings ("Email").
/// Si no, solo registra el contenido en el log (útil en desarrollo: el enlace
/// de verificación también se muestra en pantalla).
/// </summary>
public class EmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool Enabled =>
        _config.GetValue("Email:Enabled", false) &&
        !string.IsNullOrWhiteSpace(_config["Email:Host"]);

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (!Enabled)
        {
            _logger.LogInformation("[EMAIL SIMULADO] Para: {To} | Asunto: {Subject}\n{Body}", to, subject, htmlBody);
            return;
        }

        var from = _config["Email:From"] ?? _config["Email:User"]!;
        using var msg = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(_config["Email:Host"], _config.GetValue("Email:Port", 587))
        {
            EnableSsl = _config.GetValue("Email:UseSsl", true),
            Credentials = new NetworkCredential(_config["Email:User"], _config["Email:Password"])
        };
        await client.SendMailAsync(msg);
        _logger.LogInformation("Correo enviado a {To}.", to);
    }
}
