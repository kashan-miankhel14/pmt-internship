using Microsoft.AspNetCore.DataProtection;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.Security;
public sealed class DataProtectionEncryptionService(IDataProtectionProvider provider) : IEncryptionService
{
    private readonly IDataProtector _protector = provider.CreateProtector("PMT.FieldEncryption.v1");
    public string Protect(string plainText) => _protector.Protect(plainText);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
