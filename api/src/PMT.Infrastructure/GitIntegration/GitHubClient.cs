using System.Net.Http.Headers;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.GitIntegration;
public sealed class GitHubClient(HttpClient client) : IGitProviderClient
{
    public string Provider => "GitHub";
    public async Task<string?> GetReferenceAsync(string repository, string referenceId, string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{repository}/issues/{referenceId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("PMT/1.0");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
