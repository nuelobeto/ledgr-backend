namespace Api.Middleware;

public static class SecurityHeadersExtensions
{
  public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
  {
    return app.Use(async (context, next) =>
    {
      context.Response.OnStarting(() =>
          {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            headers["X-XSS-Protection"] = "0";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
            headers["Cache-Control"] = "no-store";
            return Task.CompletedTask;
          });

      await next();
    });
  }
}