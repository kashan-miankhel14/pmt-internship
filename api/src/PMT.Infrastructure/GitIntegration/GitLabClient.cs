using System.Net.Http.Headers;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.GitIntegration;
public sealed class GitLabClient(HttpClient client) : IGitProviderClient
{
    public string Provider => "GitLab";
    public async Task<string?> GetReferenceAsync(string repository, string referenceId, string accessToken, CancellationToken cancellationToken = default)
    {
        var project = Uri.EscapeDataString(repository);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"projects/{project}/issues/{referenceId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
