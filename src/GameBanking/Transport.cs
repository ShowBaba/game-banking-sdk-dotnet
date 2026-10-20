using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBanking;

/// <summary>Client configuration. ApiKey defaults to GBE_API_KEY; BaseUrl to GBE_BASE_URL, then the hosted engine.</summary>
public sealed record GameBankOptions
{
    /// <summary>The hosted engine. Override it with <see cref="BaseUrl"/> or GBE_BASE_URL for a local or self-hosted engine.</summary>
    public const string DefaultBaseUrl = "https://api.gamebank.dev";

    /// <summary>Game API key, developer credential or sign-in session token.</summary>
    public string? ApiKey { get; init; }
    /// <summary>The engine's address. Defaults to GBE_BASE_URL, then https://api.gamebank.dev. Set it only for a local or self-hosted engine.</summary>
    public string? BaseUrl { get; init; }
    /// <summary>Per-attempt timeout. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Extra attempts after a retryable failure. Default 2.</summary>
    public int MaxRetries { get; init; } = 2;
    /// <summary>Your own HttpClient (e.g. from IHttpClientFactory). Its own Timeout should be infinite or longer than <see cref="Timeout"/>.</summary>
    public HttpClient? HttpClient { get; init; }
    /// <summary>Called after every HTTP response, including retried ones.</summary>
    public Action<ResponseInfo>? OnResponse { get; init; }
}

/// <summary>Per-call options.</summary>
public sealed record RequestOptions
{
    /// <summary>Your own stable key for a money-moving call. Retrying with it never moves money twice.</summary>
    public string? IdempotencyKey { get; init; }
    public int? MaxRetries { get; init; }
}

/// <summary>Describes one HTTP response.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">API path, without the query.</param>
/// <param name="Status">HTTP status.</param>
/// <param name="RequestId">The engine's X-Request-Id.</param>
/// <param name="Replayed">True when the engine returned a stored result for a repeated Idempotency-Key.</param>
/// <param name="Attempt">0 for the first attempt.</param>
public sealed record ResponseInfo(string Method, string Path, int Status, string? RequestId, bool Replayed, int Attempt);

internal sealed record Operation(HttpMethod Method, string Path)
{
    public IReadOnlyDictionary<string, string?>? Query { get; init; }
    public object? Body { get; init; }
    /// <summary>Money-moving: send an Idempotency-Key, generated when the caller gives none.</summary>
    public bool Idempotent { get; init; }
    /// <summary>Safe to retry after a possible partial failure even without a key.</summary>
    public bool RetrySafe { get; init; }
}

internal sealed class Transport
{
    public const string Version = "0.5.0";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient Shared = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly GameBankOptions _opts;
    private readonly IReadOnlyDictionary<string, string> _headers;
    internal Func<TimeSpan, CancellationToken, Task> Delay = Task.Delay;

    public string BaseUrl { get; }
    public string? Environment { get; }

    public Transport(GameBankOptions opts, IReadOnlyDictionary<string, string>? headers = null)
    {
        var key = opts.ApiKey ?? System.Environment.GetEnvironmentVariable("GBE_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new GameBankException(0, "missing_api_key", "Set ApiKey or GBE_API_KEY.");
        var url = opts.BaseUrl;
        if (string.IsNullOrWhiteSpace(url)) url = System.Environment.GetEnvironmentVariable("GBE_BASE_URL");
        if (string.IsNullOrWhiteSpace(url)) url = GameBankOptions.DefaultBaseUrl;
        _apiKey = key.Trim();
        BaseUrl = url.TrimEnd('/');
        Environment = EnvironmentOf(_apiKey);
        _http = opts.HttpClient ?? Shared;
        _opts = opts;
        _headers = headers ?? new Dictionary<string, string>();
    }

    /// <summary>Reads the environment a game API key belongs to from its prefix.</summary>
    public static string? EnvironmentOf(string apiKey) =>
        apiKey.StartsWith("gbe_test_sk_", StringComparison.Ordinal) ? "sandbox"
        : apiKey.StartsWith("gbe_live_sk_", StringComparison.Ordinal) ? "production" : null;

    public Transport WithHeaders(IReadOnlyDictionary<string, string> extra)
    {
        var merged = new Dictionary<string, string>(_headers);
        foreach (var (k, v) in extra) merged[k] = v;
        return new Transport(_opts with { ApiKey = _apiKey, BaseUrl = BaseUrl }, merged) { Delay = Delay };
    }

    public async Task<T> SendAsync<T>(Operation op, RequestOptions? ro, CancellationToken ct)
    {
        var raw = await SendRawAsync(op, ro, ct).ConfigureAwait(false);
        if (raw is null) return default!;
        try
        {
            return JsonSerializer.Deserialize<T>(raw.Value.Body, Json)!;
        }
        catch (JsonException e)
        {
            throw new GameBankException(raw.Value.Status, "invalid_response",
                $"Expected JSON from the engine but got {raw.Value.ContentType ?? "an unknown content type"}. Check that BaseUrl ({BaseUrl}) points at the engine, not a website.",
                requestId: raw.Value.RequestId, inner: e);
        }
    }

    public async Task SendAsync(Operation op, RequestOptions? ro, CancellationToken ct) => await SendRawAsync(op, ro, ct).ConfigureAwait(false);

    private async Task<(int Status, string Body, string? ContentType, string? RequestId)?> SendRawAsync(Operation op, RequestOptions? ro, CancellationToken ct)
    {
        var url = BaseUrl + op.Path;
        if (op.Query is { Count: > 0 })
        {
            var parts = op.Query.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}").ToList();
            if (parts.Count > 0) url += "?" + string.Join("&", parts);
        }
        var key = ro?.IdempotencyKey ?? (op.Idempotent ? "sdk_" + Guid.NewGuid().ToString("N") : null);
        var body = op.Body is null ? null : JsonSerializer.Serialize(op.Body, op.Body.GetType(), Json);
        var safe = op.Method == HttpMethod.Get || op.Method == HttpMethod.Put || key is not null || op.RetrySafe;
        var maxRetries = ro?.MaxRetries ?? _opts.MaxRetries;

        for (var attempt = 0; ; attempt++)
        {
            GameBankException err;
            try
            {
                var res = await OnceAsync(op, url, key, body, attempt, ct).ConfigureAwait(false);
                if (res.Status is >= 200 and < 300)
                    return res.Status == 204 || res.Body.Length == 0 ? null : (res.Status, res.Body, res.ContentType, res.RequestId);
                err = ToError(res.Status, res.Body, res.RequestId, res.RetryAfter);
            }
            catch (GameBankException e)
            {
                err = e;
            }
            if (attempt >= maxRetries || !ShouldRetry(err, safe)) throw err;
            await Delay(RetryDelay(attempt, err.RetryAfter), ct).ConfigureAwait(false);
        }
    }

