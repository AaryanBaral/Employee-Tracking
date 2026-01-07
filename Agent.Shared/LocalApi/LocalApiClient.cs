using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agent.Shared.Models;

namespace Agent.Shared.LocalApi;

public sealed class LocalApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public LocalApiClient(HttpClient httpClient, string baseUrl, string token)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Remove(LocalApiConstants.AuthHeaderName);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(LocalApiConstants.AuthHeaderName, token);
    }

    public Task<LocalApiResult<LocalHealthResponse>> GetHealthAsync(CancellationToken ct)
        => SendAsync<LocalHealthResponse>(HttpMethod.Get, "/health", null, ct);

    public Task<LocalApiResult<LocalVersionResponse>> GetVersionAsync(CancellationToken ct)
        => SendAsync<LocalVersionResponse>(HttpMethod.Get, "/version", null, ct);

    public Task<LocalApiResult<LocalDiagResponse>> GetDiagAsync(CancellationToken ct)
        => SendAsync<LocalDiagResponse>(HttpMethod.Get, "/diag", null, ct);

    public Task<LocalApiResult> PostIdleAsync(IdleSampleRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/events/idle", request, ct);

    public Task<LocalApiResult> PostAppFocusAsync(AppFocusRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/events/app-focus", request, ct);

    public Task<LocalApiResult> PostWebEventAsync(WebEvent request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/events/web", request, ct);

    private async Task<LocalApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            HttpResponseMessage response;
            if (method == HttpMethod.Get)
            {
                response = await _httpClient.GetAsync(path, ct);
            }
            else
            {
                response = await _httpClient.PostAsJsonAsync(path, body, _jsonOptions, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                return LocalApiResult<T>.FromStatus(response.StatusCode);
            }

            if (typeof(T) == typeof(EmptyResponse))
            {
                return LocalApiResult<T>.Ok(default);
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct);
            return LocalApiResult<T>.Ok(payload);
        }
        catch (Exception ex)
        {
            return LocalApiResult<T>.FromException(ex);
        }
    }

    private async Task<LocalApiResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            HttpResponseMessage response;
            if (method == HttpMethod.Get)
            {
                response = await _httpClient.GetAsync(path, ct);
            }
            else
            {
                response = await _httpClient.PostAsJsonAsync(path, body, _jsonOptions, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                return LocalApiResult.FromStatus(response.StatusCode);
            }

            return LocalApiResult.Ok();
        }
        catch (Exception ex)
        {
            return LocalApiResult.FromException(ex);
        }
    }

    private sealed record EmptyResponse;
}

public sealed record LocalApiResult(bool Success, HttpStatusCode? StatusCode, string? Error)
{
    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public static LocalApiResult Ok() => new(true, HttpStatusCode.OK, null);

    public static LocalApiResult FromStatus(HttpStatusCode statusCode)
        => new(false, statusCode, $"HTTP {(int)statusCode} {statusCode}");

    public static LocalApiResult FromException(Exception ex)
        => new(false, null, ex.Message);
}

public sealed record LocalApiResult<T>(bool Success, T? Value, HttpStatusCode? StatusCode, string? Error)
{
    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public static LocalApiResult<T> Ok(T? value) => new(true, value, HttpStatusCode.OK, null);

    public static LocalApiResult<T> FromStatus(HttpStatusCode statusCode)
        => new(false, default, statusCode, $"HTTP {(int)statusCode} {statusCode}");

    public static LocalApiResult<T> FromException(Exception ex)
        => new(false, default, null, ex.Message);
}
