using Xunit;

namespace GameBanking.Tests;

public class SavingsTests
{
    [Fact]
    public async Task RoutesAndPenaltyChanged()
    {
        var h = new FakeHandler(
            FakeHandler.Reply(200, new { account_id = "s1", accrued_interest_minor = 5 }),
            FakeHandler.Reply(201, new { fixed_deposit = new { id = "f1" } }),
            FakeHandler.Reply(409, new { error = new { code = "penalty_changed", message = "x", details = new { }, correlation_id = "r" } }));
        var c = new GameBankClient(new GameBankOptions { ApiKey = "gbe_test_sk_abc", BaseUrl = "http://engine.test/", HttpClient = new HttpClient(h) });
        Assert.Equal(5, (await c.Accounts.InterestAsync("s1")).AccruedInterestMinor);
        await c.FixedDeposits.OpenAsync(new OpenFixedDepositParams { SourceAccountId = "a1", ProductId = "p1", TermDays = 30, AmountMinor = 1000 });
        var e = await Assert.ThrowsAsync<PenaltyChangedException>(() => c.FixedDeposits.BreakAsync("f1", 1));
        Assert.IsAssignableFrom<ConflictException>(e);
        Assert.Equal(new[] { "GET /v1/accounts/s1/interest", "POST /v1/fixed-deposits", "POST /v1/fixed-deposits/f1/break" },
            h.Calls.Select(x => $"{x.Method} {x.RequestUri!.AbsolutePath}"));
        Assert.True(h.Calls[1].Headers.Contains("Idempotency-Key"));
        Assert.Equal("{\"max_penalty_minor\":1}", h.Bodies[2]);
    }
}
