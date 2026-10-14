# GameBanking (C# / .NET)

Server-side SDK for the Game Banking Engine. .NET 8, no dependencies beyond the framework.

> Game API keys move money. Use this SDK on your game servers only, never in a Unity or other game client build.

## Install

```bash
dotnet add package GameBanking
```

## Quick start

```csharp
using GameBanking;

var gb = new GameBankClient(new GameBankOptions
{
    ApiKey = Environment.GetEnvironmentVariable("GBE_API_KEY"),
    BaseUrl = "https://your-engine.example.com",
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
