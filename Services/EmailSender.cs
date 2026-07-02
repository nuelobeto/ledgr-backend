using System.Net.Http.Json;

namespace Api.Services;

public interface IEmailSender
{
  Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
  public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
  {
    logger.LogInformation("EMAIL → {To} | {Subject}\n{Body}", to, subject, htmlBody);
    return Task.CompletedTask;
  }
}

// https://resend.com/docs/api-reference/emails/send-email
// Registered instead of LoggingEmailSender only when RESEND_API_KEY is set (see Program.cs) —
// no key configured falls back to logging rather than crashing on missing config.
public class ResendEmailSender(HttpClient http, IConfiguration config, ILogger<ResendEmailSender> logger)
    : IEmailSender
{
  public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
  {
    var from = config["RESEND_FROM"]
        ?? throw new InvalidOperationException("RESEND_FROM is not configured.");

    var response = await http.PostAsJsonAsync("emails", new
    {
      from,
      to = new[] { to },
      subject,
      html = htmlBody,
    }, ct);

    if (!response.IsSuccessStatusCode)
    {
      var body = await response.Content.ReadAsStringAsync(ct);
      // Auth flows (register, forgot-password) already respond with the same generic
      // "if that email exists..." message regardless of what happens here, so a failed send
      // shouldn't surface as a 500 to the caller — but it absolutely needs to be visible in
      // logs, or confirmation emails silently vanish with no signal to debug from.
      logger.LogError(
          "Resend send failed ({Status}) for {To}: {Body}", response.StatusCode, to, body);
    }
  }
}
