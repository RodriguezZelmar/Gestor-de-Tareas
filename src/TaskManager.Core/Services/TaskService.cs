using SQLite;

using TaskManager.Core.Data;
using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.Core.Services;

public sealed class TaskService(AppDatabase db, ProjectService projects, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Tarjetas del tablero agrupadas por columna, ordenadas y filtradas.</summary>
    public async Task<IReadOnlyDictionary<TaskState, IReadOnlyList<TaskCard>>> GetBoardAsync(int userId, int projectId, TaskFilter filter)
    {
        await projects.GetAsync(userId, projectId);
        var tasks = await db.Connection.Table<TaskItem>().Where(t => t.ProjectId == projectId).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();

        var labels = (await db.Connection.Table<Label>().Where(l => l.OwnerId == userId).ToListAsync()).ToDictionary(l => l.Id);
        var links = (await db.Connection.Table<TaskLabel>().Where(tl => taskIds.Contains(tl.TaskId)).ToListAsync())
            .ToLookup(tl => tl.TaskId, tl => tl.LabelId);
        var commentCounts = (await db.Connection.Table<Comment>().Where(c => taskIds.Contains(c.TaskId) && !c.IsSystem).ToListAsync())
            .GroupBy(c => c.TaskId)
            .ToDictionary(g => g.Key, g => g.Count());

        var cards = tasks
            .Select(t => new TaskCard(t,
                links[t.Id].Where(labels.ContainsKey).Select(id => labels[id]).OrderBy(l => l.Name).ToList(),
                commentCounts.GetValueOrDefault(t.Id)))
            .Where(c => Matches(c, filter))
            .ToList();

        return Enum.GetValues<TaskState>().ToDictionary(
            s => s,
            s => (IReadOnlyList<TaskCard>)cards.Where(c => c.Task.State == s).OrderBy(c => c.Task.Position).ThenBy(c => c.Task.Id).ToList());
    }

    public async Task<TaskDetails> GetAsync(int userId, int taskId)
    {
        var (task, project) = await LoadOwnedAsync(userId, taskId);
        var labelIds = (await db.Connection.Table<TaskLabel>().Where(tl => tl.TaskId == taskId).ToListAsync()).Select(tl => tl.LabelId).ToList();
        return new TaskDetails(task, project, labelIds);
    }

    public async Task<TaskItem> CreateAsync(int userId, int projectId, TaskInput input)
    {
        await projects.GetAsync(userId, projectId);
        var labelIds = await ValidLabelIdsAsync(userId, input.LabelIds);
        var task = new TaskItem { ProjectId = projectId, CreatedUtc = Now };
        Apply(task, input);

        await db.Connection.RunInTransactionAsync(conn =>
        {
            task.Position = conn.Table<TaskItem>().Where(t => t.ProjectId == projectId && t.State == task.State).Count();
            conn.Insert(task);
            SetLabels(conn, task.Id, labelIds);
            AddHistory(conn, task.Id, userId, $"Creó la tarea en «{task.State.Name()}».");
        });
        await TouchProjectAsync(projectId);
        return task;
    }

    public async Task<TaskItem> UpdateAsync(int userId, int taskId, TaskInput input)
    {
        var (task, project) = await LoadOwnedAsync(userId, taskId);
        var labelIds = await ValidLabelIdsAsync(userId, input.LabelIds);
        var previousState = task.State;
        var previousPriority = task.Priority;
        Apply(task, input);

        await db.Connection.RunInTransactionAsync(conn =>
        {
            if (previousState != task.State)
            {
                // Al cambiar de columna desde el editor la tarea va al final de la nueva columna.
                task.Position = conn.Table<TaskItem>().Where(t => t.ProjectId == project.Id && t.State == task.State).Count();
                AddHistory(conn, task.Id, userId, $"Movió la tarea de «{previousState.Name()}» a «{task.State.Name()}».");
            }
            if (previousPriority != task.Priority)
            {
                AddHistory(conn, task.Id, userId, $"Cambió la prioridad de {previousPriority.Name()} a {task.Priority.Name()}.");
            }
            conn.Update(task);
            SetLabels(conn, task.Id, labelIds);
            if (previousState != task.State)
            {
                Renumber(conn, project.Id, previousState);
            }
        });
        await TouchProjectAsync(project.Id);
        return task;
    }

    /// <summary>
    /// Mueve una tarea a <paramref name="state"/>, justo antes de <paramref name="beforeTaskId"/> o al final de la columna.
    /// Es la operación de arrastrar y soltar.
    /// </summary>
    public async Task MoveAsync(int userId, int taskId, TaskState state, int? beforeTaskId = null)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ValidationException("Estado inválido.");
        }
        var (task, project) = await LoadOwnedAsync(userId, taskId);
        if (beforeTaskId == taskId)
        {
            return;
        }
        var previousState = task.State;

        await db.Connection.RunInTransactionAsync(conn =>
        {
            var column = conn.Table<TaskItem>().Where(t => t.ProjectId == project.Id && t.State == state && t.Id != taskId)
                .OrderBy(t => t.Position).ToList();
            var index = beforeTaskId is { } before ? column.FindIndex(t => t.Id == before) : -1;
            if (index < 0)
            {
                index = column.Count;
            }

            task.State = state;
            task.UpdatedUtc = Now;
            task.CompletedUtc = state == TaskState.Done ? task.CompletedUtc ?? Now : null;
            column.Insert(index, task);
            for (var i = 0; i < column.Count; i++)
            {
                column[i].Position = i;
                conn.Update(column[i]);
            }

            if (previousState != state)
            {
                Renumber(conn, project.Id, previousState);
                AddHistory(conn, taskId, userId, $"Movió la tarea de «{previousState.Name()}» a «{state.Name()}».");
            }
        });
        await TouchProjectAsync(project.Id);
    }

    public async Task DeleteAsync(int userId, int taskId)
    {
        var (task, project) = await LoadOwnedAsync(userId, taskId);
        await db.Connection.RunInTransactionAsync(conn =>
        {
            conn.Table<TaskLabel>().Delete(tl => tl.TaskId == taskId);
            conn.Table<Comment>().Delete(c => c.TaskId == taskId);
            conn.Delete<TaskItem>(taskId);
            Renumber(conn, project.Id, task.State);
        });
        await TouchProjectAsync(project.Id);
    }

    /// <summary>Tareas sin completar vencidas o que vencen dentro de <paramref name="window"/>, en todos los tableros.</summary>
    public async Task<IReadOnlyList<DueAlert>> GetDueAlertsAsync(int userId, TimeSpan window)
    {
        var owned = await db.Connection.Table<Project>().Where(p => p.OwnerId == userId).ToListAsync();
        var names = owned.ToDictionary(p => p.Id, p => p.Name);
        var ids = names.Keys.ToList();
        var limit = Now + window;
        var tasks = await db.Connection.Table<TaskItem>()
            .Where(t => ids.Contains(t.ProjectId) && t.State != TaskState.Done && t.DueUtc != null && t.DueUtc <= limit)
            .ToListAsync();

        return tasks
            .OrderBy(t => t.DueUtc)
            .Select(t => new DueAlert(t, names[t.ProjectId], t.DueUtc!.Value.AsUtc() < Now))
            .ToList();
    }

    private void AddHistory(SQLiteConnection conn, int taskId, int userId, string text) =>
        conn.Insert(new Comment { TaskId = taskId, AuthorId = userId, Body = text, IsSystem = true, CreatedUtc = Now });

    private bool Matches(TaskCard card, TaskFilter filter)
    {
        var task = card.Task;
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            if (!task.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                && !task.Description.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }
        if (filter.Priority is { } priority && task.Priority != priority)
        {
            return false;
        }
        if (filter.LabelId is { } labelId && card.Labels.All(l => l.Id != labelId))
        {
            return false;
        }
        if (filter.Due == DueFilter.All)
        {
            return true;
        }
        if (task.DueUtc is not { } dueRaw)
        {
            return filter.Due == DueFilter.NoDate;
        }

        // "Hoy" y "próximos 7 días" se evalúan en la zona horaria local del dispositivo.
        var zone = clock.LocalTimeZone;
        var today = TimeZoneInfo.ConvertTimeFromUtc(Now, zone).Date;
        var dueLocal = TimeZoneInfo.ConvertTimeFromUtc(dueRaw.AsUtc(), zone);
        return filter.Due switch
        {
            DueFilter.Overdue => task.IsOverdue(Now),
            DueFilter.Today => dueLocal.Date == today,
            DueFilter.Next7Days => dueLocal.Date >= today && dueLocal.Date <= today.AddDays(7),
            _ => false,
        };
    }

    private void Apply(TaskItem task, TaskInput input)
    {
        if (!Enum.IsDefined(input.State) || !Enum.IsDefined(input.Priority))
        {
            throw new ValidationException("Estado o prioridad inválidos.");
        }
        task.Title = InputSanitizer.Text(input.Title, "El título", 120);
        task.Description = InputSanitizer.Text(input.Description, "La descripción", 4000, required: false, multiline: true);
        task.Priority = input.Priority;
        task.DueUtc = input.DueUtc?.ToUniversalTime();
        if (task.State != input.State || task.CompletedUtc is null && input.State == TaskState.Done)
        {
            task.CompletedUtc = input.State == TaskState.Done ? Now : null;
        }
        task.State = input.State;
        task.UpdatedUtc = Now;
    }

    private async Task<(TaskItem Task, Project Project)> LoadOwnedAsync(int userId, int taskId)
    {
        var task = await db.Connection.FindAsync<TaskItem>(taskId) ?? throw new NotFoundException("La tarea");
        var project = await db.Connection.FindAsync<Project>(task.ProjectId);
        return project is not null && project.OwnerId == userId ? (task, project) : throw new NotFoundException("La tarea");
    }

    /// <summary>Descarta ids de etiquetas que no son del usuario (evita asignar etiquetas ajenas).</summary>
    private async Task<List<int>> ValidLabelIdsAsync(int userId, IReadOnlyCollection<int> requested)
    {
        if (requested.Count == 0)
        {
            return [];
        }
        var wanted = requested.Distinct().ToList();
        var owned = await db.Connection.Table<Label>().Where(l => l.OwnerId == userId && wanted.Contains(l.Id)).ToListAsync();
        return owned.Select(l => l.Id).ToList();
    }

    private static void SetLabels(SQLiteConnection conn, int taskId, List<int> labelIds)
    {
        conn.Table<TaskLabel>().Delete(tl => tl.TaskId == taskId);
        conn.InsertAll(labelIds.Select(id => new TaskLabel { TaskId = taskId, LabelId = id }));
    }

    private static void Renumber(SQLiteConnection conn, int projectId, TaskState state)
    {
        var column = conn.Table<TaskItem>().Where(t => t.ProjectId == projectId && t.State == state).OrderBy(t => t.Position).ToList();
        for (var i = 0; i < column.Count; i++)
        {
            if (column[i].Position != i)
            {
                column[i].Position = i;
                conn.Update(column[i]);
            }
        }
    }

    private Task TouchProjectAsync(int projectId) =>
        db.Connection.ExecuteAsync("UPDATE projects SET UpdatedUtc = ? WHERE Id = ?", Now.Ticks, projectId);
}
