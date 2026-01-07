using System.Diagnostics;
using System.Text;

namespace Tracker.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private const int MaxBodyBytes = 8 * 1024;
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var request = context.Request;

        string? body = null;
        if (HttpMethods.IsPost(request.Method) && request.Body.CanRead)
        {
            request.EnableBuffering();
            body = await ReadBodyAsync(request);
            request.Body.Position = 0;
        }

        _logger.LogInformation(
            "Request {method} {path}{query} ContentType={contentType} ContentLength={contentLength} Body={body}",
            request.Method,
            request.Path,
            request.QueryString.Value ?? string.Empty,
            request.ContentType ?? "(none)",
            request.ContentLength?.ToString() ?? "(none)",
            body ?? "(not logged)");

        var originalBody = context.Response.Body;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        sw.Stop();

        context.Response.Body.Position = 0;
        var responseText = await ReadResponseBodyAsync(context.Response);
        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            _logger.LogWarning(
                "Request failed {method} {path} Status={status} ErrorBody={body}",
                request.Method,
                request.Path,
                context.Response.StatusCode,
                responseText);
        }
        else
        {
            _logger.LogInformation(
                "Request succeeded {method} {path} Status={status} ResponseBody={body}",
                request.Method,
                request.Path,
                context.Response.StatusCode,
                responseText);
        }
        context.Response.Body.Position = 0;

        await responseBody.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        _logger.LogInformation(
            "Response {method} {path} Status={status} DurationMs={durationMs}",
            request.Method,
            request.Path,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(
            request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: MaxBodyBytes,
            leaveOpen: true);

        var buffer = new char[MaxBodyBytes];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        var content = new string(buffer, 0, read);

        if (request.ContentLength.HasValue && request.ContentLength.Value > MaxBodyBytes)
        {
            content += "...(truncated)";
        }

        return content;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponse response)
    {
        using var reader = new StreamReader(
            response.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: MaxBodyBytes,
            leaveOpen: true);

        var buffer = new char[MaxBodyBytes];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        var content = new string(buffer, 0, read);

        if (response.ContentLength.HasValue && response.ContentLength.Value > MaxBodyBytes)
        {
            content += "...(truncated)";
        }

        return content;
    }
}
