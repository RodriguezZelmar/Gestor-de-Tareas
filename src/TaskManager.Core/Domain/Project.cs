using SQLite;

namespace TaskManager.Core.Domain;

/// <summary>Tablero de tareas. Pertenece a un único usuario.</summary>
[Table("projects")]
public sealed class Project
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int OwnerId { get; set; }

    [NotNull, MaxLength(80)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string Description { get; set; } = "";

    [NotNull, MaxLength(7)]
    public string Color { get; set; } = "#3F51B5";

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Etiqueta de color reutilizable en todos los tableros del usuario.</summary>
[Table("labels")]
public sealed class Label
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int OwnerId { get; set; }

    [NotNull, MaxLength(30)]
    public string Name { get; set; } = "";

    [NotNull, MaxLength(7)]
    public string Color { get; set; } = "#9E9E9E";
}
