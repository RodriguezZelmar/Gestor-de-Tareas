using CommunityToolkit.Mvvm.ComponentModel;

using TaskManager.Core.Domain;

namespace TaskManager.App.ViewModels;

public static class Palette
{
    /// <summary>Colores Material para tableros y etiquetas.</summary>
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#E53935", "#D81B60", "#8E24AA", "#5E35B1", "#3F51B5", "#1E88E5",
        "#00897B", "#43A047", "#7CB342", "#FDD835", "#FB8C00", "#6D4C41", "#546E7A",
    ];

    public static string PriorityColor(Priority priority) => priority switch
    {
        Priority.High => "#E53935",
        Priority.Medium => "#FB8C00",
        _ => "#43A047",
    };
}

public sealed record LabelChip(int Id, string Name, string Color);

/// <summary>Opción de un selector (Picker) con texto visible y valor.</summary>
public sealed record Option<T>(string Text, T Value)
{
    public override string ToString() => Text;
}

public sealed partial class ColorChoice(string hex) : ObservableObject
{
    public string Hex { get; } = hex;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class SelectableLabel(LabelChip label, bool selected) : ObservableObject
{
    public LabelChip Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected = selected;
}

public static class Query
{
    /// <summary>Lee un parámetro entero de la navegación de Shell (0 si falta o no es válido).</summary>
    public static int Int(IDictionary<string, object> query, string key) =>
        query.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var number) ? number : 0;
}

public static class Formatting
{
    /// <summary>Texto corto de vencimiento en hora local: "Hoy 18:00", "Mañana", "12 oct".</summary>
    public static string Due(DateTime dueUtc)
    {
        var local = DateTime.SpecifyKind(dueUtc, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.Today;
        var time = local.TimeOfDay == TimeSpan.Zero ? "" : " " + local.ToString("HH:mm");
        return (local.Date - today).Days switch
        {
            0 => "Hoy" + time,
            1 => "Mañana" + time,
            -1 => "Ayer" + time,
            _ => local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy") + time,
        };
    }

    public static string When(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.Date == DateTime.Today ? "Hoy " + local.ToString("HH:mm") : local.ToString("d MMM yyyy HH:mm");
    }
}
