using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace GameBanking.Tests;

/// <summary>Returns scripted replies in order and records each request.</summary>
internal sealed class FakeHandler(params object[] replies) : HttpMessageHandler
{
    private readonly Queue<object> _replies = new(replies);
    public List<HttpRequestMessage> Calls { get; } = new();
    /// <summary>Request bodies, read on arrival (the SDK disposes requests after sending).</summary>
    public List<string?> Bodies { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Add(request);
        Bodies.Add(request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult());
        var r = _replies.Dequeue();
        if (r is Exception e) throw e;
        var (status, body, headers) = ((int, object?, Dictionary<string, string>?))r;
        var text = body as string ?? (body is null ? "" : JsonSerializer.Serialize(body));
        var ctype = body is string s && s.StartsWith('<') ? "text/html" : "application/json";
        var res = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, ctype) };
        foreach (var (k, v) in headers ?? new()) res.Headers.TryAddWithoutValidation(k, v);
        return Task.FromResult(res);
    }

    public static (int, object?, Dictionary<string, string>?) Reply(int status, object? body = null, Dictionary<string, string>? headers = null) => (status, body, headers);
}

public class CoreTests
{
    private static readonly object TransferOk = FakeHandler.Reply(201, new { transaction = new { id = "t1" }, source_balance = new { } });

