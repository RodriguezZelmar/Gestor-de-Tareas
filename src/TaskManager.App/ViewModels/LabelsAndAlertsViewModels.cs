using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TaskManager.App.Services;
using TaskManager.Core.Services;

namespace TaskManager.App.ViewModels;

public sealed partial class LabelsViewModel(LabelService labels, AppSession session) : ViewModelBase
{
    private int _editingId;

    public ObservableCollection<LabelChip> Labels { get; } = [];

    public ObservableCollection<ColorChoice> Colors { get; } = new(Palette.Colors.Select(c => new ColorChoice(c)));

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _formTitle = "Nueva etiqueta";

    [ObservableProperty]
    private bool _isEditing;

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        Labels.Clear();
        foreach (var l in await labels.ListAsync(session.UserId))
        {
            Labels.Add(new LabelChip(l.Id, l.Name, l.Color));
        }
        if (!Colors.Any(c => c.IsSelected))
        {
            SelectColor(Colors[5]);
        }
    });

    [RelayCommand]
    private void SelectColor(ColorChoice choice)
    {
        foreach (var c in Colors)
        {
            c.IsSelected = c == choice;
        }
    }

    [RelayCommand]
    private void Edit(LabelChip label)
    {
        _editingId = label.Id;
        Name = label.Name;
        SelectColor(Colors.FirstOrDefault(c => c.Hex == label.Color) ?? Colors[0]);
        FormTitle = "Editar etiqueta";
        IsEditing = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editingId = 0;
        Name = "";
        FormTitle = "Nueva etiqueta";
        IsEditing = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var color = Colors.FirstOrDefault(c => c.IsSelected)?.Hex ?? Palette.Colors[5];
        var saved = false;
        await RunAsync(async () =>
        {
            if (_editingId == 0)
            {
                await labels.CreateAsync(session.UserId, Name, color);
            }
            else
            {
                await labels.UpdateAsync(session.UserId, _editingId, Name, color);
            }
            saved = true;
        }, inlineErrors: true);

        if (saved)
        {
            CancelEdit();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(LabelChip label)
    {
        if (!await Dialogs.ConfirmAsync("Eliminar etiqueta", $"Se quitará «{label.Name}» de todas las tareas.", "Eliminar"))
        {
            return;
        }
        await RunAsync(() => labels.DeleteAsync(session.UserId, label.Id));
        if (_editingId == label.Id)
        {
            CancelEdit();
        }
        await LoadAsync();
    }
}

public sealed record AlertItem(DueAlert Alert)
{
    public string Title => Alert.Task.Title;
    public string ProjectName => Alert.ProjectName;
    public string DueText => (Alert.IsOverdue ? "Venció " : "Vence ") + Formatting.Due(Alert.Task.DueUtc!.Value);
    public bool IsOverdue => Alert.IsOverdue;
}

public sealed class AlertGroup(string title, IEnumerable<AlertItem> items) : List<AlertItem>(items)
{
    public string Title { get; } = title;
}

public sealed partial class AlertsViewModel(TaskService tasks, AppSession session) : ViewModelBase
{
    public ObservableCollection<AlertGroup> Groups { get; } = [];

    [ObservableProperty]
    private bool _isEmpty;

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        var alerts = (await tasks.GetDueAlertsAsync(session.UserId, TimeSpan.FromDays(7))).Select(a => new AlertItem(a)).ToList();
        var now = DateTime.UtcNow;
        Groups.Clear();
        AddGroup("Vencidas", alerts.Where(a => a.IsOverdue));
        AddGroup("Próximas 24 horas", alerts.Where(a => !a.IsOverdue && a.Alert.Task.DueUtc!.Value.AsUtc() <= now.AddHours(24)));
        AddGroup("Próximos 7 días", alerts.Where(a => !a.IsOverdue && a.Alert.Task.DueUtc!.Value.AsUtc() > now.AddHours(24)));
        IsEmpty = Groups.Count == 0;
    });

    private void AddGroup(string title, IEnumerable<AlertItem> items)
    {
        var group = new AlertGroup(title, items);
        if (group.Count > 0)
        {
            Groups.Add(group);
        }
    }

    [RelayCommand]
    private Task OpenAsync(AlertItem item) =>
        Shell.Current.GoToAsync($"task?projectId={item.Alert.Task.ProjectId}&taskId={item.Alert.Task.Id}");
}
