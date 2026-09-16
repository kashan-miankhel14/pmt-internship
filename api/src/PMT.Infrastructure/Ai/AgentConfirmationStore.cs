using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PMT.Infrastructure.Ai;

/// <summary>One tool call held back until the user answers the confirmation.</summary>
/// <param name="ToolName">Tool to run on approval.</param>
/// <param name="ArgumentsJson">Arguments exactly as the model produced them.</param>
/// <param name="Summary">Human-readable description shown in the confirmation prompt.</param>
/// <param name="Order">
/// Position of the call within the model's original batch. Destructive and deferred calls are
/// parked in two lists but replayed as one sequence ordered by this, so a delete the user
/// approved still runs before or after the non-destructive work exactly as the model asked.
/// </param>
public sealed record AgentPendingInvocation(string ToolName, string ArgumentsJson, string Summary, int Order = 0);

/// <summary>A parked turn: everything needed to replay it once the user answers.</summary>
/// <param name="Token">The one-time token handed to the client.</param>
/// <param name="UserId">Owner of the confirmation. Only this user may redeem the token.</param>
/// <param name="SessionId">Conversation the actions belong to.</param>
/// <param name="ProjectId">Project scope the original turn ran under.</param>
/// <param name="Actions">The destructive calls, in the order the model asked for them.</param>
/// <param name="Deferred">
/// Non-destructive calls from the same batch that were held back because the turn stopped for the
/// confirmation. They are replayed alongside <paramref name="Actions"/> on approval so the agent
/// does not silently drop half of what the user asked for; they never carry destructive rights.
/// </param>
/// <param name="CreatedAt">When the token was issued; drives expiry.</param>
public sealed record AgentPendingConfirmation(
    string Token,
    long UserId,
    Guid SessionId,
    long? ProjectId,
    IReadOnlyList<AgentPendingInvocation> Actions,
    IReadOnlyList<AgentPendingInvocation> Deferred,
    DateTime CreatedAt);

/// <summary>
/// Holds destructive tool calls between the turn that proposed them and the user's answer.
/// </summary>
/// <remarks>
/// <para>Deliberately in-memory and singleton-scoped. A pending confirmation is worthless a few
/// seconds after the user closes the dialog, so it does not warrant a table, a migration or a
/// cache round trip; losing the lot on restart is correct behaviour, because the safe outcome of
/// a lost confirmation is "nothing was deleted".</para>
/// <para>Tokens are cryptographically random, single-use (a successful redemption removes the
/// entry with a compare-and-remove, so only one of two racing approvals wins), owned by one user,
/// and expire after <see cref="Lifetime"/>. Redeeming a token is the only way an entry leaves the
/// store other than pruning, so a replayed token can never delete twice — and a redemption that
/// fails its owner check leaves the entry alone, so knowing a token is not enough to destroy
/// someone else's pending confirmation.</para>
/// <para><b>Multi-instance caveat:</b> behind a load balancer the confirm call must land on the
/// instance that issued the token, otherwise it reads as expired. Sticky sessions cover that
/// today; a distributed store is the change to make if the API is ever scaled out without them.</para>
/// </remarks>
public sealed class AgentConfirmationStore
{
    /// <summary>How long a token stays redeemable.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Hard ceiling on parked confirmations. Reached only under abuse, since normal use redeems
    /// or expires entries quickly; past it the oldest entries are dropped so the store cannot
    /// grow without bound.
    /// </summary>
    private const int MaxEntries = 1_000;

    private readonly ConcurrentDictionary<string, AgentPendingConfirmation> _pending =
        new(StringComparer.Ordinal);

    /// <summary>Parks a set of destructive actions and returns the entry holding their token.</summary>
    /// <param name="userId">Owner of the confirmation; only this user may redeem the token.</param>
    /// <param name="sessionId">Conversation the actions belong to.</param>
    /// <param name="projectId">Project scope the original turn ran under.</param>
    /// <param name="actions">The destructive calls awaiting approval.</param>
    /// <param name="deferred">
    /// Non-destructive calls from the same batch that were held back with them, replayed on
    /// approval so the turn's remaining work is not silently dropped.
    /// </param>
    public AgentPendingConfirmation Create(
        long userId,
        Guid sessionId,
        long? projectId,
        IReadOnlyList<AgentPendingInvocation> actions,
        IReadOnlyList<AgentPendingInvocation>? deferred = null)
    {
        ArgumentNullException.ThrowIfNull(actions);

        Prune();

        var entry = new AgentPendingConfirmation(
            NewToken(), userId, sessionId, projectId, actions, deferred ?? [], DateTime.UtcNow);

        _pending[entry.Token] = entry;
        return entry;
    }

    /// <summary>
    /// Redeems a token. Succeeds at most once per token: the entry is validated first and then
    /// removed with an atomic compare-and-remove, so a concurrent replay of the same valid token
    /// loses the race and finds nothing. Returns false when the token is unknown, expired, or
    /// belongs to another user.
    /// </summary>
    /// <remarks>
    /// The order matters. Removing before validating would let anyone who learned a token destroy
    /// a confirmation they do not own, so a failed owner or expiry check deliberately leaves the
    /// entry in place for its rightful owner to redeem.
    /// </remarks>
    public bool TryConsume(string? token, long userId, out AgentPendingConfirmation? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (!_pending.TryGetValue(token, out var found)) return false;

        if (found.UserId != userId) return false;
        if (DateTime.UtcNow - found.CreatedAt > Lifetime) return false;

        // Compare-and-remove: only the caller that removes this exact entry may act on it.
        if (!_pending.TryRemove(new KeyValuePair<string, AgentPendingConfirmation>(token, found))) return false;

        entry = found;
        return true;
    }

    /// <summary>Drops expired entries, then the oldest survivors if the cap is still exceeded.</summary>
    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Lifetime;

        foreach (var pair in _pending)
        {
            if (pair.Value.CreatedAt < cutoff)
                _pending.TryRemove(pair.Key, out _);
        }

        if (_pending.Count < MaxEntries) return;

        foreach (var pair in _pending.OrderBy(x => x.Value.CreatedAt).Take(_pending.Count - MaxEntries + 1))
            _pending.TryRemove(pair.Key, out _);
    }

    /// <summary>URL-safe 256-bit token. Unguessable so a token cannot be brute-forced.</summary>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
