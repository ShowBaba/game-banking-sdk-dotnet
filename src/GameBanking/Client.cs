using System.Runtime.CompilerServices;

namespace GameBanking;

internal static class Ops
{
    internal static string Id(string id) => Uri.EscapeDataString(id);

    internal static void CheckAmount(long? amount, bool required)
    {
        if ((required && amount is null or <= 0) || amount is < 0)
            throw new InvalidRequestException(0, "validation_error", "AmountMinor must be a positive integer number of minor units", null, null, null, null);
    }
}

internal sealed record DataList<T>(List<T> Data);
internal sealed record ApiKeyEnvelope(ApiKey ApiKey);

/// <summary>
/// Runtime client for game servers, authenticated with a game API key. Keep the key on your servers;
/// never ship it in a game client.
/// </summary>
/// <example><code>
/// var gb = new GameBankClient(new GameBankOptions { ApiKey = key, BaseUrl = "https://engine.example.com" });
/// var r = await gb.Transfers.CreateAsync(new TransferParams { SourceAccountId = a, DestinationAccountNumber = n, AmountMinor = 1000 });
/// </code></example>
public sealed class GameBankClient
{
    private readonly Transport _t;

    /// <summary>"sandbox" or "production", read from the API key's prefix.</summary>
    public string? Environment => _t.Environment;
    public PlayersResource Players { get; }
    public AccountsResource Accounts { get; }
    public FundingResource Funding { get; }
    public TransfersResource Transfers { get; }
    public HoldsResource Holds { get; }
    public TransactionsResource Transactions { get; }
    public BanksResource Banks { get; }
    public CurrenciesResource Currencies { get; }
    public ClockResource Clock { get; }

    public GameBankClient(GameBankOptions? options = null) : this(new Transport(options ?? new GameBankOptions())) { }

    internal GameBankClient(Transport t)
    {
        _t = t;
        Players = new(t); Accounts = new(t); Funding = new(t); Transfers = new(t); Holds = new(t);
        Transactions = new(t); Banks = new(t); Currencies = new(t); Clock = new(t);
    }

    internal Transport Transport => _t;

    /// <summary>Confirms a recipient before a transfer: returns only the account name and status.</summary>
    public Task<NameEnquiry> NameEnquiryAsync(string bankCode, string accountNumber, RequestOptions? options = null, CancellationToken ct = default) =>
        _t.SendAsync<NameEnquiry>(new(HttpMethod.Get, "/v1/name-enquiry")
        { Query = new Dictionary<string, string?> { ["bank_code"] = bankCode, ["account_number"] = accountNumber } }, options, ct);

    /// <summary>Compares stored balances with the ledger for this game and environment.</summary>
    public Task<ReconciliationReport> ReconcileAsync(RequestOptions? options = null, CancellationToken ct = default) =>
        _t.SendAsync<ReconciliationReport>(new(HttpMethod.Get, "/v1/reconciliation"), options, ct);
}

public sealed class PlayersResource
{
    private readonly Transport _t;
    internal PlayersResource(Transport t) => _t = t;

