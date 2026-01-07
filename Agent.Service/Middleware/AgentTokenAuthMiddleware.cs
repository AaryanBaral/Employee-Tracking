using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Agent.Service.Middleware;

public sealed class AgentTokenAuthMiddleware
{
    public const string HeaderName = "X-Agent-Token";

    private readonly RequestDelegate _next;
    private readonly string _expectedToken;
    private readonly ILogger<AgentTokenAuthMiddleware> _logger;

    public AgentTokenAuthMiddleware(
        RequestDelegate next,
        string expectedToken,
        ILogger<AgentTokenAuthMiddleware> logger)
    {
        _next = next;
        _expectedToken = expectedToken;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided))
        {
            _logger.LogWarning("Local API auth failed: missing {header}", HeaderName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!string.Equals(provided.ToString(), _expectedToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Local API auth failed: token mismatch");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}
