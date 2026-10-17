using System.Globalization;

namespace GameBanking;

/// <summary>An annual rate from a balance upwards (300 = 3%).</summary>
public sealed record DepositTier(long FromMinor, int RateBps);

/// <summary>A fixed-deposit term on offer.</summary>
public sealed record DepositTerm(int Days, int RateBps);

public static class MaturityInstructions
{
    public const string Payout = "payout", RolloverPrincipal = "rollover_principal", RolloverAll = "rollover_all";
}

public sealed record DepositProduct
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>"savings" or "fixed_deposit".</summary>
    public string Kind { get; init; } = "";
    public List<DepositTier> RateTiers { get; init; } = new();
    /// <summary>"whole_balance" or "banded".</summary>
    public string TierMethod { get; init; } = "";
    public List<DepositTerm> Terms { get; init; } = new();
    public string DayCount { get; init; } = "";
    public string AccrualBasis { get; init; } = "";
    public string PayoutFrequency { get; init; } = "";
    public int WithholdingTaxBps { get; init; }
    public long MinOpeningMinor { get; init; }
    public long MinBalanceMinor { get; init; }
    public long? MaxBalanceMinor { get; init; }
    public int? WithdrawalsPerMonth { get; init; }
    /// <summary>"block", "forfeit_interest" or "fee".</summary>
    public string ExcessWithdrawalAction { get; init; } = "";
    public int EarlyForfeitBps { get; init; }
    public long EarlyPenaltyFixedMinor { get; init; }
    public int EarlyPenaltyBps { get; init; }
    public string DefaultMaturityInstruction { get; init; } = "";
    public string Status { get; init; } = "";
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Creates or updates a product; null fields keep their value (or default). Code and Kind on create only.</summary>
public sealed record DepositProductParams
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Kind { get; init; }
    public List<DepositTier>? RateTiers { get; init; }
    public string? TierMethod { get; init; }
    public List<DepositTerm>? Terms { get; init; }
    public string? DayCount { get; init; }
    public string? AccrualBasis { get; init; }
    public string? PayoutFrequency { get; init; }
    public int? WithholdingTaxBps { get; init; }
    public long? MinOpeningMinor { get; init; }
    public long? MinBalanceMinor { get; init; }
    public long? MaxBalanceMinor { get; init; }
    public int? WithdrawalsPerMonth { get; init; }
    public string? ExcessWithdrawalAction { get; init; }
    public int? EarlyForfeitBps { get; init; }
    public long? EarlyPenaltyFixedMinor { get; init; }
    public int? EarlyPenaltyBps { get; init; }
    public string? DefaultMaturityInstruction { get; init; }
    public string? Status { get; init; }
    public List<string>? Clear { get; init; }
}

public sealed record DepositQuoteParams
{
    public required string ProductId { get; init; }
    public required long AmountMinor { get; init; }
    /// <summary>Savings: days to project (default 30).</summary>
    public int? Days { get; init; }
    /// <summary>Fixed deposits.</summary>
    public int? TermDays { get; init; }
}

public sealed record DepositQuote
{
    public string ProductId { get; init; } = "";
    public long AmountMinor { get; init; }
    public int Days { get; init; }
    public int RateBps { get; init; }
    public long GrossInterestMinor { get; init; }
    public long TaxMinor { get; init; }
    public long NetInterestMinor { get; init; }
    public long MaturityAmountMinor { get; init; }
}

public sealed record InterestPayout
{
    public string TransactionId { get; init; } = "";
    public long GrossMinor { get; init; }
    public string Narration { get; init; } = "";
    public DateTimeOffset GameTime { get; init; }
}

