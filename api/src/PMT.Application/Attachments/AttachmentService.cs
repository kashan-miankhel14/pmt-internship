using PMT.Application.Attachments.Dtos;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;

namespace PMT.Application.Attachments;

public sealed class AttachmentService(IAttachmentRepository repository, ICurrentUserService currentUser)
{
    public async Task<IReadOnlyCollection<AttachmentDto>> GetForEntityAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
        => (await repository.GetForEntityAsync(entityType, entityId, cancellationToken)).Select(Map).ToArray();

    public async Task<(Attachment Attachment, string PhysicalPath)> GetDownloadAsync(long id, CancellationToken cancellationToken = default)
    {
        var attachment = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Attachment), id);
        var uploadRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads"));
        var physicalPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", attachment.FilePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar)));
        if (!physicalPath.StartsWith(uploadRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(physicalPath))
            throw new NotFoundException(nameof(Attachment), id);
        return (attachment, physicalPath);
    }

    public async Task<long> CreateWithFileAsync(CreateAttachmentRequest request, Stream fileStream, CancellationToken cancellationToken = default)
    {
        var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        Directory.CreateDirectory(uploadDir);
        var filePath = Path.Combine(uploadDir, request.StoredFileName);
        using (var fs = new FileStream(filePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs, cancellationToken);
        }

        var relativePath = $"/uploads/{request.StoredFileName}";

        return await repository.CreateAsync(new Attachment
        {
            EntityType = request.EntityType.Trim(),
            EntityId = request.EntityId,
            ProjectId = request.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            TaskId = request.EntityType.Equals("Task", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            IssueId = request.EntityType.Equals("Issue", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            FileName = request.FileName,
            StoredFileName = request.StoredFileName,
            ContentType = request.ContentType,
            FileSize = request.FileSize,
            StoragePath = relativePath,
            FilePath = relativePath,
            UploadedByUserId = currentUser.UserId ?? throw new UnauthorizedAccessException(),
            InsertedBy = currentUser.UserId
        }, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(Attachment), id);
    }

    private static AttachmentDto Map(Attachment x)
    {
        var entityType = x.ProjectId.HasValue ? "Project" : x.TaskId.HasValue ? "Task" : "Issue";
        var entityId = x.ProjectId ?? x.TaskId ?? x.IssueId ?? 0;
        return new AttachmentDto(x.Id, entityType, entityId, x.FileName, x.ContentType, x.FileSize, x.FilePath, x.InsertDate);
    }
}
