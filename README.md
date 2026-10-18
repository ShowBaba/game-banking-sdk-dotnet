# GameBanking (C# / .NET)

Server-side SDK for the Game Banking Engine. .NET 8, no dependencies beyond the framework.

> Game API keys move money. Use this SDK on your game servers only, never in a Unity or other game client build.
>
> In-game money only: the Game Banking Engine simulates banking for game currencies. It holds no real money and isn't a regulated financial service, so don't use it for balances players can cash out or exchange for real money.

## Install

From [NuGet](https://www.nuget.org/packages/GameBanking/):

```bash
dotnet add package GameBanking
```

## Quick start

```csharp
using GameBanking;

var gb = new GameBankClient(new GameBankOptions
{
    ApiKey = Environment.GetEnvironmentVariable("GBE_API_KEY"),
    BaseUrl = "https://api.gamebank.dev",
});

var player = await gb.Players.UpsertAsync(new UpsertPlayerParams { ExternalPlayerId = "user-42", DisplayName = "Ada Obi" });
var account = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bankId, PlayerId = player.Id });
await gb.Funding.CreateAsync(
    new FundingParams { AccountId = account.Id, AmountMinor = 500_000, Reason = "salary", Reference = "payroll-2040-06-01-user-42" },
    new RequestOptions { IdempotencyKey = "payroll-2040-06-01-user-42" }); // your stable ID: retrying never pays twice

try
{
    await gb.Transfers.CreateAsync(new TransferParams { SourceAccountId = account.Id, DestinationAccountNumber = "5915945174", AmountMinor = 1_000 });
}
catch (InsufficientFundsException e)
{
    Console.WriteLine($"available {e.Details["available_balance"]} (request {e.RequestId})");
}

await foreach (var line in gb.Accounts.IterateTransactionsAsync(account.Id))
    Console.WriteLine($"{line.Direction} {line.AmountDisplay} {line.Narration}");
```

Every method is async and takes an optional `CancellationToken`. Pass your own `HttpClient` (for example from `IHttpClientFactory`) with `GameBankOptions.HttpClient`.

## Fees and charges

```csharp
// Show the player what the transfer costs, then cap the fee at what they saw.
var quote = await gb.Fees.QuoteAsync(new QuoteFeesParams { BankId = bankId, AmountMinor = 2_000_00 });
try
{
    var r = await gb.Transfers.CreateAsync(new TransferParams
    {
        SourceAccountId = account.Id, DestinationAccountNumber = "5915945174",
        AmountMinor = 2_000_00, MaxFeeMinor = quote.FeesTotalMinor,
    });
    Console.WriteLine($"{r.FeesTotalMinor} {r.TotalDebitMinor}"); // each fee is its own ledger and statement line
}
catch (FeeChangedException) { /* the schedule changed: quote again */ }

// Configure fees (bank:write): ₦50 + 1%, capped at ₦500, on transfers of ₦1,000 or more.
await gb.Fees.CreateAsync(bankId, new CreateFeeRuleParams
{
    Code = "transfer_fee", Name = "Transfer fee", Event = FeeEvents.Transfer, Model = FeeModels.Combined,
    FixedMinor = 50_00, PercentageBps = 100, MaxFeeMinor = 500_00, MinAmountMinor = 1_000_00,
});
var books = await gb.Banks.BooksAsync(bankId); // FeeIncomeMinor, LeviesPayableMinor
```

## Merchants and cards

```csharp
// A shop and its merchant account (card payments settle here).
var shop = await gb.Merchants.CreateAsync(new CreateMerchantParams { ExternalMerchantId = "shop:mama-put", Name = "Mama Put", Category = "food" });
var till = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bankId, MerchantId = shop.Id });

// Once per bank: turn on cards and add a program (bank:write).
await gb.Banks.UpdateAsync(bankId, new UpdateBankParams { FeatureFlags = new() { ["cards"] = true } });
await gb.CardPrograms.CreateAsync(bankId, new CardProgramParams { Name = "Classic" });

// Issue a card: CardNumber and Cvv are shown only now.
var issued = await gb.Cards.IssueAsync(new IssueCardParams { AccountId = accountId });

// Pay: an approved payment is charged at once (status "captured"). A decline is a result, not an exception.
var paid = await gb.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = issued.Card.Id, MerchantAccountId = till.Id, AmountMinor = 2_500_00 });
if (paid.Authorization.IsDeclined) ShowPlayer(paid.Authorization.DeclineCode); // e.g. DeclineCodes.InsufficientFunds

// Or hold first when the final amount or the sale isn't settled yet (a fuel pump, a pre-order).
var held = await gb.CardAuthorizations.AuthorizeAsync(new AuthorizeCardParams { CardId = issued.Card.Id, MerchantAccountId = till.Id, AmountMinor = 2_500_00, Capture = false });
await gb.CardAuthorizations.CaptureAsync(held.Authorization.Id, new CaptureCardParams { AmountMinor = 2_100_00 }); // or ReleaseAsync; RefundAsync later, in parts

await gb.Cards.ReportLostAsync(issued.Card.Id, "stolen"); // final; no replacement is issued
```

## Savings and fixed deposits

```csharp
// Once per bank: turn savings on and define products (bank:write).
await gb.Banks.UpdateAsync(bankId, new UpdateBankParams { FeatureFlags = new() { ["savings"] = true } });
var saver = await gb.DepositProducts.CreateAsync(bankId, new DepositProductParams { Code = "easy_saver", Name = "Easy Saver", Kind = "savings", RateTiers = [new(0, 300)] });
var term = await gb.DepositProducts.CreateAsync(bankId, new DepositProductParams { Code = "term", Name = "Term Deposit", Kind = "fixed_deposit", Terms = [new(90, 800)] });

// A savings account earns daily on the game clock and is paid monthly.
var savings = await gb.Accounts.OpenAsync(new OpenAccountParams { BankId = bankId, PlayerId = playerId, ProductId = saver.Id });
var position = await gb.Accounts.InterestAsync(savings.Id); // AccruedInterestMinor, NextPayoutDay, Payouts…

// A fixed deposit (deposits:write); break it early with a quote first.
var fd = await gb.FixedDeposits.OpenAsync(new OpenFixedDepositParams { SourceAccountId = accountId, ProductId = term.Id, TermDays = 90, AmountMinor = 50_000_00 });
var quote = await gb.FixedDeposits.BreakQuoteAsync(fd.FixedDeposit.Id);
await gb.FixedDeposits.BreakAsync(fd.FixedDeposit.Id, quote.PenaltyMinor); // PenaltyChangedException if it rose
```

## Webhooks: debit and credit alerts

Every money movement on a player's account sends a signed `account.debited` or `account.credited` event to your endpoint. Verify it with the **raw** body:

```csharp
// Register once (webhooks:write). The secret is shown only here.
var ep = await gb.Webhooks.Endpoints.CreateAsync(new CreateWebhookEndpointParams
{
    Url = "https://game.example.com/gbe/webhooks",
    EventTypes = [WebhookEventTypes.AccountDebited, WebhookEventTypes.AccountCredited],
});

app.MapPost("/gbe/webhooks", async (HttpRequest req) => // ASP.NET Core minimal API
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    WebhookEvent ev;
    try { ev = WebhookVerifier.VerifyWebhook(ms.ToArray(), req.Headers[WebhookVerifier.SignatureHeader], secret); }
    catch (WebhookVerificationException) { return Results.BadRequest(); }
    if (ev.Type == WebhookEventTypes.AccountDebited)
    {
        var a = ev.Alert(); // AmountMinor, Charges, TotalMinor, BalanceAfter, Counterparty, Sequence…
        NotifyPlayer(a.PlayerId, $"-{a.TotalDisplay}");
    }
    return Results.NoContent(); // answer within 10s; delivery is at least once, so dedupe on ev.Id
});
```

`gb.Webhooks.Endpoints` also has `ListAsync`, `GetAsync`, `UpdateAsync` (Status `"disabled"` / `"active"`), `DeleteAsync`, `RotateSecretAsync` (the old secret keeps signing for 24 hours; pass both secrets to `VerifyWebhook`) and `TestAsync`. `gb.Webhooks.Events` has `ListAsync` / `IterateAsync` (the last 30 days, filterable by type and account, also a polling feed), `GetAsync`, `DeliveriesAsync` and `RedeliverAsync`.

## Setup and CI

```csharp
var mgmt = new GameBankManagement(new GameBankOptions { ApiKey = devCredential, BaseUrl = baseUrl });
var game = await mgmt.Games.CreateAsync("Lagos Hustle");
var key = await mgmt.ApiKeys.IssueAsync(game.Id, new IssueApiKeyParams { Environment = "sandbox" });
Console.WriteLine(key.Secret); // shown once
await mgmt.Game(game.Id, "sandbox").Banks.CreateAsync(new CreateBankParams { Name = "Eko Trust Bank", BaseCurrency = "NGN" });
```

`mgmt.Games.UpdateAsync(id, new UpdateGameParams { ... })` renames or suspends a game; `mgmt.Games.DeleteAsync(id)` deletes it and revokes its keys.

## Behaviour

Retries, idempotency keys and money helpers follow the shared contract in [`CONTRACT.md`](CONTRACT.md). Exceptions use .NET naming: `GameBankException`, `InsufficientFundsException`, `ConnectionException` and so on. `Money.ToMinor("100.50", 2)` and `Money.FormatMinor(10050, 2)` convert amounts without floating point.

## Development

Unit tests need no network:

```bash
dotnet test
```

The contract test runs the full journey against a real engine. `ci/docker-compose.yml` starts one (PostgreSQL plus the engine's published image) on port 8080:

```bash
docker compose -f ci/docker-compose.yml up -d && ./ci/wait-for-engine.sh
GBE_BASE_URL=http://localhost:8080 dotnet test
docker compose -f ci/docker-compose.yml down -v
```

To test against a local engine build instead, build its image and set `GBE_ENGINE_IMAGE` to that tag. CI (`.github/workflows/ci.yml`) runs both on every push.
