using Isopoh.Cryptography.Argon2;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.Identity;
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) => Argon2.Hash(password);
    public bool Verify(string hash, string password) => Argon2.Verify(hash, password);
}