    private async Task<(int Status, string Body, string? ContentType, string? RequestId, double? RetryAfter)> OnceAsync(
        Operation op, string url, string? key, string? body, int attempt, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(op.Method, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Headers.UserAgent.ParseAdd($"gamebanking-sdk-dotnet/{Version}");
        foreach (var (k, v) in _headers) req.Headers.TryAddWithoutValidation(k, v);
        if (key is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_opts.Timeout);
        HttpResponseMessage res;
        string text;
        try
        {
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            text = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller cancelled: do not retry
        }
        catch (OperationCanceledException e)
        {
            throw GameBankException.For(0, "timeout", $"No response from {BaseUrl} within {_opts.Timeout.TotalSeconds:0.#} s", inner: e);
        }
        catch (HttpRequestException e)
        {
            throw GameBankException.For(0, "connection_error", $"Could not reach the engine at {BaseUrl}: {e.Message}", inner: e);
        }
        using (res)
        {
            var rid = res.Headers.TryGetValues("X-Request-Id", out var r) ? r.FirstOrDefault() : null;
            var replayed = res.Headers.TryGetValues("Idempotent-Replayed", out var rp) && rp.FirstOrDefault() == "true";
            _opts.OnResponse?.Invoke(new ResponseInfo(op.Method.Method, op.Path, (int)res.StatusCode, rid, replayed, attempt));
            double? retryAfter = res.Headers.RetryAfter?.Delta?.TotalSeconds;
            return ((int)res.StatusCode, text, res.Content.Headers.ContentType?.ToString(), rid, retryAfter is > 0 ? retryAfter : null);
        }
    }

    private static bool ShouldRetry(GameBankException e, bool safe)
    {
        if (e.Status == 429) return true;
        if (!safe) return false;
        return e.Status switch
        {
            0 => e.Code is "connection_error" or "timeout",
            500 or 502 or 503 or 504 => true,
            409 => e.Code == "request_in_progress",
            _ => false,
        };
    }

    /// <summary>The server's Retry-After (capped at 60 s) when sent, else exponential backoff with jitter.</summary>
    internal static TimeSpan RetryDelay(int attempt, double? retryAfterSeconds)
    {
        if (retryAfterSeconds is > 0) return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, 60));
        var b = Math.Min(0.5 * Math.Pow(2, attempt), 8);
        return TimeSpan.FromSeconds(b * (0.75 + Random.Shared.NextDouble() * 0.25));
    }

    private static GameBankException ToError(int status, string body, string? rid, double? retryAfter)
    {
        string? code = null, message = null, correlation = null;
        Dictionary<string, object?> details = new();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("code", out var c)) code = c.GetString();
                if (e.TryGetProperty("message", out var m)) message = m.GetString();
                if (e.TryGetProperty("correlation_id", out var cid)) correlation = cid.GetString();
                if (e.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Object)
                    foreach (var p in d.EnumerateObject()) details[p.Name] = Plain(p.Value);
            }
        }
        catch (JsonException)
        {
            /* not JSON */
        }
        return GameBankException.For(status, code ?? $"http_{status}", message ?? ((HttpStatusCode)status).ToString(), details, correlation ?? rid, retryAfter);
    }

    /// <summary>Turns JSON detail values into plain .NET values: long, double, string, bool or null.</summary>
    private static object? Plain(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.TryGetInt64(out var l) ? (object)l : v.GetDouble(), // box each branch, or the long is widened to double
        JsonValueKind.String => v.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => v.Clone(),
    };
}
