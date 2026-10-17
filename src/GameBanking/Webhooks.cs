using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameBanking;

/// <summary>Webhook event types an endpoint can subscribe to ("*" means all).</summary>
public static class WebhookEventTypes
{
    public const string AccountDebited = "account.debited";
    public const string AccountCredited = "account.credited";
    public const string Test = "webhook.test";
}

public sealed record WebhookEndpoint
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string? Description { get; init; }
    public List<string> EventTypes { get; init; } = new();
    /// <summary>"active" or "disabled"; the engine disables an endpoint after three days of failed deliveries.</summary>
    public string Status { get; init; } = "";
    public string? DisabledReason { get; init; }
    public string SecretPrefix { get; init; } = "";
    /// <summary>Set only when the endpoint is created or its secret rotated.</summary>
    public string? Secret { get; init; }
    /// <summary>Until then deliveries are also signed with the previous secret.</summary>
    public DateTimeOffset? PreviousSecretExpiresAt { get; init; }
    public DateTimeOffset? FailingSince { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateWebhookEndpointParams
{
    /// <summary>https (the sandbox also accepts http); private and loopback addresses are refused.</summary>
    public required string Url { get; init; }
    public string? Description { get; init; }
    /// <summary>Defaults to ["*"].</summary>
    public List<string>? EventTypes { get; init; }
}

/// <summary>Changes an endpoint; null fields are unchanged.</summary>
public sealed record UpdateWebhookEndpointParams
{
    public string? Url { get; init; }
    public string? Description { get; init; }
    public List<string>? EventTypes { get; init; }
    /// <summary>"active" re-enables an endpoint; "disabled" pauses it.</summary>
    public string? Status { get; init; }
}

/// <summary>The body of a webhook delivery, and an item of the events feed. Deduplicate on Id: delivery is at least once.</summary>
public sealed record WebhookEvent
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? GameTime { get; init; }
    public string GameId { get; init; } = "";
    public string Environment { get; init; } = "";
    /// <summary>An <see cref="AccountAlert"/> for account.* events; read it with <see cref="Alert"/>.</summary>
    public JsonElement Data { get; init; }

    /// <summary>The data of an account.debited or account.credited event.</summary>
    public AccountAlert Alert() =>
        Type is WebhookEventTypes.AccountDebited or WebhookEventTypes.AccountCredited
            ? Data.Deserialize<AccountAlert>(Transport.Json) ?? throw new InvalidOperationException("empty alert")
            : throw new InvalidOperationException($"{Type} is not an account alert");
}

/// <summary>A fee or levy line on an alert: charged on a debit, refunded on a credit.</summary>
public sealed record AlertCharge
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string? FeeRuleId { get; init; }
    public long AmountMinor { get; init; }
}

public sealed record AlertBalance
{
    public long PostedMinor { get; init; }
    public long AvailableMinor { get; init; }
}