    private static (GameBankClient, FakeHandler, List<TimeSpan>) Client(FakeHandler h, int maxRetries = 2)
    {
        var c = new GameBankClient(new GameBankOptions { ApiKey = "gbe_test_sk_abc", BaseUrl = "http://engine.test/", HttpClient = new HttpClient(h), MaxRetries = maxRetries });
        var waits = new List<TimeSpan>();
        c.Transport.Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; };
        return (c, h, waits);
    }

    private static object Err(int status, string code, object? details = null) =>
        FakeHandler.Reply(status, new { error = new { code, message = code, details = details ?? new { }, correlation_id = "req_1" } });

    private static string? Header(HttpRequestMessage r, string name) => r.Headers.TryGetValues(name, out var v) ? v.First() : null;

    [Fact]
    public async Task Configuration()
    {
        var (c, h, _) = Client(new FakeHandler(FakeHandler.Reply(200, new { mode = "manual" })));
        Assert.Equal("sandbox", c.Environment);
        await c.Clock.GetAsync();
        Assert.Equal("http://engine.test/v1/clock", h.Calls[0].RequestUri!.ToString());
        Assert.Equal("Bearer gbe_test_sk_abc", h.Calls[0].Headers.Authorization!.ToString());
        Assert.Equal("production", new GameBankClient(new GameBankOptions { ApiKey = "gbe_live_sk_x", BaseUrl = "http://e" }).Environment);
        var savedKey = Environment.GetEnvironmentVariable("GBE_API_KEY");
        var savedUrl = Environment.GetEnvironmentVariable("GBE_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("GBE_API_KEY", null);
            Environment.SetEnvironmentVariable("GBE_BASE_URL", null);
            Assert.Equal("missing_api_key", Assert.Throws<GameBankException>(() => new GameBankClient(new GameBankOptions { BaseUrl = "http://e" })).Code);
            Assert.Equal("missing_base_url", Assert.Throws<GameBankException>(() => new GameBankClient(new GameBankOptions { ApiKey = "gbe_test_sk_x" })).Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GBE_API_KEY", savedKey);
            Environment.SetEnvironmentVariable("GBE_BASE_URL", savedUrl);
        }
    }

    [Fact]
    public async Task IdempotencyKeyIsReusedAcrossRetries()
    {
        var (c, h, _) = Client(new FakeHandler(new HttpRequestException("reset"), FakeHandler.Reply(503, new { }), TransferOk));
        var r = await c.Transfers.CreateAsync(new TransferParams { SourceAccountId = "a", DestinationAccountId = "b", AmountMinor = 100 });
        Assert.Equal("t1", r.Transaction.Id);
        Assert.Equal(3, h.Calls.Count);
        var keys = h.Calls.Select(x => Header(x, "Idempotency-Key")).Distinct().ToList();
        Assert.Single(keys);
        Assert.StartsWith("sdk_", keys[0]);
    }

    [Fact]
    public async Task CallersKeyIsUsed()
    {
        var (c, h, _) = Client(new FakeHandler(TransferOk));
        await c.Transfers.CreateAsync(new TransferParams { SourceAccountId = "a", DestinationAccountId = "b", AmountMinor = 1 }, new RequestOptions { IdempotencyKey = "order-42" });
        Assert.Equal("order-42", Header(h.Calls[0], "Idempotency-Key"));
    }

    [Fact]
    public async Task PlainPostIsNotRetried()
    {
        foreach (var first in new object[] { new HttpRequestException("reset"), FakeHandler.Reply(502, new { }) })
        {
            var (c, h, _) = Client(new FakeHandler(first, FakeHandler.Reply(201, new { })));
            await Assert.ThrowsAnyAsync<GameBankException>(() => c.Accounts.OpenAsync(new OpenAccountParams { BankId = "b", PlayerId = "p" }));
            Assert.Single(h.Calls);
            Assert.Null(Header(h.Calls[0], "Idempotency-Key"));
        }
    }

    [Fact]
    public async Task RateLimitHonoursRetryAfter()
    {
        var (c, h, waits) = Client(new FakeHandler(FakeHandler.Reply(429, new { error = new { code = "rate_limited" } }, new() { ["Retry-After"] = "3" }), FakeHandler.Reply(201, new { id = "acc" })));
        var a = await c.Accounts.OpenAsync(new OpenAccountParams { BankId = "b", PlayerId = "p" });
        Assert.Equal("acc", a.Id);
        Assert.Equal(2, h.Calls.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, waits);
    }

    [Fact]
    public async Task GetsAndUpsertsRetryAfterNetworkErrors()
    {
        var (c, h, _) = Client(new FakeHandler(new HttpRequestException("reset"), FakeHandler.Reply(200, new { id = "p1" }), new HttpRequestException("reset"), FakeHandler.Reply(200, new { id = "p2" })));
        Assert.Equal("p1", (await c.Players.GetAsync("p1")).Id);
        Assert.Equal("p2", (await c.Players.UpsertAsync(new UpsertPlayerParams { ExternalPlayerId = "x" })).Id);
        Assert.Equal(4, h.Calls.Count);
    }

    [Fact]
    public async Task StopsAfterMaxRetries()
    {
        var (c, h, _) = Client(new FakeHandler(FakeHandler.Reply(500, new { }), FakeHandler.Reply(500, new { })), maxRetries: 1);
        await Assert.ThrowsAsync<ServerException>(() => c.Accounts.BalanceAsync("a"));
        Assert.Equal(2, h.Calls.Count);
    }

    [Fact]
    public async Task RequestInProgressRetriedForKeyedRequests()
    {
        var (c, h, _) = Client(new FakeHandler(Err(409, "request_in_progress"), TransferOk));
        await c.Transfers.CreateAsync(new TransferParams { SourceAccountId = "a", DestinationAccountId = "b", AmountMinor = 1 });
        Assert.Equal(2, h.Calls.Count);
    }

    [Fact]
    public async Task TypedErrorsCarryDetails()
    {
        var (c, _, _) = Client(new FakeHandler(Err(422, "insufficient_funds", new { available_balance = 5 })));
        var e = await Assert.ThrowsAsync<InsufficientFundsException>(() => c.Transfers.CreateAsync(new TransferParams { SourceAccountId = "a", DestinationAccountId = "b", AmountMinor = 9 }));
        Assert.IsAssignableFrom<UnprocessableException>(e);
        Assert.Equal(422, e.Status);
        Assert.Equal(5L, e.Details["available_balance"]);
        Assert.Equal("req_1", e.RequestId);
        var (c2, _, _) = Client(new FakeHandler(Err(409, "idempotency_key_reused")));
        await Assert.ThrowsAsync<ConflictException>(() => c2.Transfers.CreateAsync(new TransferParams { SourceAccountId = "a", DestinationAccountId = "b", AmountMinor = 9 }));
        var (c3, h3, _) = Client(new FakeHandler(Err(409, "fee_changed", new { fees_total_minor = 70 })));
        var fc = await Assert.ThrowsAsync<FeeChangedException>(() => c3.Transfers.CreateAsync(new TransferParams { SourceAccountId = "a", DestinationAccountId = "b", AmountMinor = 9, MaxFeeMinor = 60 }));
        Assert.Equal(70L, fc.Details["fees_total_minor"]);
        Assert.Contains("\"max_fee_minor\":60", h3.Bodies[0]);
    }

    [Fact]
    public async Task ConnectionErrorAfterRetries()
    {
        var (c, _, _) = Client(new FakeHandler(new HttpRequestException("refused"), new HttpRequestException("refused")), maxRetries: 1);
        var e = await Assert.ThrowsAsync<ConnectionException>(() => c.Clock.GetAsync());
        Assert.Equal(0, e.Status);
    }

    [Fact]
    public async Task CallerCancellationIsNotRetried()
    {
        var (c, h, _) = Client(new FakeHandler(TransferOk, TransferOk));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.Clock.GetAsync(ct: cts.Token));
        Assert.True(h.Calls.Count <= 1);
    }

    [Fact]
    public async Task HtmlInsteadOfEngineIsExplained()
    {
        var (c, _, _) = Client(new FakeHandler(FakeHandler.Reply(200, "<!doctype html>")));
        var e = await Assert.ThrowsAnyAsync<GameBankException>(() => c.Clock.GetAsync());
        Assert.Equal("invalid_response", e.Code);
        Assert.Contains("BaseUrl", e.Message);
    }

    [Fact]
    public async Task BadAmountRejectedBeforeSending()
    {
        var (c, h, _) = Client(new FakeHandler());
        Assert.Throws<InvalidRequestException>(() => { _ = c.Funding.CreateAsync(new FundingParams { AccountId = "a", AmountMinor = -1, Reason = "r", Reference = "x" }); });
        Assert.Empty(h.Calls);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("100.50", 2, 10050)]
    [InlineData("1,234.5", 2, 123450)]
    [InlineData("0.07", 2, 7)]
    [InlineData("42", 0, 42)]
    [InlineData("-3.2", 1, -32)]
    public void ToMinor(string amount, int exponent, long want) => Assert.Equal(want, Money.ToMinor(amount, exponent));

    [Theory]
    [InlineData(10050, 2, "100.50")]
    [InlineData(7, 2, "0.07")]
    [InlineData(-5, 3, "-0.005")]
    [InlineData(42, 0, "42")]
    [InlineData(long.MinValue, 0, "-9223372036854775808")]
    public void FormatMinor(long minor, int exponent, string want) => Assert.Equal(want, Money.FormatMinor(minor, exponent));

    [Fact]
    public void ToMinorRejectsBadInput()
    {
        Assert.Throws<FormatException>(() => Money.ToMinor("1.005", 2));
        Assert.Throws<FormatException>(() => Money.ToMinor("abc", 2));
        Assert.Throws<OverflowException>(() => Money.ToMinor("99999999999999999999", 2));
    }
}
