namespace PMT.Application.Common.Interfaces;

public interface IEncryptionService
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}
