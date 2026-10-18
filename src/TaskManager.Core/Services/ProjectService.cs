using SQLite;

using TaskManager.Core.Data;
using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.Core.Services;

public sealed class ProjectService(AppDatabase db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(int userId)
    {
        var projects = await db.Connection.Table<Project>().Where(p => p.OwnerId == userId).ToListAsync();
        var ids = projects.Select(p => p.Id).ToList();
        var tasks = await db.Connection.Table<TaskItem>().Where(t => ids.Contains(t.ProjectId)).ToListAsync();
        var byProject = tasks.ToLookup(t => t.ProjectId);

        return projects
            .OrderByDescending(p => p.UpdatedUtc)
            .Select(p =>
            {
                var list = byProject[p.Id].ToList();
                return new ProjectSummary(p,
                    list.Count(t => t.State == TaskState.ToDo),
                    list.Count(t => t.State == TaskState.InProgress),
                    list.Count(t => t.State == TaskState.Done),
                    list.Count(t => t.IsOverdue(Now)));
            })
            .ToList();
    }

    public async Task<Project> GetAsync(int userId, int projectId)
    {
        var project = await db.Connection.FindAsync<Project>(projectId);
        return project is not null && project.OwnerId == userId ? project : throw new NotFoundException("El tablero");
    }

    public async Task<Project> CreateAsync(int userId, string? name, string? description, string? color)
    {
        var project = new Project
        {
            OwnerId = userId,
            Name = InputSanitizer.Text(name, "El nombre del tablero", 80),
            Description = InputSanitizer.Text(description, "La descripción", 500, required: false, multiline: true),
            Color = InputSanitizer.Color(color),
            CreatedUtc = Now,
            UpdatedUtc = Now,
        };
        await db.Connection.InsertAsync(project);
        return project;
    }

    public async Task<Project> UpdateAsync(int userId, int projectId, string? name, string? description, string? color)
    {
        var project = await GetAsync(userId, projectId);
        project.Name = InputSanitizer.Text(name, "El nombre del tablero", 80);
        project.Description = InputSanitizer.Text(description, "La descripción", 500, required: false, multiline: true);
        project.Color = InputSanitizer.Color(color);
        project.UpdatedUtc = Now;
        await db.Connection.UpdateAsync(project);
        return project;
    }

    public async Task DeleteAsync(int userId, int projectId)
    {
        await GetAsync(userId, projectId);
        await db.Connection.RunInTransactionAsync(conn => DeleteProjectRows(conn, projectId));
    }

    /// <summary>Borrado en cascada de un tablero (tareas, etiquetas asignadas y comentarios). Debe correr en una transacción.</summary>
    internal static void DeleteProjectRows(SQLiteConnection conn, int projectId)
    {
        var taskIds = conn.Table<TaskItem>().Where(t => t.ProjectId == projectId).ToList().Select(t => t.Id).ToList();
        conn.Table<TaskLabel>().Delete(tl => taskIds.Contains(tl.TaskId));
        conn.Table<Comment>().Delete(c => taskIds.Contains(c.TaskId));
        conn.Table<TaskItem>().Delete(t => t.ProjectId == projectId);
        conn.Delete<Project>(projectId);
    }
}
