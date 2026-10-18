using TaskManager.Core.Domain;

namespace TaskManager.Core.Services;

public sealed record ProjectSummary(Project Project, int ToDo, int InProgress, int Done, int Overdue)
{
    public int Total => ToDo + InProgress + Done;
}

public sealed record TaskInput(
    string Title,
    string? Description,
    TaskState State,
    Priority Priority,
    DateTime? DueUtc,
    IReadOnlyCollection<int> LabelIds);

public sealed record TaskCard(TaskItem Task, IReadOnlyList<Label> Labels, int CommentCount);

public sealed record TaskDetails(TaskItem Task, Project Project, IReadOnlyList<int> LabelIds);

public sealed record CommentView(Comment Comment, string AuthorName);

public sealed record DueAlert(TaskItem Task, string ProjectName, bool IsOverdue);

public sealed record TaskFilter(string? Search = null, Priority? Priority = null, int? LabelId = null, DueFilter Due = DueFilter.All)
{
    public static TaskFilter None { get; } = new();

    public bool IsActive => !string.IsNullOrWhiteSpace(Search) || Priority is not null || LabelId is not null || Due != DueFilter.All;
}

public static class DateTimeExtensions
{
    /// <summary>sqlite-net devuelve las fechas guardadas como ticks con Kind = Unspecified; en la base siempre son UTC.</summary>
    public static DateTime AsUtc(this DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
