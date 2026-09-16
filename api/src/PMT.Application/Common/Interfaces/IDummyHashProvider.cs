namespace PMT.Application.Common.Interfaces;

/// <summary>
/// Provides a single, lazily-computed dummy password hash used to keep login-failure
/// timing uniform regardless of whether the supplied username exists.
/// </summary>
public interface IDummyHashProvider
{
    /// <summary>A valid password hash that no real password is expected to match.</summary>
    string Hash { get; }
}
