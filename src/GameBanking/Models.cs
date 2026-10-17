// Request and response models. Property names map to the API's snake_case JSON.
// Amounts are long minor units; a currency's Exponent is its decimal places.
namespace GameBanking;

public record CurrencyBrief
{
    public string Code { get; init; } = "";
    public int Exponent { get; init; }
    public string? Symbol { get; init; }
}

public sealed record Currency : CurrencyBrief
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Standard { get; init; }
    public string Status { get; init; } = "";
}

public sealed record Account
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string BankCode { get; init; } = "";
    public string OwnerType { get; init; } = "";
    public string? PlayerId { get; init; }
    /// <summary>Set on merchant accounts.</summary>
    public string? MerchantId { get; init; }
    /// <summary>The deposit product of a savings or fixed-deposit account.</summary>
    public string? ProductId { get; init; }
    /// <summary>Receives the account's interest; null pays the account itself.</summary>
    public string? InterestAccountId { get; init; }
    public string? SystemRole { get; init; }
    public string AccountType { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string AccountName { get; init; } = "";
    public CurrencyBrief Currency { get; init; } = new();
    public string Status { get; init; } = "";
    public bool AllowNegativeBalance { get; init; }
    public long PostedBalance { get; init; }
    public long AvailableBalance { get; init; }
    public long HeldBalance { get; init; }
    public long Version { get; init; }
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record Bank
{
    public string Id { get; init; } = "";
    public string Environment { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public string BankCode { get; init; } = "";
    public Currency BaseCurrency { get; init; } = new();
    public Dictionary<string, object?> Branding { get; init; } = new();
    public Dictionary<string, bool> FeatureFlags { get; init; } = new();
    public int ConfigVersion { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Set when a bank is created or fetched by ID.</summary>
    public List<Account>? SystemAccounts { get; init; }
}

public sealed record Balance
{
    public string AccountId { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public CurrencyBrief Currency { get; init; } = new();
    public long PostedBalance { get; init; }
    public long AvailableBalance { get; init; }
    public long HeldBalance { get; init; }
    public string PostedDisplay { get; init; } = "";
    public string AvailableDisplay { get; init; } = "";
    public long Version { get; init; }
    public DateTimeOffset AsOf { get; init; }
    public DateTimeOffset? AsOfGameTime { get; init; }
}

public sealed record Player
{
    public string Id { get; init; } = "";
    public string ExternalPlayerId { get; init; } = "";
    public string? DisplayName { get; init; }
    public string Status { get; init; } = "";
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record Entry
{
    public string Id { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string AccountName { get; init; } = "";
    public string Side { get; init; } = "";
    public long AmountMinor { get; init; }
    public long BalanceAfter { get; init; }
    /// <summary>"principal", "fee" or "levy". Fee and levy lines carry the rule and its name.</summary>
    public string Kind { get; init; } = "principal";
    public string? FeeRuleId { get; init; }
    public string? Description { get; init; }
}

public sealed record Initiator
{
    public string Type { get; init; } = "";
    public string Id { get; init; } = "";
}

public sealed record Transaction
{
    public string Id { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string BankId { get; init; } = "";
    public CurrencyBrief Currency { get; init; } = new();
    public long AmountMinor { get; init; }
    public string AmountDisplay { get; init; } = "";
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public Initiator InitiatedBy { get; init; } = new();
    public string? RelatedTransactionId { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("reversed_by_transaction_id")]
    public string? ReversedBy { get; init; }
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public DateTimeOffset GameTime { get; init; }
    public List<Entry> Entries { get; init; } = new();
}

public sealed record Counterparty
{
    public string AccountId { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string AccountName { get; init; } = "";
    public string BankCode { get; init; } = "";
}

/// <summary>One statement line. Direction is "debit" or "credit" from the account holder's point of view.</summary>
public sealed record HistoryItem
{
    public string EntryId { get; init; } = "";
    public string TransactionId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string Direction { get; init; } = "";
    public long AmountMinor { get; init; }
    public string AmountDisplay { get; init; } = "";
    public long BalanceAfter { get; init; }
    /// <summary>"principal", "fee" or "levy". A fee or levy line's narration is the fee's name.</summary>
    public string Kind { get; init; } = "principal";
    public string? Narration { get; init; }
    public string? Reference { get; init; }
    public Counterparty? Counterparty { get; init; }
    public DateTimeOffset RealTime { get; init; }
    public DateTimeOffset GameTime { get; init; }
    public string? RelatedTransactionId { get; init; }
}

public sealed record HistoryPage
{
    public List<HistoryItem> Data { get; init; } = new();
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
}

public sealed record Hold
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string? DestinationAccountId { get; init; }
    public long AmountMinor { get; init; }
    /// <summary>Held on top of the amount for the capture's expected fees.</summary>
    public long ReservedFeeMinor { get; init; }
    public long? CapturedAmountMinor { get; init; }
    public string Status { get; init; } = "";
    public string Reason { get; init; } = "";
    public string? Reference { get; init; }
    public DateTimeOffset ExpiresAtGame { get; init; }
    public string? CaptureTransactionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset CreatedGameTime { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public DateTimeOffset? ClosedGameTime { get; init; }
}

public sealed record NameEnquiry
{
    public string BankCode { get; init; } = "";
    public string BankName { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string AccountName { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed record Clock
{
    public string Mode { get; init; } = "";
    public int Speed { get; init; }
    public string TimeZone { get; init; } = "";
    public DateTimeOffset GameTime { get; init; }
    public string GameDay { get; init; } = "";
    public DateTimeOffset RealTime { get; init; }
}

/// <summary>Caps for one scope; null means no limit.</summary>
public sealed record LimitPolicy
{
    public long? PerTransactionMax { get; init; }
    public long? DailyMax { get; init; }
}

public static class LimitScopes
{
    public const string PlayerFunding = "player_funding";
    public const string GameFunding = "game_funding";
    public const string AccountTransfer = "account_transfer";
}

public sealed record Limits
{
    public string BankId { get; init; } = "";
    public string Currency { get; init; } = "";
    /// <summary>Policy per scope (see <see cref="LimitScopes"/>); null means no limits.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("limits")]
    public Dictionary<string, LimitPolicy?> LimitsByScope { get; init; } = new();
    public string GameDay { get; init; } = "";
    public long GameFundingUsedToday { get; init; }
    public string Note { get; init; } = "";
}

public sealed record ReconciliationReport
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public long AccountsChecked { get; init; }
    public long TransactionsChecked { get; init; }
    public List<Dictionary<string, object?>> Differences { get; init; } = new();
    public bool Ok { get; init; }
}

public sealed record FundingResult
{
    public Transaction Transaction { get; init; } = new();
    public Balance Balance { get; init; } = new();
}

public sealed record TransferResult
{
    public Transaction Transaction { get; init; } = new();
    public Balance SourceBalance { get; init; } = new();
    public Balance? DestinationBalance { get; init; }
    /// <summary>Fees and levies charged to the sender on top of the amount.</summary>
    public List<FeeCharge> Fees { get; init; } = new();
    public long FeesTotalMinor { get; init; }
    /// <summary>Amount plus fees.</summary>
    public long TotalDebitMinor { get; init; }
}

public sealed record HoldResult
{
    public Hold Hold { get; init; } = new();
    public Balance Balance { get; init; } = new();
}

public sealed record CaptureResult
{
    public Hold Hold { get; init; } = new();
    public Transaction Transaction { get; init; } = new();
    public Balance SourceBalance { get; init; } = new();
    public Balance? DestinationBalance { get; init; }
    public List<FeeCharge> Fees { get; init; } = new();
    public long FeesTotalMinor { get; init; }
    public long TotalDebitMinor { get; init; }
}

public sealed record ClockJobs
{
    public string GameDay { get; init; } = "";
    public int HoldsExpired { get; init; }
    public List<string> JobsRun { get; init; } = new();
}

public sealed record AdvanceClockResult
{
    public Clock Clock { get; init; } = new();
    public DateTimeOffset PreviousGameTime { get; init; }
    public ClockJobs Jobs { get; init; } = new();
}

// ---- request parameters (null fields are omitted, so the engine's defaults apply) ----

public sealed record UpsertPlayerParams
{
    public required string ExternalPlayerId { get; init; }
    public string? DisplayName { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public sealed record OpenAccountParams
{
    public required string BankId { get; init; }
    /// <summary>For a player account.</summary>
    public string? PlayerId { get; init; }
    /// <summary>For a merchant account, which receives card payments.</summary>
    public string? MerchantId { get; init; }
    /// <summary>A savings product: the account earns interest and follows its rules.</summary>
    public string? ProductId { get; init; }
    public string? InterestAccountId { get; init; }
    public string? AccountType { get; init; }
    public string? AccountName { get; init; }
    public string? Currency { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public sealed record ListAccountsParams
{
    public string? BankId { get; init; }
    public string? PlayerId { get; init; }
    public string? MerchantId { get; init; }
    public bool IncludeSystem { get; init; }
    public int? Limit { get; init; }
}

public sealed record UpdateAccountStatusParams
{
    /// <summary>active, frozen or closed.</summary>
    public required string Status { get; init; }
    public string? Reason { get; init; }
}

public sealed record FundingParams
{
    public required string AccountId { get; init; }
    public required long AmountMinor { get; init; }
    public required string Reason { get; init; }
    /// <summary>Your own unique ID for this issuance.</summary>
    public required string Reference { get; init; }
    public string? Currency { get; init; }
    public string? SourceAccountId { get; init; }
    public string? Narration { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

public sealed record TransferParams
{
    public required string SourceAccountId { get; init; }
    public required long AmountMinor { get; init; }
    public string? DestinationAccountId { get; init; }
    public string? DestinationAccountNumber { get; init; }
    public string? DestinationBankCode { get; init; }
    public string? BankId { get; init; }
    public string? Currency { get; init; }
    public string? Reference { get; init; }
    public string? Narration { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
    /// <summary>Refuse with <see cref="FeeChangedException"/> if the fees now total more, e.g. a quote's FeesTotalMinor.</summary>
    public long? MaxFeeMinor { get; init; }
}

public sealed record PlaceHoldParams
{
    public required string AccountId { get; init; }
    public required long AmountMinor { get; init; }
    public required string Reason { get; init; }
    public string? Reference { get; init; }
    public string? DestinationAccountId { get; init; }
    public string? DestinationAccountNumber { get; init; }
    public DateTimeOffset? ExpiresAtGame { get; init; }
    public long? ExpiresInGameSeconds { get; init; }
    /// <summary>Caps the capture fee reserved now (<see cref="FeeChangedException"/> if higher).</summary>
    public long? MaxFeeMinor { get; init; }
}

public sealed record CaptureHoldParams
{
    /// <summary>Amount to capture; null captures the whole hold.</summary>
    public long? AmountMinor { get; init; }
    public string? DestinationAccountId { get; init; }
    public string? DestinationAccountNumber { get; init; }
    public string? Narration { get; init; }
    public string? Reference { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
    /// <summary>Refuse with <see cref="FeeChangedException"/> if the capture's fees now total more.</summary>
    public long? MaxFeeMinor { get; init; }
}

public sealed record ReverseParams
{
    public required string Reason { get; init; }
    public string? Narration { get; init; }
}

public sealed record CreateBankParams
{
    public required string Name { get; init; }
    public required string BaseCurrency { get; init; }
    public string? Slug { get; init; }
    public string? BankCode { get; init; }
    public Dictionary<string, object?>? Branding { get; init; }
    public Dictionary<string, bool>? FeatureFlags { get; init; }
}

public sealed record UpdateBankParams
{
    public string? Name { get; init; }
    public Dictionary<string, object?>? Branding { get; init; }
    public Dictionary<string, bool>? FeatureFlags { get; init; }
    public string? Status { get; init; }
}

public sealed record CreateCurrencyParams
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required int Exponent { get; init; }
    public string? Symbol { get; init; }
}

public sealed record ConfigureClockParams
{
    public string? Mode { get; init; }
    public int? Speed { get; init; }
    public string? TimeZone { get; init; }
    public DateTimeOffset? GameTime { get; init; }
}

public sealed record AdvanceClockParams
{
    public long? Seconds { get; init; }
    public long? Minutes { get; init; }
    public long? Hours { get; init; }
    public long? Days { get; init; }
    public DateTimeOffset? To { get; init; }
}

// ---- fees and charges ----

public static class FeeEvents
{
    public const string Transfer = "transfer";
    public const string HoldCapture = "hold_capture";
    public const string AccountMaintenance = "account_maintenance";
}

public static class FeeModels
{
    public const string Fixed = "fixed";
    public const string Percentage = "percentage";
    public const string Combined = "combined";
}

public static class FeeKinds
{
    /// <summary>Credits the bank's fee income.</summary>
    public const string Fee = "fee";
    /// <summary>Credits levies payable: money held for someone else.</summary>
    public const string Levy = "levy";
}

/// <summary>A fee or levy a bank charges. Amounts are minor units; times are game time.</summary>
public sealed record FeeRule
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Event { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Model { get; init; } = "";
    public long FixedMinor { get; init; }
    /// <summary>Basis points: 100 = 1%.</summary>
    public long PercentageBps { get; init; }
    public long? MinFeeMinor { get; init; }
    public long? MaxFeeMinor { get; init; }
    public long? MinAmountMinor { get; init; }
    public long? MaxAmountMinor { get; init; }
    public string Payer { get; init; } = "";
    public string CreditAccount { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public int Version { get; init; }
    /// <summary>"in_effect", "scheduled", "ended" or "inactive". Only in_effect rules charge.</summary>
    public string State { get; init; } = "";
    public List<string> Warnings { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateFeeRuleParams
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    /// <summary>See <see cref="FeeEvents"/>.</summary>
    public required string Event { get; init; }
    /// <summary>See <see cref="FeeModels"/>.</summary>
    public required string Model { get; init; }
    public string? Kind { get; init; }
    public long? FixedMinor { get; init; }
    public long? PercentageBps { get; init; }
    public long? MinFeeMinor { get; init; }
    public long? MaxFeeMinor { get; init; }
    public long? MinAmountMinor { get; init; }
    public long? MaxAmountMinor { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    /// <summary>Start at the current game time.</summary>
    public bool? StartNow { get; init; }
}

/// <summary>Changes a fee; null fields keep their value. Code and event cannot change.</summary>
public sealed record UpdateFeeRuleParams
{
    public string? Name { get; init; }
    public string? Kind { get; init; }
    public string? Model { get; init; }
    public long? FixedMinor { get; init; }
    public long? PercentageBps { get; init; }
    public long? MinFeeMinor { get; init; }
    public long? MaxFeeMinor { get; init; }
    public long? MinAmountMinor { get; init; }
    public long? MaxAmountMinor { get; init; }
    /// <summary>"active", or "inactive" to pause.</summary>
    public string? Status { get; init; }
    public DateTimeOffset? EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public bool? StartNow { get; init; }
    /// <summary>Fields to reset to "no limit", e.g. "max_fee_minor".</summary>
    public List<string>? Clear { get; init; }
}

/// <summary>One fee or levy charged on a payment.</summary>
public sealed record FeeCharge
{
    public string FeeRuleId { get; init; } = "";
    public int FeeRuleVersion { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public long AmountMinor { get; init; }
    public string CreditAccount { get; init; } = "";
}

public sealed record QuoteFeesParams
{
    public required string BankId { get; init; }
    public required long AmountMinor { get; init; }
    /// <summary>"transfer" (default) or "hold_capture".</summary>
    public string? Event { get; init; }
}

public sealed record FeeQuote
{
    public string BankId { get; init; } = "";
    public string Event { get; init; } = "";
    public long AmountMinor { get; init; }
    public List<FeeCharge> Fees { get; init; } = new();
    public long FeesTotalMinor { get; init; }
    public long TotalDebitMinor { get; init; }
    public DateTimeOffset GameTime { get; init; }
}

public sealed record BookLine
{
    public string SystemRole { get; init; } = "";
    public string AccountName { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public long BalanceMinor { get; init; }
    public string BalanceDisplay { get; init; } = "";
}

/// <summary>The bank's own ledger accounts, from which its profit and loss derives.</summary>
public sealed record BankBooks
{
    public string BankId { get; init; } = "";
    public string Currency { get; init; } = "";
    public long FeeIncomeMinor { get; init; }
    public long LeviesPayableMinor { get; init; }
    /// <summary>Interest paid to savers (negative).</summary>
    public long InterestExpenseMinor { get; init; }
    /// <summary>Interest earned by savers, not yet paid.</summary>
    public long AccruedInterestMinor { get; init; }
    /// <summary>Money in active fixed deposits.</summary>
    public long FixedDepositsMinor { get; init; }
    public List<BookLine> Accounts { get; init; } = new();
    public DateTimeOffset AsOf { get; init; }
}

// ---- management ----

public sealed record Developer
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ContactEmail { get; init; } = "";
    public string Status { get; init; } = "";
    public bool HasPassword { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record Game
{
    public string Id { get; init; } = "";
    public string DeveloperId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ApiKey
{
    public string Id { get; init; } = "";
    public string GameId { get; init; } = "";
    public string Environment { get; init; } = "";
    public string Label { get; init; } = "";
    public string Prefix { get; init; } = "";
    /// <summary>Set only in the response that created the key. Store it then.</summary>
    public string? Secret { get; init; }
    public List<string> Scopes { get; init; } = new();
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record DeveloperCredential
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Prefix { get; init; } = "";
    public string? Secret { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record AuditLog
{
    public string Id { get; init; } = "";
    public string? DeveloperId { get; init; }
    public string? GameId { get; init; }
    public string? Environment { get; init; }
    public string ActorType { get; init; } = "";
    public string ActorId { get; init; } = "";
    public string Action { get; init; } = "";
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record UpdateGameParams
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
    /// <summary>"active" or "suspended".</summary>
    public string? Status { get; init; }
}

public sealed record IssueApiKeyParams
{
    /// <summary>sandbox or production.</summary>
    public required string Environment { get; init; }
    public string? Label { get; init; }
    /// <summary>Defaults to bank:read, account:read, account:write, transfer:create.</summary>
    public List<string>? Scopes { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public static class Scopes
{
    public const string BankRead = "bank:read";
    public const string BankWrite = "bank:write";
    public const string AccountRead = "account:read";
    public const string AccountWrite = "account:write";
    public const string TransferCreate = "transfer:create";
    public const string FundingCreate = "funding:create";
    public const string TransactionReverse = "transaction:reverse";
    public const string LimitsWrite = "limits:write";
    public const string WebhooksRead = "webhooks:read";
    public const string WebhooksWrite = "webhooks:write";
    public const string MerchantsWrite = "merchants:write";
    public const string CardsRead = "cards:read";
    public const string CardsWrite = "cards:write";
    public const string CardsCharge = "cards:charge";
    public const string DepositsWrite = "deposits:write";
    public static readonly IReadOnlyList<string> All = [BankRead, BankWrite, AccountRead, AccountWrite, TransferCreate, FundingCreate, TransactionReverse, LimitsWrite,
        WebhooksRead, WebhooksWrite, MerchantsWrite, CardsRead, CardsWrite, CardsCharge, DepositsWrite];
}
