using SQLite;

namespace TaskManager.Core.Domain;

[Table("tasks")]
public sealed class TaskItem
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ProjectId { get; set; }

    [NotNull, MaxLength(120)]
    public string Title { get; set; } = "";

    [MaxLength(4000)]
    public string Description { get; set; } = "";

    public TaskState State { get; set; }

    public Priority Priority { get; set; } = Priority.Medium;

    public DateTime? DueUtc { get; set; }

    /// <summary>Orden dentro de la columna (0 = arriba).</summary>
    public int Position { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public bool IsOverdue(DateTime nowUtc) => State != TaskState.Done && DueUtc is { } due && due < nowUtc;
}

[Table("task_labels")]
public sealed class TaskLabel
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "ix_task_label", Order = 1, Unique = true)]
    public int TaskId { get; set; }

    [Indexed(Name = "ix_task_label", Order = 2, Unique = true)]
    public int LabelId { get; set; }
}

/// <summary>Comentario o evento del historial de una tarea.</summary>
[Table("comments")]
public sealed class Comment
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TaskId { get; set; }

    public int AuthorId { get; set; }

    [NotNull, MaxLength(2000)]
    public string Body { get; set; } = "";

    /// <summary>true para entradas automáticas (creación, cambios de estado).</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedUtc { get; set; }
}
