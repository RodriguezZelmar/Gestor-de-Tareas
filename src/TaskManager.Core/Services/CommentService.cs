using TaskManager.Core.Data;
using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.Core.Services;

/// <summary>Comentarios e historial de una tarea.</summary>
public sealed class CommentService(AppDatabase db, TaskService tasks, TimeProvider clock)
{
    public async Task<IReadOnlyList<CommentView>> ListAsync(int userId, int taskId)
    {
        await tasks.GetAsync(userId, taskId);
        var comments = await db.Connection.Table<Comment>().Where(c => c.TaskId == taskId).ToListAsync();
        var authorIds = comments.Select(c => c.AuthorId).Distinct().ToList();
        var authors = (await db.Connection.Table<User>().Where(u => authorIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id, u => u.DisplayName);

        return comments
            .OrderBy(c => c.CreatedUtc)
            .ThenBy(c => c.Id)
            .Select(c => new CommentView(c, authors.GetValueOrDefault(c.AuthorId, "Usuario eliminado")))
            .ToList();
    }

    public async Task<Comment> AddAsync(int userId, int taskId, string? body)
    {
        await tasks.GetAsync(userId, taskId);
        var comment = new Comment
        {
            TaskId = taskId,
            AuthorId = userId,
            Body = InputSanitizer.Text(body, "El comentario", 2000, multiline: true),
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        };
        await db.Connection.InsertAsync(comment);
        return comment;
    }

    /// <summary>Sólo el autor puede borrar sus comentarios; las entradas del historial no se borran.</summary>
    public async Task DeleteAsync(int userId, int commentId)
    {
        var comment = await db.Connection.FindAsync<Comment>(commentId);
        if (comment is null || comment.AuthorId != userId || comment.IsSystem)
        {
            throw new NotFoundException("El comentario");
        }
        await tasks.GetAsync(userId, comment.TaskId);
        await db.Connection.DeleteAsync(comment);
    }
}
