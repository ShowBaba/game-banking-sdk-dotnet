using System.Globalization;

namespace GameBanking;

/// <summary>A business in the game: an NPC shop or a player-run store.</summary>
public sealed record Merchant
{
    public string Id { get; init; } = "";
    public string ExternalMerchantId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string? OwnerPlayerId { get; init; }
    /// <summary>"active" or "suspended" (card payments declined).</summary>
    public string Status { get; init; } = "";
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateMerchantParams
{
    /// <summary>Your own ID; creating it again returns the existing merchant.</summary>
    public required string ExternalMerchantId { get; init; }
    public required string Name { get; init; }
    /// <summary>e.g. "food"; default "general".</summary>
    public string? Category { get; init; }
    public string? OwnerPlayerId { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public sealed record UpdateMerchantParams
{
    public string? Name { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public sealed record ListMerchantsParams
{
    public string? Status { get; init; }
    public string? OwnerPlayerId { get; init; }
    public string? ExternalMerchantId { get; init; }
    public int? Limit { get; init; }
}

public sealed record CardProgram
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Bin { get; init; } = "";
    public int ValidityMonths { get; init; }
    /// <summary>How long an authorisation holds funds, in game seconds.</summary>
    public long AuthExpirySeconds { get; init; }
    public long? PerTxnLimitMinor { get; init; }
    public long? DailyLimitMinor { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Creates or updates a program; null fields are unchanged (or defaulted).</summary>
public sealed record CardProgramParams
{
    public string? Name { get; init; }
    /// <summary>Create only: 6 digits, unique in the game.</summary>
    public string? Bin { get; init; }
    public int? ValidityMonths { get; init; }
    public long? AuthExpirySeconds { get; init; }
    public long? PerTxnLimitMinor { get; init; }
    public long? DailyLimitMinor { get; init; }
    public string? Status { get; init; }
    public List<string>? Clear { get; init; }
}

public static class CardStatuses
{
    public const string Active = "active", Blocked = "blocked", Lost = "lost", Stolen = "stolen", Expired = "expired", Closed = "closed";
}

public sealed record Card
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string PlayerId { get; init; } = "";
    public string ProgramId { get; init; } = "";
    public string MaskedNumber { get; init; } = "";
    public string Last4 { get; init; } = "";
    public int ExpiryMonth { get; init; }
    /// <summary>Valid through the end of ExpiryMonth of game time.</summary>
    public int ExpiryYear { get; init; }
    public string NameOnCard { get; init; } = "";
    public string Status { get; init; } = "";
    public string? StatusReason { get; init; }
    /// <summary>The card's own limits; null uses the program's.</summary>
    public long? PerTxnLimitMinor { get; init; }
    public long? DailyLimitMinor { get; init; }
    public long? EffectivePerTxnLimitMinor { get; init; }
    public long? EffectiveDailyLimitMinor { get; init; }
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public DateTimeOffset StatusChangedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record IssueCardParams
{
    /// <summary>A player account.</summary>
    public required string AccountId { get; init; }
    public string? ProgramId { get; init; }
    public string? NameOnCard { get; init; }
    public long? PerTxnLimitMinor { get; init; }
    public long? DailyLimitMinor { get; init; }
    public long? MaxFeeMinor { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

/// <summary>CardNumber and Cvv are shown only here.</summary>
public sealed record IssueCardResult
{
    public Card Card { get; init; } = new();
    public string CardNumber { get; init; } = "";
    public string Cvv { get; init; } = "";
    public List<FeeCharge> Fees { get; init; } = new();
    public long FeesTotalMinor { get; init; }
    public Balance Balance { get; init; } = new();
}

public sealed record UpdateCardParams
{
    public string? NameOnCard { get; init; }
    public long? PerTxnLimitMinor { get; init; }
    public long? DailyLimitMinor { get; init; }
    /// <summary>Back to the program's limit: "per_txn_limit_minor", "daily_limit_minor".</summary>
    public List<string>? Clear { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public sealed record ListCardsParams
{
    public string? AccountId { get; init; }
    public string? PlayerId { get; init; }
    public string? Status { get; init; }
    public int? Limit { get; init; }
}

/// <summary>Why a card payment was declined.</summary>
public static class DeclineCodes
{
    public const string InvalidExpiry = "invalid_expiry", InvalidCvv = "invalid_cvv", CardBlocked = "card_blocked", CardLost = "card_lost",
        CardStolen = "card_stolen", CardExpired = "card_expired", CardClosed = "card_closed", AccountFrozen = "account_frozen",
        AccountClosed = "account_closed", MerchantInactive = "merchant_inactive", ExceedsTransactionLimit = "exceeds_transaction_limit",
        ExceedsDailyLimit = "exceeds_daily_limit", InsufficientFunds = "insufficient_funds";
}

public sealed record CardRefund
{
    public string Id { get; init; } = "";
    public string TransactionId { get; init; } = "";
    public long AmountMinor { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset GameTime { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record MerchantRef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
}

public sealed record CardAuthorization
{
    public string Id { get; init; } = "";
    public string CardId { get; init; } = "";
    public string CardLast4 { get; init; } = "";
    public string BankId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public MerchantRef Merchant { get; init; } = new();
    public string MerchantAccountId { get; init; } = "";
    /// <summary>approved, declined, captured, released, expired or refunded.</summary>
    public string Status { get; init; } = "";
    public string? DeclineCode { get; init; }
    public string? DeclineMessage { get; init; }
    public long AmountMinor { get; init; }
    public long CapturedMinor { get; init; }
    public long RefundedMinor { get; init; }
    public CurrencyBrief Currency { get; init; } = new();
    public string? HoldId { get; init; }
    public long ReservedFeeMinor { get; init; }
    public DateTimeOffset? ExpiresAtGame { get; init; }
    public string? CaptureTransactionId { get; init; }
    public string? Reference { get; init; }
    public string? Description { get; init; }
    public List<CardRefund> Refunds { get; init; } = new();
    public DateTimeOffset GameTime { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }

    public bool IsDeclined => Status == "declined";
}

public sealed record AuthorizeCardParams
{
    /// <summary>The card: CardId, or CardNumber with ExpiryMonth and ExpiryYear (and optionally Cvv).</summary>
    public string? CardId { get; init; }
    public string? CardNumber { get; init; }
    public int? ExpiryMonth { get; init; }
    public int? ExpiryYear { get; init; }
    public string? Cvv { get; init; }
    /// <summary>A merchant account in the card's bank.</summary>
    public string? MerchantAccountId { get; init; }
    public string? MerchantAccountNumber { get; init; }
    public required long AmountMinor { get; init; }
    public string? Reference { get; init; }
    public string? Description { get; init; }
    public long? MaxFeeMinor { get; init; }
    /// <summary>Null or true (the default) captures an approved payment in full in the same call; false only reserves the money for a later capture or release.</summary>
    public bool? Capture { get; init; }
}

/// <summary>A decline is a result, not an exception: check Authorization.IsDeclined and DeclineCode.</summary>
public sealed record AuthorizeCardResult
{
    public CardAuthorization Authorization { get; init; } = new();
    public List<FeeCharge> Fees { get; init; } = new();
    public long FeesTotalMinor { get; init; }
    /// <summary>The cardholder's account after the call.</summary>
    public Balance Balance { get; init; } = new();
    /// <summary>The card_payment, when the payment was captured (approved, without Capture = false).</summary>
    public Transaction? Transaction { get; init; }
    public Balance? MerchantBalance { get; init; }
    public long? TotalDebitMinor { get; init; }
}

public sealed record CaptureCardParams
{
    /// <summary>Null captures everything; the rest is released.</summary>
    public long? AmountMinor { get; init; }
    public long? MaxFeeMinor { get; init; }
}

public sealed record CaptureCardResult
{
    public CardAuthorization Authorization { get; init; } = new();
    public Transaction Transaction { get; init; } = new();
    public Balance CardBalance { get; init; } = new();
    public Balance MerchantBalance { get; init; } = new();
    public List<FeeCharge> Fees { get; init; } = new();
    public long FeesTotalMinor { get; init; }
    public long TotalDebitMinor { get; init; }
}

public sealed record ReleaseCardResult
{
    public CardAuthorization Authorization { get; init; } = new();
    public Balance Balance { get; init; } = new();
}

public sealed record RefundCardParams
{
    /// <summary>Null refunds the rest.</summary>
    public long? AmountMinor { get; init; }
    public string? Reason { get; init; }
}

public sealed record RefundCardResult
{
    public CardRefund Refund { get; init; } = new();
    public CardAuthorization Authorization { get; init; } = new();
    public Transaction Transaction { get; init; } = new();
    public Balance CardBalance { get; init; } = new();
    public Balance MerchantBalance { get; init; } = new();
}

public sealed record ListCardAuthorizationsParams
{
    public string? CardId { get; init; }
    public string? MerchantId { get; init; }
    public string? AccountId { get; init; }
    public string? Status { get; init; }
    public int? Limit { get; init; }
}

internal sealed record ReasonBody(string? Reason);

public sealed class MerchantsResource
{
    private readonly Transport _t;
    internal MerchantsResource(Transport t) => _t = t;

    /// <summary>Registers a merchant; repeat calls with the same ExternalMerchantId return it. Needs merchants:write.</summary>
    public Task<Merchant> CreateAsync(CreateMerchantParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Merchant>(new(HttpMethod.Post, "/v1/merchants") { Body = p, RetrySafe = true }, o, ct);

    public async Task<List<Merchant>> ListAsync(ListMerchantsParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        return (await _t.SendAsync<DataList<Merchant>>(new(HttpMethod.Get, "/v1/merchants")
        {
            Query = new Dictionary<string, string?> { ["status"] = p.Status, ["owner_player_id"] = p.OwnerPlayerId,
                ["external_merchant_id"] = p.ExternalMerchantId, ["limit"] = p.Limit?.ToString(CultureInfo.InvariantCulture) },
        }, o, ct).ConfigureAwait(false)).Data;
    }

    public Task<Merchant> GetAsync(string merchantId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Merchant>(new(HttpMethod.Get, $"/v1/merchants/{Ops.Id(merchantId)}"), o, ct);

    /// <summary>Renames, recategorises, suspends or reactivates a merchant.</summary>
    public Task<Merchant> UpdateAsync(string merchantId, UpdateMerchantParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Merchant>(new(HttpMethod.Patch, $"/v1/merchants/{Ops.Id(merchantId)}") { Body = p }, o, ct);
}

public sealed class CardProgramsResource
{
    private readonly Transport _t;
    internal CardProgramsResource(Transport t) => _t = t;

    public async Task<List<CardProgram>> ListAsync(string bankId, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<CardProgram>>(new(HttpMethod.Get, $"/v1/banks/{Ops.Id(bankId)}/card-programs"), o, ct).ConfigureAwait(false)).Data;

    public Task<CardProgram> GetAsync(string bankId, string programId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<CardProgram>(new(HttpMethod.Get, $"/v1/banks/{Ops.Id(bankId)}/card-programs/{Ops.Id(programId)}"), o, ct);

    /// <summary>Adds a program. Turn on the bank's "cards" feature flag to issue cards. Needs bank:write.</summary>
    public Task<CardProgram> CreateAsync(string bankId, CardProgramParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<CardProgram>(new(HttpMethod.Post, $"/v1/banks/{Ops.Id(bankId)}/card-programs") { Body = p }, o, ct);

    public Task<CardProgram> UpdateAsync(string bankId, string programId, CardProgramParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<CardProgram>(new(HttpMethod.Patch, $"/v1/banks/{Ops.Id(bankId)}/card-programs/{Ops.Id(programId)}") { Body = p }, o, ct);
}

public sealed class CardsResource
{
    private readonly Transport _t;
    internal CardsResource(Transport t) => _t = t;

    /// <summary>Issues a card on a player account, charging any card_issuance fee. CardNumber and Cvv are in the result once. Needs cards:write.</summary>
    public Task<IssueCardResult> IssueAsync(IssueCardParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<IssueCardResult>(new(HttpMethod.Post, "/v1/cards") { Body = p, Idempotent = true }, o, ct);

    public async Task<List<Card>> ListAsync(ListCardsParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        return (await _t.SendAsync<DataList<Card>>(new(HttpMethod.Get, "/v1/cards")
        {
            Query = new Dictionary<string, string?> { ["account_id"] = p.AccountId, ["player_id"] = p.PlayerId, ["status"] = p.Status,
                ["limit"] = p.Limit?.ToString(CultureInfo.InvariantCulture) },
        }, o, ct).ConfigureAwait(false)).Data;
    }

    public Task<Card> GetAsync(string cardId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Card>(new(HttpMethod.Get, $"/v1/cards/{Ops.Id(cardId)}"), o, ct);

    /// <summary>Changes the name on the card or its own limits.</summary>
    public Task<Card> UpdateAsync(string cardId, UpdateCardParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<Card>(new(HttpMethod.Patch, $"/v1/cards/{Ops.Id(cardId)}") { Body = p }, o, ct);

    private Task<Card> Status(string cardId, string action, string? reason, RequestOptions? o, CancellationToken ct) =>
        _t.SendAsync<Card>(new(HttpMethod.Post, $"/v1/cards/{Ops.Id(cardId)}/{action}") { Body = new ReasonBody(reason) }, o, ct);

    /// <summary>Pauses the card: new payments are declined (card_blocked).</summary>
    public Task<Card> BlockAsync(string cardId, string? reason = null, RequestOptions? o = null, CancellationToken ct = default) =>
        Status(cardId, "block", reason, o, ct);

    public Task<Card> UnblockAsync(string cardId, RequestOptions? o = null, CancellationToken ct = default) =>
        Status(cardId, "unblock", null, o, ct);

    /// <summary>Reports the card "lost" or "stolen": final, and no replacement is issued. Approved payments still capture.</summary>
    public Task<Card> ReportLostAsync(string cardId, string reason = "lost", RequestOptions? o = null, CancellationToken ct = default) =>
        Status(cardId, "report-lost", reason, o, ct);

    public Task<Card> CloseAsync(string cardId, string? reason = null, RequestOptions? o = null, CancellationToken ct = default) =>
        Status(cardId, "close", reason, o, ct);
}

public sealed class CardAuthorizationsResource
{
    private readonly Transport _t;
    internal CardAuthorizationsResource(Transport t) => _t = t;

    /// <summary>Authorises a payment at a merchant. A decline is a result, not an exception. Needs cards:charge.</summary>
    public Task<AuthorizeCardResult> AuthorizeAsync(AuthorizeCardParams p, RequestOptions? o = null, CancellationToken ct = default)
    {
        Ops.CheckAmount(p.AmountMinor, true);
        return _t.SendAsync<AuthorizeCardResult>(new(HttpMethod.Post, "/v1/card-authorizations") { Body = p, Idempotent = true }, o, ct);
    }

    public async Task<List<CardAuthorization>> ListAsync(ListCardAuthorizationsParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        return (await _t.SendAsync<DataList<CardAuthorization>>(new(HttpMethod.Get, "/v1/card-authorizations")
        {
            Query = new Dictionary<string, string?> { ["card_id"] = p.CardId, ["merchant_id"] = p.MerchantId, ["account_id"] = p.AccountId,
                ["status"] = p.Status, ["limit"] = p.Limit?.ToString(CultureInfo.InvariantCulture) },
        }, o, ct).ConfigureAwait(false)).Data;
    }

    public Task<CardAuthorization> GetAsync(string authorizationId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<CardAuthorization>(new(HttpMethod.Get, $"/v1/card-authorizations/{Ops.Id(authorizationId)}"), o, ct);

    /// <summary>Settles all or part of an approved payment into the merchant account; the rest is released.</summary>
    public Task<CaptureCardResult> CaptureAsync(string authorizationId, CaptureCardParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        Ops.CheckAmount(p.AmountMinor, false);
        return _t.SendAsync<CaptureCardResult>(new(HttpMethod.Post, $"/v1/card-authorizations/{Ops.Id(authorizationId)}/capture") { Body = p, Idempotent = true }, o, ct);
    }

    /// <summary>Cancels an approved payment and frees its funds.</summary>
    public Task<ReleaseCardResult> ReleaseAsync(string authorizationId, string? reason = null, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<ReleaseCardResult>(new(HttpMethod.Post, $"/v1/card-authorizations/{Ops.Id(authorizationId)}/release")
        { Body = new ReasonBody(reason), Idempotent = true }, o, ct);

    /// <summary>Refunds all (null amount) or part of a captured payment from the merchant account. Fees are not refunded.</summary>
    public Task<RefundCardResult> RefundAsync(string authorizationId, RefundCardParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        Ops.CheckAmount(p.AmountMinor, false);
        return _t.SendAsync<RefundCardResult>(new(HttpMethod.Post, $"/v1/card-authorizations/{Ops.Id(authorizationId)}/refunds") { Body = p, Idempotent = true }, o, ct);
    }
}