public sealed record AccountInterest
{
    public string AccountId { get; init; } = "";
    public string ProductId { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string PayoutFrequency { get; init; } = "";
    /// <summary>The rate the current balance earns (a fixed deposit's locked rate).</summary>
    public int RateBps { get; init; }
    public long AccruedInterestMinor { get; init; }
    public string AccruedThrough { get; init; } = "";
    public string NextPayoutDay { get; init; } = "";
    public string? ForfeitMonth { get; init; }
    public int WithdrawalsThisMonth { get; init; }
    public int? WithdrawalsPerMonth { get; init; }
    public List<InterestPayout> Payouts { get; init; } = new();
}

public sealed record FixedDeposit
{
    public string Id { get; init; } = "";
    public string BankId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string PlayerId { get; init; } = "";
    public string ProductId { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string SourceAccountId { get; init; } = "";
    public string PayoutAccountId { get; init; } = "";
    public long PrincipalMinor { get; init; }
    public int RateBps { get; init; }
    public int TermDays { get; init; }
    public string StartDay { get; init; } = "";
    public string MaturityDay { get; init; } = "";
    public string MaturityInstruction { get; init; } = "";
    /// <summary>"active", "matured" or "broken".</summary>
    public string Status { get; init; } = "";
    public int TermsCompleted { get; init; }
    public long InterestPaidMinor { get; init; }
    public long AccruedInterestMinor { get; init; }
    public long BalanceMinor { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
}

public sealed record OpenFixedDepositParams
{
    public required string SourceAccountId { get; init; }
    public required string ProductId { get; init; }
    public required int TermDays { get; init; }
    public required long AmountMinor { get; init; }
    public string? MaturityInstruction { get; init; }
    /// <summary>Default the source account.</summary>
    public string? PayoutAccountId { get; init; }
}

public sealed record OpenFixedDepositResult
{
    public FixedDeposit FixedDeposit { get; init; } = new();
    public Transaction Transaction { get; init; } = new();
    public Balance SourceBalance { get; init; } = new();
}

public sealed record BreakQuote
{
    public string FixedDepositId { get; init; } = "";
    public long PrincipalMinor { get; init; }
    public long AccruedInterestMinor { get; init; }
    public long ForfeitedInterestMinor { get; init; }
    public long InterestPaidMinor { get; init; }
    public long TaxMinor { get; init; }
    public long PenaltyMinor { get; init; }
    public long PayoutMinor { get; init; }
    public string PayoutAccountId { get; init; } = "";
}

public sealed record BreakFixedDepositResult
{
    public FixedDeposit FixedDeposit { get; init; } = new();
    public BreakQuote Quote { get; init; } = new();
    public Transaction? Transaction { get; init; }
}

public sealed record ListFixedDepositsParams
{
    public string? PlayerId { get; init; }
    public string? SourceAccountId { get; init; }
    public string? Status { get; init; }
    public int? Limit { get; init; }
}

internal sealed record MaturityBody(string MaturityInstruction);
internal sealed record BreakBody(long? MaxPenaltyMinor);

public sealed class DepositProductsResource
{
    private readonly Transport _t;
    internal DepositProductsResource(Transport t) => _t = t;

    public async Task<List<DepositProduct>> ListAsync(string bankId, RequestOptions? o = null, CancellationToken ct = default) =>
        (await _t.SendAsync<DataList<DepositProduct>>(new(HttpMethod.Get, $"/v1/banks/{Ops.Id(bankId)}/deposit-products"), o, ct).ConfigureAwait(false)).Data;

    public Task<DepositProduct> GetAsync(string bankId, string productId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<DepositProduct>(new(HttpMethod.Get, $"/v1/banks/{Ops.Id(bankId)}/deposit-products/{Ops.Id(productId)}"), o, ct);

    /// <summary>Adds a savings or fixed-deposit product. Needs bank:write; turn on the bank's "savings" flag to open accounts.</summary>
    public Task<DepositProduct> CreateAsync(string bankId, DepositProductParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<DepositProduct>(new(HttpMethod.Post, $"/v1/banks/{Ops.Id(bankId)}/deposit-products") { Body = p }, o, ct);

    /// <summary>Changes a product (bumps its version).</summary>
    public Task<DepositProduct> UpdateAsync(string bankId, string productId, DepositProductParams p, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<DepositProduct>(new(HttpMethod.Patch, $"/v1/banks/{Ops.Id(bankId)}/deposit-products/{Ops.Id(productId)}") { Body = p }, o, ct);

    /// <summary>Projects the interest an amount would earn.</summary>
    public Task<DepositQuote> QuoteAsync(DepositQuoteParams p, RequestOptions? o = null, CancellationToken ct = default)
    {
        Ops.CheckAmount(p.AmountMinor, true);
        return _t.SendAsync<DepositQuote>(new(HttpMethod.Post, "/v1/deposit-products/quote") { Body = p }, o, ct);
    }
}

public sealed class FixedDepositsResource
{
    private readonly Transport _t;
    internal FixedDepositsResource(Transport t) => _t = t;

    /// <summary>Books a fixed deposit at the term's current rate. Needs deposits:write.</summary>
    public Task<OpenFixedDepositResult> OpenAsync(OpenFixedDepositParams p, RequestOptions? o = null, CancellationToken ct = default)
    {
        Ops.CheckAmount(p.AmountMinor, true);
        return _t.SendAsync<OpenFixedDepositResult>(new(HttpMethod.Post, "/v1/fixed-deposits") { Body = p, Idempotent = true }, o, ct);
    }

    public async Task<List<FixedDeposit>> ListAsync(ListFixedDepositsParams? p = null, RequestOptions? o = null, CancellationToken ct = default)
    {
        p ??= new();
        return (await _t.SendAsync<DataList<FixedDeposit>>(new(HttpMethod.Get, "/v1/fixed-deposits")
        {
            Query = new Dictionary<string, string?> { ["player_id"] = p.PlayerId, ["source_account_id"] = p.SourceAccountId, ["status"] = p.Status,
                ["limit"] = p.Limit?.ToString(CultureInfo.InvariantCulture) },
        }, o, ct).ConfigureAwait(false)).Data;
    }

    public Task<FixedDeposit> GetAsync(string depositId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<FixedDeposit>(new(HttpMethod.Get, $"/v1/fixed-deposits/{Ops.Id(depositId)}"), o, ct);

    /// <summary>Changes what happens at maturity.</summary>
    public Task<FixedDeposit> SetMaturityInstructionAsync(string depositId, string instruction, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<FixedDeposit>(new(HttpMethod.Patch, $"/v1/fixed-deposits/{Ops.Id(depositId)}") { Body = new MaturityBody(instruction) }, o, ct);

    /// <summary>What breaking the deposit early pays now.</summary>
    public Task<BreakQuote> BreakQuoteAsync(string depositId, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<BreakQuote>(new(HttpMethod.Get, $"/v1/fixed-deposits/{Ops.Id(depositId)}/break-quote"), o, ct);

    /// <summary>Breaks the deposit early. Pass the quote's PenaltyMinor as maxPenaltyMinor (PenaltyChangedException if it rose).</summary>
    public Task<BreakFixedDepositResult> BreakAsync(string depositId, long? maxPenaltyMinor = null, RequestOptions? o = null, CancellationToken ct = default) =>
        _t.SendAsync<BreakFixedDepositResult>(new(HttpMethod.Post, $"/v1/fixed-deposits/{Ops.Id(depositId)}/break")
        { Body = new BreakBody(maxPenaltyMinor), Idempotent = true }, o, ct);
}
