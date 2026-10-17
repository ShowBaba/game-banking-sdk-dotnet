using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace GameBanking.Tests;

/// <summary>Reports the first POST /v1/transfers as a network failure after the engine has processed it.</summary>
internal sealed class LossyHandler() : DelegatingHandler(new HttpClientHandler())
{
    public bool Dropped { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var res = await base.SendAsync(request, ct);
        if (!Dropped && request.RequestUri!.AbsolutePath.EndsWith("/v1/transfers", StringComparison.Ordinal))
        {
            Dropped = true;
            res.Dispose();
            throw new HttpRequestException("connection reset after the engine processed the request");
        }
        return res;
    }
}

/// <summary>
/// A local webhook receiver keeping each raw delivery. The engine reaches it at GBE_WEBHOOK_RECEIVER_HOST
/// (default host.docker.internal) on GBE_WEBHOOK_RECEIVER_PORT (default 9950; publish it when the tests run in a container).
/// </summary>
internal sealed class Receiver : IDisposable
{
    private readonly HttpListener _listener = new();
    public ConcurrentQueue<(string Signature, byte[] Body)> Got { get; } = new();
    public string Url { get; }

    public Receiver()
    {
        var port = Environment.GetEnvironmentVariable("GBE_WEBHOOK_RECEIVER_PORT") ?? "9950";
        var host = Environment.GetEnvironmentVariable("GBE_WEBHOOK_RECEIVER_HOST") ?? "host.docker.internal";
        _listener.Prefixes.Add($"http://*:{port}/");
        _listener.Start();
        Url = $"http://{host}:{port}/hooks";
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                using var ms = new MemoryStream();
                await ctx.Request.InputStream.CopyToAsync(ms);
                Got.Enqueue((ctx.Request.Headers[WebhookVerifier.SignatureHeader] ?? "", ms.ToArray()));
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
            }
        });
    }

    public async Task<(string Signature, byte[] Body)> WaitForAsync(string contains, int skip = 0)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = Got.Skip(skip).FirstOrDefault(d => System.Text.Encoding.UTF8.GetString(d.Body).Contains(contains, StringComparison.Ordinal));
            if (hit.Body is not null) return hit;
            await Task.Delay(200);
        }
        throw new TimeoutException($"no webhook delivery containing {contains} within 20s");
    }

    public void Dispose() => _listener.Close();
}

