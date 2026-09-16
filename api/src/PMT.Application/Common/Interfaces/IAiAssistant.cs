namespace PMT.Application.Common.Interfaces;

public interface IAiAssistant
{
    Task<string> BreakDownStoryAsync(string title, string? description, CancellationToken cancellationToken = default);
    Task<string> SummarizeThreadAsync(IEnumerable<string> comments, CancellationToken cancellationToken = default);
}