/// <summary>The other side of the principal; customer account numbers are masked to the last four digits.</summary>
public sealed record AlertCounterparty
{
    /// <summary>"customer" or "bank".</summary>
    public string Type { get; init; } = "";
    public string AccountName { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string BankCode { get; init; } = "";
}

/// <summary>The data of an account.debited or account.credited event.</summary>
public sealed record AccountAlert
{
    public string AccountId { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string AccountName { get; init; } = "";
    public string? PlayerId { get; init; }
    public string BankId { get; init; } = "";
    public string BankCode { get; init; } = "";
    public string TransactionId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string TransactionType { get; init; } = "";
    public string? RelatedTransactionId { get; init; }
    /// <summary>The principal (0 for a fee-only transaction).</summary>
    public long AmountMinor { get; init; }
    public List<AlertCharge> Charges { get; init; } = new();
    /// <summary>Principal plus charges: what left (or reached) the account.</summary>
    public long TotalMinor { get; init; }
    public string TotalDisplay { get; init; } = "";
    public CurrencyBrief Currency { get; init; } = new();
    public AlertBalance BalanceAfter { get; init; } = new();
    public string? Narration { get; init; }
    public string? Reference { get; init; }
    public AlertCounterparty? Counterparty { get; init; }
    public DateTimeOffset GameTime { get; init; }
    /// <summary>Increases with every ledger entry: keep the alert with the highest for the latest balance.</summary>
    public long Sequence { get; init; }
}

public sealed record WebhookDelivery
{
    public string Id { get; init; } = "";
    public string EventId { get; init; } = "";
    public string EventType { get; init; } = "";
    public string EndpointId { get; init; } = "";
    public string EndpointUrl { get; init; } = "";
    /// <summary>pending, delivering, delivered or failed.</summary>
    public string State { get; init; } = "";
    public int Attempts { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    /// <summary>The endpoint's HTTP status on the last attempt.</summary>
    public int? LastStatus { get; init; }
    public string? LastError { get; init; }
    public int? LastResponseMs { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record WebhookEventPage
{
    public List<WebhookEvent> Data { get; init; } = new();
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
}

public sealed record ListWebhookEventsParams
{
    public string? Type { get; init; }
    public string? AccountId { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }
}

/// <summary>Signed event notifications (debit and credit alerts). Verify deliveries with <see cref="WebhookVerifier"/>.</summary>
public sealed class WebhooksResource
{
    internal WebhooksResource(Transport t)
    {
        Endpoints = new(t);
        Events = new(t);
    }

    public WebhookEndpointsResource Endpoints { get; }
    public WebhookEventsResource Events { get; }
}

public sealed class WebhookEndpointsResource
{
    private readonly Transport _t;
    internal WebhookEndpointsResource(Transport t) => _t = t;

    /// <summary>Registers an endpoint. The result's Secret is shown only now: store it for verification. Needs webhooks:write.</summary>
    public Task<WebhookEndpoint> CreateAsync(CreateWebhookEndpointParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<WebhookEndpoint>(new(HttpMethod.Post, "/v1/webhooks/endpoints") { Body = p }, o, ct);

    public async Task<List<WebhookEndpoint>> ListAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<WebhookEndpoint>>(new(HttpMethod.Get, "/v1/webhooks/endpoints"), o, ct).ConfigureAwait(false)).Data;

    public Task<WebhookEndpoint> GetAsync(string endpointId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<WebhookEndpoint>(new(HttpMethod.Get, $"/v1/webhooks/endpoints/{Ops.Id(endpointId)}"), o, ct);

    /// <summary>Changes the URL, description or event types, or pauses (Status "disabled") and re-enables it.</summary>
    public Task<WebhookEndpoint> UpdateAsync(string endpointId, UpdateWebhookEndpointParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<WebhookEndpoint>(new(HttpMethod.Patch, $"/v1/webhooks/endpoints/{Ops.Id(endpointId)}") { Body = p }, o, ct);

    public Task DeleteAsync(string endpointId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync(new(HttpMethod.Delete, $"/v1/webhooks/endpoints/{Ops.Id(endpointId)}"), o, ct);

    /// <summary>Issues a new secret (in the result). The old one keeps signing for 24 hours.</summary>
    public Task<WebhookEndpoint> RotateSecretAsync(string endpointId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<WebhookEndpoint>(new(HttpMethod.Post, $"/v1/webhooks/endpoints/{Ops.Id(endpointId)}/rotate-secret"), o, ct);

    /// <summary>Sends a webhook.test event now and returns how that attempt went.</summary>
    public Task<WebhookDelivery> TestAsync(string endpointId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<WebhookDelivery>(new(HttpMethod.Post, $"/v1/webhooks/endpoints/{Ops.Id(endpointId)}/test"), o, ct);
}

public sealed class WebhookEventsResource
{
    private readonly Transport _t;
    internal WebhookEventsResource(Transport t) => _t = t;

    /// <summary>One page of the event log, newest first (kept 30 days). Also a polling feed.</summary>
    public Task<WebhookEventPage> ListAsync(ListWebhookEventsParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new ListWebhookEventsParams();
        return _t.SendAsync<WebhookEventPage>(new(HttpMethod.Get, "/v1/webhooks/events")
        {
            Query = new Dictionary<string, string?>
            {
                ["type"] = p.Type, ["account_id"] = p.AccountId,
                ["limit"] = p.Limit?.ToString(CultureInfo.InvariantCulture), ["cursor"] = p.Cursor,
            },
        }, o, ct);
    }

    /// <summary>Every event matching the filter, newest first, fetching pages as needed.</summary>
    public async IAsyncEnumerable<WebhookEvent> IterateAsync(ListWebhookEventsParams? p = null, RequestOptions? o = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var q = (p ?? new ListWebhookEventsParams()) with { Cursor = null };
        do
        {
            var page = await ListAsync(q, o, ct).ConfigureAwait(false);
            foreach (var ev in page.Data) yield return ev;
            q = q with { Cursor = page.NextCursor };
        } while (!string.IsNullOrEmpty(q.Cursor));
    }

    public Task<WebhookEvent> GetAsync(string eventId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<WebhookEvent>(new(HttpMethod.Get, $"/v1/webhooks/events/{Ops.Id(eventId)}"), o, ct);

    /// <summary>The event's deliveries, one per endpoint, with attempts and the last error.</summary>
    public async Task<List<WebhookDelivery>> DeliveriesAsync(string eventId, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<WebhookDelivery>>(new(HttpMethod.Get, $"/v1/webhooks/events/{Ops.Id(eventId)}/deliveries"), o, ct).ConfigureAwait(false)).Data;

    /// <summary>Sends the event again, to one endpoint or (null) every subscribed one. Retries start over.</summary>
    public async Task<List<WebhookDelivery>> RedeliverAsync(string eventId, string? endpointId = null, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<WebhookDelivery>>(new(HttpMethod.Post, $"/v1/webhooks/events/{Ops.Id(eventId)}/redeliver")
        { Body = endpointId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["endpoint_id"] = endpointId } }, o, ct).ConfigureAwait(false)).Data;
}

/// <summary>The request did not come from the engine, or was replayed or tampered with. Answer 400.</summary>
public sealed class WebhookVerificationException : Exception
{
    /// <summary>malformed_header, timestamp_out_of_tolerance, no_matching_signature or invalid_payload.</summary>
    public string Reason { get; }

    public WebhookVerificationException(string reason, string message, Exception? inner = null) : base(message, inner) => Reason = reason;
}

/// <summary>Checks webhook deliveries from the engine.</summary>
/// <example><code>
/// var body = await new StreamReader(Request.Body).ReadToEndAsync();
/// var ev = WebhookVerifier.VerifyWebhook(body, Request.Headers[WebhookVerifier.SignatureHeader], secret);
/// if (ev.Type == WebhookEventTypes.AccountDebited) Notify(ev.Alert());
/// </code></example>
public static class WebhookVerifier
{
    /// <summary>The request header that carries a delivery's timestamp and signatures.</summary>
    public const string SignatureHeader = "GBE-Signature";

    /// <summary>
    /// Verifies a delivery and returns its event. Pass the raw body (before any JSON parsing), the GBE-Signature
    /// header and the endpoint secret. Throws <see cref="WebhookVerificationException"/> if no signature matches or the
    /// timestamp is outside the tolerance (default 5 minutes).
    /// </summary>
    public static WebhookEvent VerifyWebhook(string payload, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        VerifyWebhook(Encoding.UTF8.GetBytes(payload), signatureHeader, new[] { secret }, tolerance, now);

    /// <summary>Verifies against several secrets, e.g. while you roll a new one out.</summary>
    public static WebhookEvent VerifyWebhook(byte[] payload, string? signatureHeader, IEnumerable<string> secrets, TimeSpan? tolerance = null, DateTimeOffset? now = null)
    {
        long ts = 0;
        var sigs = new List<byte[]>();
        foreach (var part in (signatureHeader ?? "").Split(','))
        {
            var i = part.IndexOf('=');
            if (i < 0) continue;
            var key = part[..i].Trim();
            var value = part[(i + 1)..].Trim();
            if (key == "t" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) ts = n;
            else if (key == "v1")
            {
                try { sigs.Add(Convert.FromHexString(value)); }
                catch (FormatException) { }
            }
        }
        if (ts <= 0 || sigs.Count == 0)
            throw new WebhookVerificationException("malformed_header", $"missing or malformed {SignatureHeader} header");
        var age = (now ?? DateTimeOffset.UtcNow) - DateTimeOffset.FromUnixTimeSeconds(ts);
        var limit = tolerance ?? TimeSpan.FromMinutes(5);
        if (age > limit || age < -limit)
            throw new WebhookVerificationException("timestamp_out_of_tolerance", "the signature's timestamp is outside the tolerance");

        var prefix = Encoding.ASCII.GetBytes(ts.ToString(CultureInfo.InvariantCulture) + ".");
        var signed = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(signed, 0);
        payload.CopyTo(signed, prefix.Length);
        var matched = false;
        foreach (var secret in secrets)
        {
            var want = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
            foreach (var s in sigs)
                matched |= CryptographicOperations.FixedTimeEquals(s, want);
        }
        if (!matched) throw new WebhookVerificationException("no_matching_signature", "no signature matches the secret");
        try
        {
            return JsonSerializer.Deserialize<WebhookEvent>(payload, Transport.Json) ?? throw new JsonException("null");
        }
        catch (JsonException e)
        {
            throw new WebhookVerificationException("invalid_payload", "the body is not a JSON event", e);
        }
    }

    /// <summary>Verifies a raw byte body with one secret.</summary>
    public static WebhookEvent VerifyWebhook(byte[] payload, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        VerifyWebhook(payload, signatureHeader, new[] { secret }, tolerance, now);
}