/// <summary>The PRD §16.2 journey against a real engine. Runs only when GBE_BASE_URL is set.</summary>
public class ContractTests(ITestOutputHelper output)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task PrdJourney()
    {
        var baseUrl = Environment.GetEnvironmentVariable("GBE_BASE_URL");
        if (string.IsNullOrEmpty(baseUrl))
        {
            output.WriteLine("SKIPPED: set GBE_BASE_URL to run against an engine");
            return;
        }

        // Sign up (a browser flow, so a plain request) for a developer session token.
        using var http = new HttpClient();
        var reg = await http.PostAsJsonAsync($"{baseUrl}/v1/auth/register",
            new { name = "SDK Contract CS", email = $"sdk-cs-{Uniq()}@studio.test", password = "contract-test-password" });
        Assert.Equal(201, (int)reg.StatusCode);
        var session = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetProperty("token").GetString()!;

        var mgmt = new GameBankManagement(new GameBankOptions { ApiKey = session, BaseUrl = baseUrl });
        Assert.Equal("SDK Contract CS", (await mgmt.MeAsync()).Name);
        var game = await mgmt.Games.CreateAsync($"CS Game {Uniq()}");
        var key = await mgmt.ApiKeys.IssueAsync(game.Id, new IssueApiKeyParams { Environment = "sandbox", Label = "contract", Scopes = Scopes.All.ToList() });
        Assert.StartsWith("gbe_test_sk_", key.Secret);
        var bank = await mgmt.Game(game.Id, "sandbox").Banks.CreateAsync(new CreateBankParams { Name = "Eko Trust Bank", BaseCurrency = "NGN" });
        Assert.Equal(8, bank.SystemAccounts!.Count); // incl. levies_payable
        var readOnly = await mgmt.ApiKeys.IssueAsync(game.Id, new IssueApiKeyParams { Environment = "sandbox", Scopes = [Scopes.AccountRead] });

        var seen = new List<ResponseInfo>();
        var gb = new GameBankClient(new GameBankOptions { ApiKey = key.Secret, BaseUrl = baseUrl, OnResponse = seen.Add });
        Assert.Equal("sandbox", gb.Environment);

        var ada = await gb.Players.UpsertAsync(new UpsertPlayerParams { ExternalPlayerId = $"ada-{Uniq()}", DisplayName = "Ada Obi" });
        Assert.Equal(ada.Id, (await gb.Players.UpsertAsync(new UpsertPlayerParams { ExternalPlayerId = ada.ExternalPlayerId })).Id);
        var tunde = await gb.Players.UpsertAsync(new UpsertPlayerParams { ExternalPlayerId = $"tunde-{Uniq()}", DisplayName = "Tunde Bello" });
        var adaAcct = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bank.Id, PlayerId = ada.Id });
        var tundeAcct = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bank.Id, PlayerId = tunde.Id });

        var reference = $"pay-{Uniq()}";
        var salary = new FundingParams { AccountId = adaAcct.Id, AmountMinor = 10_000_00, Reason = "salary", Reference = reference };
        var keyed = new RequestOptions { IdempotencyKey = $"fund-{reference}" };
        var funded = await gb.Funding.CreateAsync(salary, keyed);
        Assert.Equal(10_000_00, funded.Balance.PostedBalance);
        var replay = await gb.Funding.CreateAsync(salary, keyed);
        Assert.Equal(funded.Transaction.Id, replay.Transaction.Id);
        Assert.True(seen[^1].Replayed);
        var reused = await Assert.ThrowsAsync<ConflictException>(() => gb.Funding.CreateAsync(salary with { AmountMinor = 1 }, keyed));
        Assert.Equal("idempotency_key_reused", reused.Code);

        Assert.Equal("Tunde Bello", (await gb.NameEnquiryAsync(bank.BankCode, tundeAcct.AccountNumber)).AccountName);
        var t = await gb.Transfers.CreateAsync(new TransferParams
        { SourceAccountId = adaAcct.Id, DestinationAccountNumber = tundeAcct.AccountNumber, AmountMinor = 2_500_00, Narration = "Generator" });
        Assert.Equal(7_500_00, t.SourceBalance.PostedBalance);
        Assert.Equal(2_500_00, t.DestinationBalance!.PostedBalance);

        var nsf = await Assert.ThrowsAsync<InsufficientFundsException>(() =>
            gb.Transfers.CreateAsync(new TransferParams { SourceAccountId = tundeAcct.Id, DestinationAccountId = adaAcct.Id, AmountMinor = 99_999_00 }));
        Assert.Equal(2_500_00L, nsf.Details["available_balance"]);
        Assert.False(string.IsNullOrEmpty(nsf.RequestId));
        await Assert.ThrowsAsync<NotFoundException>(() => gb.Accounts.GetAsync("00000000-0000-0000-0000-000000000000"));
        var scope = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            new GameBankClient(new GameBankOptions { ApiKey = readOnly.Secret, BaseUrl = baseUrl })
                .Transfers.CreateAsync(new TransferParams { SourceAccountId = adaAcct.Id, DestinationAccountId = tundeAcct.Id, AmountMinor = 1 }));
        Assert.Equal("insufficient_scope", scope.Code);

        var held = await gb.Holds.PlaceAsync(new PlaceHoldParams { AccountId = adaAcct.Id, AmountMinor = 3_000_00, Reason = "Order #1", DestinationAccountId = tundeAcct.Id });
        Assert.Equal(7_500_00, held.Balance.PostedBalance);
        Assert.Equal(4_500_00, held.Balance.AvailableBalance);
        Assert.Equal(0, held.Hold.ReservedFeeMinor);
        var cap = await gb.Holds.CaptureAsync(held.Hold.Id, new CaptureHoldParams { AmountMinor = 2_000_00 });
        Assert.Equal("captured", cap.Hold.Status);
        Assert.Equal(5_500_00, cap.SourceBalance.AvailableBalance);

        var lossyHandler = new LossyHandler();
        var lossy = new GameBankClient(new GameBankOptions { ApiKey = key.Secret, BaseUrl = baseUrl, HttpClient = new HttpClient(lossyHandler) });
        var lost = await lossy.Transfers.CreateAsync(new TransferParams { SourceAccountId = adaAcct.Id, DestinationAccountId = tundeAcct.Id, AmountMinor = 500_00 });
        Assert.True(lossyHandler.Dropped);
        Assert.Equal(5_000_00, lost.SourceBalance.PostedBalance);
        Assert.Equal(5_000_00, (await gb.Accounts.BalanceAsync(adaAcct.Id)).PostedBalance);

        var amounts = new List<long>();
        await foreach (var line in gb.Accounts.IterateTransactionsAsync(adaAcct.Id, pageSize: 2)) amounts.Add(line.AmountMinor);
        Assert.Equal(new long[] { 500_00, 2_000_00, 2_500_00, 10_000_00 }, amounts);

        var rev = await gb.Transactions.ReverseAsync(t.Transaction.Id, new ReverseParams { Reason = "customer dispute" });
        Assert.Equal("reversal", rev.Type);
        Assert.Equal("reversed", (await gb.Transactions.GetAsync(t.Transaction.Id)).Status);

        // Fees: a ₦50 + 1% transfer fee (max ₦500) via the developer credential, a levy via the game key.
        var cfg = mgmt.Game(game.Id, "sandbox");
        var fee = await cfg.Fees.CreateAsync(bank.Id, new CreateFeeRuleParams
        {
            Code = "transfer_fee", Name = "Transfer fee", Event = FeeEvents.Transfer, Model = FeeModels.Combined,
            FixedMinor = 50_00, PercentageBps = 100, MaxFeeMinor = 500_00,
        });
        Assert.Equal("in_effect", fee.State);
        Assert.Equal("fee_income", fee.CreditAccount);
        var levy = await gb.Fees.CreateAsync(bank.Id, new CreateFeeRuleParams
        {
            Code = "stamp_duty", Name = "Stamp duty", Event = FeeEvents.Transfer, Kind = FeeKinds.Levy, Model = FeeModels.Fixed,
            FixedMinor = 50_00, MinAmountMinor = 10_000_00,
        });
        Assert.Equal("levies_payable", levy.CreditAccount);
        Assert.Equal(2, (await gb.Fees.ListAsync(bank.Id)).Count);

        var quote = await gb.Fees.QuoteAsync(new QuoteFeesParams { BankId = bank.Id, AmountMinor = 1_000_00 });
        Assert.Equal(60_00, quote.FeesTotalMinor);
        var before = (await gb.Accounts.BalanceAsync(adaAcct.Id)).PostedBalance;
        var pay = new TransferParams { SourceAccountId = adaAcct.Id, DestinationAccountId = tundeAcct.Id, AmountMinor = 1_000_00, MaxFeeMinor = quote.FeesTotalMinor };
        var paid = await gb.Transfers.CreateAsync(pay);
        Assert.Equal(60_00, paid.FeesTotalMinor);
        Assert.Equal(1_060_00, paid.TotalDebitMinor);
        Assert.Equal(before - 1_060_00, paid.SourceBalance.PostedBalance);
        Assert.Equal(new[] { "fee", "fee", "principal", "principal" }, paid.Transaction.Entries.Select(e => e.Kind).OrderBy(k => k));
        var latest = (await gb.Accounts.TransactionsAsync(adaAcct.Id, 2)).Data;
        Assert.Contains(latest, l => l.Kind == "fee" && l.Narration == "Transfer fee");

        await cfg.Fees.UpdateAsync(bank.Id, fee.Id, new UpdateFeeRuleParams { FixedMinor = 60_00 });
        var changed = await Assert.ThrowsAsync<FeeChangedException>(() => gb.Transfers.CreateAsync(pay));
        Assert.IsAssignableFrom<ConflictException>(changed);
        Assert.Equal("fee_changed", changed.Code);

        Assert.Equal(60_00, (await gb.Banks.BooksAsync(bank.Id)).FeeIncomeMinor);
        await gb.Transactions.ReverseAsync(paid.Transaction.Id, new ReverseParams { Reason = "refund" });
        Assert.Equal(0, (await gb.Banks.BooksAsync(bank.Id)).FeeIncomeMinor);
        Assert.Equal("inactive", (await cfg.Fees.UpdateAsync(bank.Id, fee.Id, new UpdateFeeRuleParams { Status = "inactive" })).State);

        // Webhooks: a debit alert reaches a real endpoint, signed, and verifies with the SDK helper.
        Assert.Empty(await mgmt.Game(game.Id, "sandbox").Webhooks.Endpoints.ListAsync());
        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            new GameBankClient(new GameBankOptions { ApiKey = readOnly.Secret, BaseUrl = baseUrl }).Webhooks.Endpoints.ListAsync());
        using (var receiver = new Receiver())
        {
            var ep = await gb.Webhooks.Endpoints.CreateAsync(new CreateWebhookEndpointParams { Url = receiver.Url, EventTypes = [WebhookEventTypes.AccountDebited], Description = "contract" });
            Assert.StartsWith(ep.SecretPrefix, ep.Secret);
            Assert.Null((await gb.Webhooks.Endpoints.GetAsync(ep.Id)).Secret);
            var probe = await gb.Webhooks.Endpoints.TestAsync(ep.Id);
            Assert.True(probe.State == "delivered", probe.LastError);

            var sent = await gb.Transfers.CreateAsync(new TransferParams { SourceAccountId = adaAcct.Id, DestinationAccountId = tundeAcct.Id, AmountMinor = 5_00, Narration = "Webhook check" });
            var hit = await receiver.WaitForAsync(sent.Transaction.Id);
            var ev = WebhookVerifier.VerifyWebhook(hit.Body, hit.Signature, ep.Secret!);
            var alert = ev.Alert();
            Assert.Equal(WebhookEventTypes.AccountDebited, ev.Type);
            Assert.Equal((adaAcct.Id, 5_00L, "Tunde Bello", sent.SourceBalance.PostedBalance),
                (alert.AccountId, alert.TotalMinor, alert.Counterparty?.AccountName, alert.BalanceAfter.PostedMinor));

            var feed = await gb.Webhooks.Events.ListAsync(new ListWebhookEventsParams { Type = WebhookEventTypes.AccountDebited, AccountId = adaAcct.Id, Limit = 1 });
            Assert.Equal(ev.Id, feed.Data.Single().Id);
            Assert.Equal(ev.Id, (await gb.Webhooks.Events.GetAsync(ev.Id)).Id);
            Assert.Equal("delivered", (await gb.Webhooks.Events.DeliveriesAsync(ev.Id)).Single().State);
            var rotated = await gb.Webhooks.Endpoints.RotateSecretAsync(ep.Id);
            Assert.NotEqual(ep.Secret, rotated.Secret);
            Assert.NotNull(rotated.PreviousSecretExpiresAt);
            var delivered = receiver.Got.Count;
            await gb.Webhooks.Events.RedeliverAsync(ev.Id, ep.Id);
            var again = await receiver.WaitForAsync(sent.Transaction.Id, delivered);
            WebhookVerifier.VerifyWebhook(again.Body, again.Signature, rotated.Secret!);
            WebhookVerifier.VerifyWebhook(again.Body, again.Signature, ep.Secret!); // both sign during the overlap

            Assert.Equal("disabled", (await gb.Webhooks.Endpoints.UpdateAsync(ep.Id, new UpdateWebhookEndpointParams { Status = "disabled" })).Status);
            await gb.Webhooks.Endpoints.DeleteAsync(ep.Id);
            await Assert.ThrowsAsync<NotFoundException>(() => gb.Webhooks.Endpoints.GetAsync(ep.Id));
        }

        // Merchants and cards: a shop with a merchant account, a card, a payment, a refund, a lost card.
        var shop = await gb.Merchants.CreateAsync(new CreateMerchantParams { ExternalMerchantId = $"shop-{Uniq()}", Name = "Mama Put", Category = "food" });
        Assert.Equal(shop.Id, (await gb.Merchants.CreateAsync(new CreateMerchantParams { ExternalMerchantId = shop.ExternalMerchantId, Name = "x" })).Id);
        var till = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bank.Id, MerchantId = shop.Id });
        Assert.Equal(("merchant", shop.Id), (till.OwnerType, till.MerchantId));
        await cfg.Banks.UpdateAsync(bank.Id, new UpdateBankParams { FeatureFlags = new() { ["cards"] = true } });
        var program = await cfg.CardPrograms.CreateAsync(bank.Id, new CardProgramParams { Name = "Classic", DailyLimitMinor = 3_000_00 });
        Assert.Equal(6, program.Bin.Length);
        var issued = await gb.Cards.IssueAsync(new IssueCardParams { AccountId = adaAcct.Id, NameOnCard = "Ada Obi" });
        var pan = issued.CardNumber;
        Assert.Equal(pan[..6] + "******" + pan[12..], issued.Card.MaskedNumber);

        var spendable = (await gb.Accounts.BalanceAsync(adaAcct.Id)).AvailableBalance;
        var auth = await gb.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams
        {
            CardNumber = pan, ExpiryMonth = issued.Card.ExpiryMonth, ExpiryYear = issued.Card.ExpiryYear, Cvv = issued.Cvv,
            MerchantAccountNumber = till.AccountNumber, AmountMinor = 200_00, Capture = false,
        });
        Assert.Equal(("approved", spendable - 200_00), (auth.Authorization.Status, auth.Balance.AvailableBalance));
        var captured = await gb.CardAuthorizations.CaptureAsync(auth.Authorization.Id, new CaptureCardParams { AmountMinor = 150_00 });
        Assert.Equal(("card_payment", 150_00L, spendable - 150_00),
            (captured.Transaction.Type, captured.MerchantBalance.PostedBalance, captured.CardBalance.AvailableBalance));
        var refunded = await gb.CardAuthorizations.RefundAsync(auth.Authorization.Id, new RefundCardParams { AmountMinor = 50_00, Reason = "cold" });
        Assert.Equal(("card_refund", 50_00L), (refunded.Transaction.Type, refunded.Authorization.RefundedMinor));

        var instant = await gb.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = issued.Card.Id, MerchantAccountId = till.Id, AmountMinor = 20_00 });
        Assert.Equal(("captured", "card_payment", 120_00L), (instant.Authorization.Status, instant.Transaction?.Type, instant.MerchantBalance?.PostedBalance));
        var pending = await gb.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = issued.Card.Id, MerchantAccountId = till.Id, AmountMinor = 10_00, Capture = false });
        Assert.Equal(CardStatuses.Stolen, (await gb.Cards.ReportLostAsync(issued.Card.Id, "stolen")).Status);
        var refused = await gb.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = issued.Card.Id, MerchantAccountId = till.Id, AmountMinor = 10_00 });
        Assert.True(refused.Authorization.IsDeclined);
        Assert.Equal(DeclineCodes.CardStolen, refused.Authorization.DeclineCode);
        Assert.Equal("captured", (await gb.CardAuthorizations.CaptureAsync(pending.Authorization.Id)).Authorization.Status);
        Assert.Single(await gb.Cards.ListAsync(new ListCardsParams { AccountId = adaAcct.Id })); // no replacement card
        Assert.Equal(4, (await gb.CardAuthorizations.ListAsync(new ListCardAuthorizationsParams { CardId = issued.Card.Id })).Count);

        await gb.Clock.ConfigureAsync(new ConfigureClockParams { Mode = "manual" });
        Assert.NotEmpty((await gb.Clock.AdvanceAsync(new AdvanceClockParams { Days = 1 })).Clock.GameDay);
        var recon = await gb.ReconcileAsync();
        Assert.True(recon.Ok, JsonSerializer.Serialize(recon.Differences));

        // Savings: ₦100,000 at 36.5% earns ₦100 a game day; a fixed deposit can be broken early.
        await cfg.Banks.UpdateAsync(bank.Id, new UpdateBankParams { FeatureFlags = new() { ["savings"] = true } });
        var saver = await cfg.DepositProducts.CreateAsync(bank.Id, new DepositProductParams { Code = "easy_saver", Name = "Easy Saver", Kind = "savings", RateTiers = [new(0, 3650)] });
        var term = await cfg.DepositProducts.CreateAsync(bank.Id, new DepositProductParams
        {
            Code = "term", Name = "Term Deposit", Kind = "fixed_deposit", Terms = [new(30, 3650)], EarlyForfeitBps = 10_000, EarlyPenaltyFixedMinor = 100_00,
        });
        var savings = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bank.Id, PlayerId = ada.Id, ProductId = saver.Id });
        Assert.Equal("savings", savings.AccountType);
        await gb.Funding.CreateAsync(new FundingParams { AccountId = savings.Id, AmountMinor = 100_000_00, Reason = "savings", Reference = $"save-{Uniq()}" });
        await gb.Funding.CreateAsync(new FundingParams { AccountId = adaAcct.Id, AmountMinor = 100_000_00, Reason = "deposit", Reference = $"fd-{Uniq()}" });
        var fd = await gb.FixedDeposits.OpenAsync(new OpenFixedDepositParams { SourceAccountId = adaAcct.Id, ProductId = term.Id, TermDays = 30, AmountMinor = 100_000_00 });
        Assert.Equal(3_000_00, (await gb.DepositProducts.QuoteAsync(new DepositQuoteParams { ProductId = term.Id, AmountMinor = 100_000_00, TermDays = 30 })).GrossInterestMinor);
        await gb.Clock.AdvanceAsync(new AdvanceClockParams { Days = 2 });
        Assert.Equal(200_00, (await gb.Accounts.InterestAsync(savings.Id)).AccruedInterestMinor);
        var bq = await gb.FixedDeposits.BreakQuoteAsync(fd.FixedDeposit.Id);
        Assert.Equal(100_000_00 - 100_00, bq.PayoutMinor);
        await Assert.ThrowsAsync<PenaltyChangedException>(() => gb.FixedDeposits.BreakAsync(fd.FixedDeposit.Id, 50_00));
        Assert.Equal("broken", (await gb.FixedDeposits.BreakAsync(fd.FixedDeposit.Id, bq.PenaltyMinor)).FixedDeposit.Status);

        // Renaming and deleting a game. Deleting revokes its keys at once.
        Assert.Equal("CS Game renamed", (await mgmt.Games.UpdateAsync(game.Id, new UpdateGameParams { Name = "CS Game renamed" })).Name);
        await mgmt.Games.DeleteAsync(game.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => mgmt.Games.GetAsync(game.Id));
        await Assert.ThrowsAsync<AuthenticationException>(() => gb.Clock.GetAsync());
    }
}
