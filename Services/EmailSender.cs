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