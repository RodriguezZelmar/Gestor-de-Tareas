namespace TaskManager.Core.Domain;

/// <summary>Columnas del tablero. El valor numérico define el orden de izquierda a derecha.</summary>
public enum TaskState
{
    ToDo = 0,
    InProgress = 1,
    Done = 2,
}

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>Filtro por fecha de vencimiento.</summary>
public enum DueFilter
{
    All,
    Overdue,
    Today,
    Next7Days,
    NoDate,
}

public static class DomainText
{
    public static string Name(this TaskState state) => state switch
    {
        TaskState.ToDo => "Por hacer",
        TaskState.InProgress => "En proceso",
        TaskState.Done => "Completado",
        _ => state.ToString(),
    };

    public static string Name(this Priority priority) => priority switch
    {
        Priority.Low => "Baja",
        Priority.Medium => "Media",
        Priority.High => "Alta",
        _ => priority.ToString(),
    };

    public static string Name(this DueFilter filter) => filter switch
    {
        DueFilter.All => "Todas las fechas",
        DueFilter.Overdue => "Vencidas",
        DueFilter.Today => "Vencen hoy",
        DueFilter.Next7Days => "Próximos 7 días",
        DueFilter.NoDate => "Sin fecha",
        _ => filter.ToString(),
    };
}
