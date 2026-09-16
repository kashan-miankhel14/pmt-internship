namespace PMT.Application.Common.Interfaces;

public interface IGitProviderClient
{
    string Provider { get; }
    Task<string?> GetReferenceAsync(string repository, string referenceId, string accessToken, CancellationToken cancellationToken = default);
}