    /// <summary>Maps your player ID to an engine player; repeat calls return the same player.</summary>
    public Task<Player> UpsertAsync(UpsertPlayerParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Player>(new(HttpMethod.Post, "/v1/players") { Body = p, RetrySafe = true }, o, ct);

    public Task<Player> GetAsync(string playerId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Player>(new(HttpMethod.Get, $"/v1/players/{Ops.Id(playerId)}"), o, ct);

    /// <summary>Looks a player up by your own player ID.</summary>
    public Task<Player> FindByExternalIdAsync(string externalPlayerId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Player>(new(HttpMethod.Get, "/v1/players") { Query = new Dictionary<string, string?> { ["external_player_id"] = externalPlayerId } }, o, ct);
}

public sealed class AccountsResource
{
    private readonly Transport _t;
    internal AccountsResource(Transport t) => _t = t;

    public Task<Account> OpenAsync(OpenAccountParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Account>(new(HttpMethod.Post, "/v1/accounts") { Body = p }, o, ct);

    public Task<Account> GetAsync(string accountId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Account>(new(HttpMethod.Get, $"/v1/accounts/{Ops.Id(accountId)}"), o, ct);

    public async Task<List<Account>> ListAsync(ListAccountsParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        var q = new Dictionary<string, string?>
        {
            ["bank_id"] = p.BankId, ["player_id"] = p.PlayerId,
            ["include_system"] = p.IncludeSystem ? "true" : null, ["limit"] = p.Limit?.ToString(),
        };
        return (await _t.SendAsync<DataList<Account>>(new(HttpMethod.Get, "/v1/accounts") { Query = q }, o, ct).ConfigureAwait(false)).Data;
    }

    /// <summary>Freezes (no debits), unfreezes ("active") or closes an account. Closing needs a zero balance.</summary>
    public Task<Account> UpdateStatusAsync(string accountId, UpdateAccountStatusParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Account>(new(HttpMethod.Patch, $"/v1/accounts/{Ops.Id(accountId)}") { Body = p }, o, ct);

    public Task<Balance> BalanceAsync(string accountId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Balance>(new(HttpMethod.Get, $"/v1/accounts/{Ops.Id(accountId)}/balance"), o, ct);

    /// <summary>One page of history, newest first. Pass NextCursor back as cursor for the next page.</summary>
    public Task<HistoryPage> TransactionsAsync(string accountId, int? limit = null, string? cursor = null, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<HistoryPage>(new(HttpMethod.Get, $"/v1/accounts/{Ops.Id(accountId)}/transactions")
        { Query = new Dictionary<string, string?> { ["limit"] = limit?.ToString(), ["cursor"] = cursor } }, o, ct);

    /// <summary>Every history line, newest first, fetching pages as needed: <c>await foreach (var line in gb.Accounts.IterateTransactionsAsync(id))</c>.</summary>
    public async IAsyncEnumerable<HistoryItem> IterateTransactionsAsync(string accountId, int? pageSize = null, RequestOptions? o = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? cursor = null;
        do
        {
            var page = await TransactionsAsync(accountId, pageSize, cursor, o, ct).ConfigureAwait(false);
            foreach (var item in page.Data) yield return item;
            cursor = page.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));
    }

    /// <summary>An account's holds; status may be null for all.</summary>
    public async Task<List<Hold>> HoldsAsync(string accountId, string? status = null, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<Hold>>(new(HttpMethod.Get, $"/v1/accounts/{Ops.Id(accountId)}/holds")
        { Query = new Dictionary<string, string?> { ["status"] = status } }, o, ct).ConfigureAwait(false)).Data;
}

public sealed class FundingResource
{
    private readonly Transport _t;
    internal FundingResource(Transport t) => _t = t;

    /// <summary>Credits a player account from the bank's issuance account. Needs the funding:create scope.</summary>
    public Task<FundingResult> CreateAsync(FundingParams p, RequestOptions? o = null, CancellationToken ct = default)
    {
        Ops.CheckAmount(p.AmountMinor, true);
        return _t.SendAsync<FundingResult>(new(HttpMethod.Post, "/v1/funding") { Body = p, Idempotent = true }, o, ct);
    }
}

public sealed class TransfersResource
{
    private readonly Transport _t;
    internal TransfersResource(Transport t) => _t = t;

    /// <summary>Moves money between two customer accounts in the same bank, atomically.</summary>
    public Task<TransferResult> CreateAsync(TransferParams p, RequestOptions? o = null, CancellationToken ct = default)
    {
        Ops.CheckAmount(p.AmountMinor, true);
        return _t.SendAsync<TransferResult>(new(HttpMethod.Post, "/v1/transfers") { Body = p, Idempotent = true }, o, ct);
    }
}

public sealed class HoldsResource
{
    private readonly Transport _t;
    internal HoldsResource(Transport t) => _t = t;

    /// <summary>Reserves funds: available balance drops, posted balance does not.</summary>
    public Task<HoldResult> PlaceAsync(PlaceHoldParams p, RequestOptions? o = null, CancellationToken ct = default)
    {
        Ops.CheckAmount(p.AmountMinor, true);
        return _t.SendAsync<HoldResult>(new(HttpMethod.Post, "/v1/holds") { Body = p, Idempotent = true }, o, ct);
    }

    public Task<Hold> GetAsync(string holdId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Hold>(new(HttpMethod.Get, $"/v1/holds/{Ops.Id(holdId)}"), o, ct);

    /// <summary>Posts all (AmountMinor null) or part of a hold as a transfer and releases the rest.</summary>
    public Task<CaptureResult> CaptureAsync(string holdId, CaptureHoldParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        Ops.CheckAmount(p.AmountMinor, false);
        return _t.SendAsync<CaptureResult>(new(HttpMethod.Post, $"/v1/holds/{Ops.Id(holdId)}/capture") { Body = p, Idempotent = true }, o, ct);
    }

    /// <summary>Cancels a hold and restores the available balance.</summary>
    public Task<HoldResult> ReleaseAsync(string holdId, string? reason = null, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<HoldResult>(new(HttpMethod.Post, $"/v1/holds/{Ops.Id(holdId)}/release")
        { Body = new Dictionary<string, string?> { ["reason"] = reason }.Where(kv => kv.Value is not null).ToDictionary(), Idempotent = true }, o, ct);
}

public sealed class TransactionsResource
{
    private readonly Transport _t;
    internal TransactionsResource(Transport t) => _t = t;

    public Task<Transaction> GetAsync(string transactionId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Transaction>(new(HttpMethod.Get, $"/v1/transactions/{Ops.Id(transactionId)}"), o, ct);

    /// <summary>Posts a compensating transaction. Needs the transaction:reverse scope.</summary>
    public Task<Transaction> ReverseAsync(string transactionId, ReverseParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Transaction>(new(HttpMethod.Post, $"/v1/transactions/{Ops.Id(transactionId)}/reverse") { Body = p, Idempotent = true }, o, ct);
}

public sealed class BanksResource
{
    private readonly Transport _t;
    internal BanksResource(Transport t) => _t = t;

    /// <summary>Creates a bank with its own system ledger accounts.</summary>
    public Task<Bank> CreateAsync(CreateBankParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Bank>(new(HttpMethod.Post, "/v1/banks") { Body = p }, o, ct);

    public async Task<List<Bank>> ListAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<Bank>>(new(HttpMethod.Get, "/v1/banks"), o, ct).ConfigureAwait(false)).Data;

    public Task<Bank> GetAsync(string bankId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Bank>(new(HttpMethod.Get, $"/v1/banks/{Ops.Id(bankId)}"), o, ct);

    public Task<Bank> UpdateAsync(string bankId, UpdateBankParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Bank>(new(HttpMethod.Patch, $"/v1/banks/{Ops.Id(bankId)}") { Body = p }, o, ct);

    public Task<Limits> LimitsAsync(string bankId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Limits>(new(HttpMethod.Get, $"/v1/banks/{Ops.Id(bankId)}/limits"), o, ct);

    /// <summary>Sets caps per scope (see <see cref="LimitScopes"/>); a null policy removes that scope's limits. Needs limits:write.</summary>
    public Task<Limits> SetLimitsAsync(string bankId, IReadOnlyDictionary<string, LimitPolicy?> limits, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Limits>(new(HttpMethod.Put, $"/v1/banks/{Ops.Id(bankId)}/limits") { Body = limits }, o, ct);
}

public sealed class CurrenciesResource
{
    private readonly Transport _t;
    internal CurrenciesResource(Transport t) => _t = t;

    /// <summary>Standard ISO currencies plus this game's custom units.</summary>
    public async Task<List<Currency>> ListAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<Currency>>(new(HttpMethod.Get, "/v1/currencies"), o, ct).ConfigureAwait(false)).Data;

    /// <summary>Defines a custom virtual unit, e.g. Code "GOLD", Exponent 0.</summary>
    public Task<Currency> CreateAsync(CreateCurrencyParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Currency>(new(HttpMethod.Post, "/v1/currencies") { Body = p }, o, ct);
}

public sealed class ClockResource
{
    private readonly Transport _t;
    internal ClockResource(Transport t) => _t = t;

    public Task<Clock> GetAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Clock>(new(HttpMethod.Get, "/v1/clock"), o, ct);

    /// <summary>Changes mode, speed or time zone, or jumps forward. Needs limits:write.</summary>
    public Task<Clock> ConfigureAsync(ConfigureClockParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Clock>(new(HttpMethod.Put, "/v1/clock") { Body = p }, o, ct);

    /// <summary>Moves a manual clock forward and runs the jobs that became due.</summary>
    public Task<AdvanceClockResult> AdvanceAsync(AdvanceClockParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<AdvanceClockResult>(new(HttpMethod.Post, "/v1/clock/advance") { Body = p }, o, ct);
}

/// <summary>Setup and CI client, authenticated with a developer credential (gbe_dev_…) or a sign-in session token.</summary>
public sealed class GameBankManagement
{
    private readonly Transport _t;
    public GamesResource Games { get; }
    public ApiKeysResource ApiKeys { get; }
    public DeveloperCredentialsResource Credentials { get; }

    public GameBankManagement(GameBankOptions? options = null)
    {
        _t = new Transport(options ?? new GameBankOptions());
        Games = new(_t); ApiKeys = new(_t); Credentials = new(_t);
    }

    /// <summary>The developer this credential belongs to.</summary>
    public Task<Developer> MeAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Developer>(new(HttpMethod.Get, "/v1/developer"), o, ct);

    public async Task<List<AuditLog>> AuditLogsAsync(string gameId, string? environment = null, int? limit = null, DateTimeOffset? before = null,
        RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<AuditLog>>(new(HttpMethod.Get, $"/v1/games/{Ops.Id(gameId)}/audit-logs")
        {
            Query = new Dictionary<string, string?> { ["environment"] = environment, ["limit"] = limit?.ToString(), ["before"] = before?.UtcDateTime.ToString("O") },
        }, o, ct).ConfigureAwait(false)).Data;

    /// <summary>
    /// A client acting on one of your games with your developer credential, to configure banks, currencies,
    /// limits and the clock. Moving money still needs a game API key.
    /// </summary>
    public GameBankClient Game(string gameId, string environment) =>
        new(_t.WithHeaders(new Dictionary<string, string> { ["X-Game-Id"] = gameId, ["X-Environment"] = environment }));
}

public sealed class GamesResource
{
    private readonly Transport _t;
    internal GamesResource(Transport t) => _t = t;

    public Task<Game> CreateAsync(string name, string? slug = null, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Game>(new(HttpMethod.Post, "/v1/games") { Body = new Dictionary<string, string?> { ["name"] = name, ["slug"] = slug }.Where(kv => kv.Value is not null).ToDictionary() }, o, ct);

    public async Task<List<Game>> ListAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<Game>>(new(HttpMethod.Get, "/v1/games"), o, ct).ConfigureAwait(false)).Data;

    public Task<Game> GetAsync(string gameId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Game>(new(HttpMethod.Get, $"/v1/games/{Ops.Id(gameId)}"), o, ct);

    /// <summary>Suspends or reactivates a game ("active" or "suspended").</summary>
    public Task<Game> SetStatusAsync(string gameId, string status, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Game>(new(HttpMethod.Patch, $"/v1/games/{Ops.Id(gameId)}") { Body = new Dictionary<string, string> { ["status"] = status } }, o, ct);
}

public sealed class ApiKeysResource
{
    private readonly Transport _t;
    internal ApiKeysResource(Transport t) => _t = t;

    /// <summary>Issues a game API key. Secret is set in the result once; store it on your servers.</summary>
    public async Task<ApiKey> IssueAsync(string gameId, IssueApiKeyParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<ApiKeyEnvelope>(new(HttpMethod.Post, $"/v1/games/{Ops.Id(gameId)}/api-keys") { Body = p }, o, ct).ConfigureAwait(false)).ApiKey;

    public async Task<List<ApiKey>> ListAsync(string gameId, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<ApiKey>>(new(HttpMethod.Get, $"/v1/games/{Ops.Id(gameId)}/api-keys"), o, ct).ConfigureAwait(false)).Data;

    /// <summary>Revokes a key immediately.</summary>
    public Task RevokeAsync(string gameId, string keyId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync(new(HttpMethod.Delete, $"/v1/games/{Ops.Id(gameId)}/api-keys/{Ops.Id(keyId)}"), o, ct);
}

public sealed class DeveloperCredentialsResource
{
    private readonly Transport _t;
    internal DeveloperCredentialsResource(Transport t) => _t = t;

    public async Task<List<DeveloperCredential>> ListAsync(RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<DeveloperCredential>>(new(HttpMethod.Get, "/v1/developer/credentials"), o, ct).ConfigureAwait(false)).Data;

    /// <summary>Issues a long-lived developer credential for scripts and CI.</summary>
    public Task<DeveloperCredential> IssueAsync(string? label = null, DateTimeOffset? expiresAt = null, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<DeveloperCredential>(new(HttpMethod.Post, "/v1/developer/credentials")
        { Body = new Dictionary<string, object?> { ["label"] = label, ["expires_at"] = expiresAt }.Where(kv => kv.Value is not null).ToDictionary() }, o, ct);

    public Task RevokeAsync(string credentialId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync(new(HttpMethod.Delete, $"/v1/developer/credentials/{Ops.Id(credentialId)}"), o, ct);
}
