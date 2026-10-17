namespace GameBanking;

/// <summary>Any failure from the Game Banking Engine or from reaching it. Branch on <see cref="Code"/> for anything finer than the type.</summary>
public class GameBankException : Exception
{
    /// <summary>HTTP status, or 0 when no response arrived.</summary>
    public int Status { get; }
    /// <summary>The engine's stable error code, e.g. "insufficient_funds".</summary>
    public string Code { get; }
    /// <summary>Extra context, e.g. available_balance.</summary>
    public IReadOnlyDictionary<string, object?> Details { get; }
    /// <summary>The engine's correlation ID (X-Request-Id). Quote it when reporting a problem.</summary>
    public string? RequestId { get; }
    /// <summary>Seconds the engine asked the caller to wait (Retry-After).</summary>
    public double? RetryAfter { get; }

    public GameBankException(int status, string code, string message, IReadOnlyDictionary<string, object?>? details = null,
        string? requestId = null, double? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
        Code = code;
        Details = details ?? new Dictionary<string, object?>();
        RequestId = requestId;
        RetryAfter = retryAfter;
    }

    public override string ToString() => $"{GetType().Name}: {Code}: {Message}" + (RequestId is null ? "" : $" (request {RequestId})");

    internal static GameBankException For(int status, string code, string message, IReadOnlyDictionary<string, object?>? details = null,
        string? requestId = null, double? retryAfter = null, Exception? inner = null) => status switch
    {
        0 => new ConnectionException(status, code, message, details, requestId, retryAfter, inner),
        401 => new AuthenticationException(status, code, message, details, requestId, retryAfter, inner),
        403 => new PermissionDeniedException(status, code, message, details, requestId, retryAfter, inner),
        404 => new NotFoundException(status, code, message, details, requestId, retryAfter, inner),
        409 when code == "fee_changed" => new FeeChangedException(status, code, message, details, requestId, retryAfter, inner),
        409 when code == "penalty_changed" => new PenaltyChangedException(status, code, message, details, requestId, retryAfter, inner),
        409 => new ConflictException(status, code, message, details, requestId, retryAfter, inner),
        429 => new RateLimitException(status, code, message, details, requestId, retryAfter, inner),
        422 when code == "insufficient_funds" => new InsufficientFundsException(status, code, message, details, requestId, retryAfter, inner),
        422 when code == "limit_exceeded" => new LimitExceededException(status, code, message, details, requestId, retryAfter, inner),
        422 => new UnprocessableException(status, code, message, details, requestId, retryAfter, inner),
        >= 500 => new ServerException(status, code, message, details, requestId, retryAfter, inner),
        >= 400 => new InvalidRequestException(status, code, message, details, requestId, retryAfter, inner),
        _ => new GameBankException(status, code, message, details, requestId, retryAfter, inner),
    };
}

/// <summary>401: the API key or credential is missing, wrong, expired or revoked.</summary>
public class AuthenticationException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>403: the credential lacks a scope, or the game or developer is suspended.</summary>
public class PermissionDeniedException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>404: the resource does not exist in this game and environment.</summary>
public class NotFoundException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>409: e.g. idempotency_key_reused, already_reversed.</summary>
public class ConflictException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>
/// 409 fee_changed: the fees now total more than the request's MaxFeeMinor, e.g. the fee schedule
/// changed after the player saw a quote. Details["fees"] lists the current charges; get a new quote.
/// </summary>
public class FeeChangedException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : ConflictException(s, c, m, d, r, ra, i);
/// <summary>409 penalty_changed: breaking a fixed deposit would cost more than MaxPenaltyMinor. Details["quote"] has the figures.</summary>
public class PenaltyChangedException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : ConflictException(s, c, m, d, r, ra, i);
/// <summary>400, 413, 415, or a request the SDK rejected before sending it.</summary>
public class InvalidRequestException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>422 business rule failure, e.g. account_frozen, hold_not_active.</summary>
public class UnprocessableException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>422 insufficient_funds: the source account's available balance is too low.</summary>
public class InsufficientFundsException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : UnprocessableException(s, c, m, d, r, ra, i);
/// <summary>422 limit_exceeded: a per-transaction or per-game-day cap would be exceeded.</summary>
public class LimitExceededException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : UnprocessableException(s, c, m, d, r, ra, i);
/// <summary>429: slow down; see RetryAfter.</summary>
public class RateLimitException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>5xx: the engine failed. Nothing was committed for a 500.</summary>
public class ServerException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
/// <summary>No response: network failure or timeout.</summary>
public class ConnectionException(int s, string c, string m, IReadOnlyDictionary<string, object?>? d, string? r, double? ra, Exception? i)
    : GameBankException(s, c, m, d, r, ra, i);
