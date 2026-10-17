using Xunit;

namespace GameBanking.Tests;

public class CardTests
{
    [Fact]
    public async Task DeclineIsAResultAndRoutesAreRight()
    {
        var h = new FakeHandler(
            FakeHandler.Reply(201, new { authorization = new { id = "a1", status = "declined", decline_code = "card_blocked" } }),
            FakeHandler.Reply(200, new { id = "c1", status = "stolen" }),
            FakeHandler.Reply(201, new { refund = new { id = "r1" } }),
            FakeHandler.Reply(201, new { id = "m1" }));
        var c = new GameBankClient(new GameBankOptions { ApiKey = "gbe_test_sk_abc", BaseUrl = "http://engine.test/", HttpClient = new HttpClient(h) });
        var r = await c.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = "c1", MerchantAccountId = "ma1", AmountMinor = 500 });
        Assert.True(r.Authorization.IsDeclined);
        Assert.Equal(DeclineCodes.CardBlocked, r.Authorization.DeclineCode);
        await c.Cards.ReportLostAsync("c1", "stolen");
        await c.CardAuthorizations.RefundAsync("a1", new RefundCardParams { AmountMinor = 100 });
        await c.Merchants.CreateAsync(new CreateMerchantParams { ExternalMerchantId = "shop-1", Name = "Shop" });
        Assert.Equal(new[]
        {
            "POST /v1/card-authorizations", "POST /v1/cards/c1/report-lost", "POST /v1/card-authorizations/a1/refunds", "POST /v1/merchants",
        }, h.Calls.Select(x => $"{x.Method} {x.RequestUri!.AbsolutePath}"));
        Assert.True(h.Calls[0].Headers.Contains("Idempotency-Key"));
        Assert.True(h.Calls[2].Headers.Contains("Idempotency-Key"));
        Assert.Equal("{\"reason\":\"stolen\"}", h.Bodies[1]);
        await Assert.ThrowsAsync<InvalidRequestException>(() =>
            c.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = "c1", AmountMinor = 0 }));
    }
}
