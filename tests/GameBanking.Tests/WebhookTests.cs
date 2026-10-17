using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace GameBanking.Tests;

public class WebhookTests
{
    // The engine's published test vector (docs/webhooks.md).
    private const string Body = "{\"hello\":\"world\"}";
    private const string Vector = "f592bbf3951cfc94e560eecfb5d9dd4da6b0fff2e626235f8ab4b54860925d0b";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);

    private static string Sign(string secret, long ts, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}.{body}"))).ToLowerInvariant();

    private static string Reason(Action verify) => Assert.Throws<WebhookVerificationException>(verify).Reason;

    [Fact]
    public void AcceptsTheEngineVector()
    {
        WebhookVerifier.VerifyWebhook(Body, $"t=1700000000,v1={Vector}", "whsec_test", now: Now);
        WebhookVerifier.VerifyWebhook(Encoding.UTF8.GetBytes(Body), $"t=1700000000,v1={Vector}", "whsec_test", now: Now);
        var h = $"t=1700000000,v1={Sign("whsec_new", 1_700_000_000, Body)},v1={Vector}";
        WebhookVerifier.VerifyWebhook(Body, h, "whsec_new", now: Now);
        WebhookVerifier.VerifyWebhook(Body, h, "whsec_test", now: Now);
        WebhookVerifier.VerifyWebhook(Encoding.UTF8.GetBytes(Body), $"t=1700000000,v1={Vector}", new[] { "whsec_other", "whsec_test" }, now: Now);
    }

    [Fact]
    public void RejectsTamperingReplaysAndBadHeaders()
    {
        var h = $"t=1700000000,v1={Vector}";
        Assert.Equal("no_matching_signature", Reason(() => WebhookVerifier.VerifyWebhook("{\"hello\":\"world!\"}", h, "whsec_test", now: Now)));
        Assert.Equal("no_matching_signature", Reason(() => WebhookVerifier.VerifyWebhook(Body, h, "whsec_wrong", now: Now)));
        Assert.Equal("timestamp_out_of_tolerance", Reason(() => WebhookVerifier.VerifyWebhook(Body, h, "whsec_test", now: Now.AddSeconds(291))));
        Assert.Equal("timestamp_out_of_tolerance", Reason(() => WebhookVerifier.VerifyWebhook(Body, h, "whsec_test", now: Now.AddSeconds(-1010))));
        WebhookVerifier.VerifyWebhook(Body, h, "whsec_test", TimeSpan.FromSeconds(1000), Now.AddSeconds(890));
        Assert.Equal("malformed_header", Reason(() => WebhookVerifier.VerifyWebhook(Body, $"v1={Vector}", "whsec_test", now: Now)));
        Assert.Equal("malformed_header", Reason(() => WebhookVerifier.VerifyWebhook(Body, null, "whsec_test", now: Now)));
        Assert.Equal("malformed_header", Reason(() => WebhookVerifier.VerifyWebhook(Body, "t=1700000000,v1=zz", "whsec_test", now: Now)));
        Assert.Equal("invalid_payload", Reason(() => WebhookVerifier.VerifyWebhook("plain text", $"t=1700000000,v1={Sign("s", 1_700_000_000, "plain text")}", "s", now: Now)));
    }

    [Fact]
    public void DecodesAnAccountAlert()
    {
        const string body = "{\"id\":\"ev1\",\"type\":\"account.debited\",\"created_at\":\"2026-10-16T09:00:00Z\",\"game_time\":null,\"game_id\":\"g\",\"environment\":\"sandbox\"," +
            "\"data\":{\"account_id\":\"a1\",\"total_minor\":250,\"charges\":[{\"kind\":\"fee\",\"name\":\"Fee\",\"fee_rule_id\":null,\"amount_minor\":50}]," +
            "\"balance_after\":{\"posted_minor\":10,\"available_minor\":5},\"counterparty\":{\"type\":\"customer\",\"account_name\":\"Bayo\",\"account_number\":\"******4321\",\"bank_code\":\"058\"}}}";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ev = WebhookVerifier.VerifyWebhook(body, $"t={ts},v1={Sign("s", ts, body)}", "s");
        var a = ev.Alert();
        Assert.Equal(("ev1", "a1", 250L, 50L, 5L, "Bayo"), (ev.Id, a.AccountId, a.TotalMinor, a.Charges[0].AmountMinor, a.BalanceAfter.AvailableMinor, a.Counterparty!.AccountName));
        Assert.Throws<InvalidOperationException>(() => new WebhookEvent { Type = WebhookEventTypes.Test }.Alert());
    }

    [Fact]
    public async Task CallsTheWebhookRoutes()
    {
        var h = new FakeHandler(
            FakeHandler.Reply(201, new { id = "e1", secret = "whsec_x" }),
            FakeHandler.Reply(200, new { data = new[] { new { id = "ev1" } }, next_cursor = "c2", has_more = true }),
            FakeHandler.Reply(200, new { data = new[] { new { id = "ev2" } }, next_cursor = (string?)null, has_more = false }),
            FakeHandler.Reply(202, new { data = Array.Empty<object>() }),
            FakeHandler.Reply(204));
        var c = new GameBankClient(new GameBankOptions { ApiKey = "gbe_test_sk_abc", BaseUrl = "http://engine.test/", HttpClient = new HttpClient(h) });
        var ep = await c.Webhooks.Endpoints.CreateAsync(new CreateWebhookEndpointParams { Url = "https://hooks.example.com", EventTypes = [WebhookEventTypes.AccountDebited] });
        Assert.Equal("whsec_x", ep.Secret);
        var ids = new List<string>();
        await foreach (var ev in c.Webhooks.Events.IterateAsync(new ListWebhookEventsParams { Type = WebhookEventTypes.AccountDebited, AccountId = "a1" })) ids.Add(ev.Id);
        await c.Webhooks.Events.RedeliverAsync("ev1", "e1");
        await c.Webhooks.Endpoints.DeleteAsync("e1");
        Assert.Equal(new[] { "ev1", "ev2" }, ids);
        Assert.Equal(new[]
        {
            "POST /v1/webhooks/endpoints",
            "GET /v1/webhooks/events?type=account.debited&account_id=a1",
            "GET /v1/webhooks/events?type=account.debited&account_id=a1&cursor=c2",
            "POST /v1/webhooks/events/ev1/redeliver",
            "DELETE /v1/webhooks/endpoints/e1",
        }, h.Calls.Select(r => $"{r.Method} {r.RequestUri!.PathAndQuery}"));
        Assert.Equal("{\"url\":\"https://hooks.example.com\",\"event_types\":[\"account.debited\"]}", h.Bodies[0]);
        Assert.Equal("{\"endpoint_id\":\"e1\"}", h.Bodies[3]);
    }
}
