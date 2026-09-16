using PMT.Domain.Entities;

namespace PMT.Application.Boards;

public interface IBoardColumnRepository
{
    /// <summary>The whole board for one project, in Ordinal order.</summary>
    Task<IReadOnlyCollection<BoardColumn>> GetByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    Task<long> CreateAsync(BoardColumn entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(BoardColumn entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
