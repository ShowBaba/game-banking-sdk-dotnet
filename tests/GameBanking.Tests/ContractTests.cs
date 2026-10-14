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

        await gb.Clock.ConfigureAsync(new ConfigureClockParams { Mode = "manual" });
        Assert.NotEmpty((await gb.Clock.AdvanceAsync(new AdvanceClockParams { Days = 1 })).Clock.GameDay);
        var recon = await gb.ReconcileAsync();
        Assert.True(recon.Ok, JsonSerializer.Serialize(recon.Differences));

        // Renaming and deleting a game. Deleting revokes its keys at once.
        Assert.Equal("CS Game renamed", (await mgmt.Games.UpdateAsync(game.Id, new UpdateGameParams { Name = "CS Game renamed" })).Name);
        await mgmt.Games.DeleteAsync(game.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => mgmt.Games.GetAsync(game.Id));
        await Assert.ThrowsAsync<AuthenticationException>(() => gb.Clock.GetAsync());
    }
}
