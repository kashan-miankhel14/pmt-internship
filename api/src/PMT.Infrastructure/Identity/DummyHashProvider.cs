using PMT.Application.Common.Interfaces;

namespace PMT.Infrastructure.Identity;

/// <summary>
/// Provides a single, lazily-computed dummy password hash used to keep login-failure
/// timing uniform regardless of whether the supplied username exists. The hash is computed
/// once per application lifetime and reused for every "user not found" verification attempt
/// so that the password-hasher cost is incurred on the not-found path as well.
/// </summary>
public sealed class DummyHashProvider(IPasswordHasher hasher) : IDummyHashProvider
{
    private readonly Lazy<string> _dummy = new(() => hasher.Hash(Guid.NewGuid().ToString()));

    /// <inheritdoc />
    public string Hash => _dummy.Value;
}
