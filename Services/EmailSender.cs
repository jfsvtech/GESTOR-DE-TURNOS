using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace GeneradorTurnos.Services;

public interface IEmailSender
{
    bool Enabled { get; }
    Task SendAsync(string to, string subject, string htmlBody);
}

public class EmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;
    private readonly HttpClient _http;

    public EmailSender(IConfiguration config, ILogger<EmailSender> logger, HttpClient http)
    {
        _config = config;
        _logger = logger;
        _http = http;
    }

    private string Provider => _config["Email:Provider"] ?? "Smtp";

    public bool Enabled =>
        _config.GetValue("Email:Enabled", false) &&
        (Provider.Equals("GmailApi", StringComparison.OrdinalIgnoreCase)
            ? GmailConfigured()
            : !string.IsNullOrWhiteSpace(_config["Email:Host"]));

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (!Enabled)
        {
            _logger.LogInformation("[EMAIL SIMULADO] Para: {To} | Asunto: {Subject}. Cuerpo omitido para no exponer tokens.", to, subject);
            return;
        }

        if (Provider.Equals("GmailApi", StringComparison.OrdinalIgnoreCase))
        {
            await SendWithGmailApiAsync(to, subject, htmlBody);
            return;
        }

        await SendWithSmtpAsync(to, subject, htmlBody);
    }

    private bool GmailConfigured()
        => !string.IsNullOrWhiteSpace(_config["GmailApi:ClientId"])
           && !string.IsNullOrWhiteSpace(_config["GmailApi:ClientSecret"])
           && !string.IsNullOrWhiteSpace(_config["GmailApi:RefreshToken"])
           && !string.IsNullOrWhiteSpace(_config["GmailApi:From"]);

    private async Task SendWithSmtpAsync(string to, string subject, string htmlBody)
    {
        var from = _config["Email:From"] ?? _config["Email:User"]!;
        using var msg = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(_config["Email:Host"], _config.GetValue("Email:Port", 587))
        {
            EnableSsl = _config.GetValue("Email:UseSsl", true),
            Credentials = new NetworkCredential(_config["Email:User"], _config["Email:Password"])
        };
        await client.SendMailAsync(msg);
        _logger.LogInformation("Correo enviado a {To} via SMTP.", to);
    }

    private async Task SendWithGmailApiAsync(string to, string subject, string htmlBody)
    {
        var accessToken = await GetGmailAccessTokenAsync();
        var from = _config["GmailApi:From"]!;
        var fromName = _config["GmailApi:FromName"] ?? "Turnos";
        var mime = BuildMime(from, fromName, to, subject, htmlBody);
        var raw = Base64UrlEncode(Encoding.UTF8.GetBytes(mime));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { raw }), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gmail API no pudo enviar correo a {To}. Status {Status}. Body omitido: {Length} chars.",
                to, response.StatusCode, body.Length);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Correo enviado a {To} via Gmail API.", to);
    }

    private async Task<string> GetGmailAccessTokenAsync()
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _config["GmailApi:ClientId"]!,
            ["client_secret"] = _config["GmailApi:ClientSecret"]!,
            ["refresh_token"] = _config["GmailApi:RefreshToken"]!,
            ["grant_type"] = "refresh_token"
        };

        using var response = await _http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Gmail API no devolvio access_token.");
    }

    private static string BuildMime(string from, string fromName, string to, string subject, string htmlBody)
    {
        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(subject));
        return string.Join("\r\n", new[]
        {
            $"From: {EncodeHeader(fromName)} <{from}>",
            $"To: <{to}>",
            $"Subject: =?UTF-8?B?{encodedSubject}?=",
            "MIME-Version: 1.0",
            "Content-Type: text/html; charset=UTF-8",
            "",
            htmlBody
        });
    }

    private static string EncodeHeader(string value)
        => $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
