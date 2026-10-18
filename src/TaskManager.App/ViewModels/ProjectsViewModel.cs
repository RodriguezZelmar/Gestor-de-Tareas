using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TaskManager.App.Services;
using TaskManager.Core.Services;

namespace TaskManager.App.ViewModels;

public sealed record ProjectItem(ProjectSummary Summary)
{
    public int Id => Summary.Project.Id;
    public string Name => Summary.Project.Name;
    public string Description => Summary.Project.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public string Color => Summary.Project.Color;
    public string Counts => $"{Summary.ToDo} por hacer · {Summary.InProgress} en proceso · {Summary.Done} completadas";
    public double Progress => Summary.Total == 0 ? 0 : (double)Summary.Done / Summary.Total;
    public bool HasOverdue => Summary.Overdue > 0;
    public string OverdueText => Summary.Overdue == 1 ? "1 vencida" : $"{Summary.Overdue} vencidas";
}

public sealed partial class ProjectsViewModel(ProjectService projects, TaskService tasks, AppSession session) : ViewModelBase
{
    public ObservableCollection<ProjectItem> Projects { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlerts))]
    private string _alertText = "";

    [ObservableProperty]
    private bool _isEmpty;

    public bool HasAlerts => !string.IsNullOrEmpty(AlertText);

    public string Greeting => $"Hola, {session.User.DisplayName.Split(' ')[0]}";

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        var list = await projects.ListAsync(session.UserId);
        Projects.Clear();
        foreach (var p in list)
        {
            Projects.Add(new ProjectItem(p));
        }
        IsEmpty = Projects.Count == 0;
        OnPropertyChanged(nameof(Greeting));

        var alerts = await tasks.GetDueAlertsAsync(session.UserId, TimeSpan.FromHours(24));
        var overdue = alerts.Count(a => a.IsOverdue);
        var soon = alerts.Count - overdue;
        AlertText = (overdue, soon) switch
        {
            (0, 0) => "",
            (_, 0) => $"Tenés {overdue} {(overdue == 1 ? "tarea vencida" : "tareas vencidas")}.",
            (0, _) => $"{soon} {(soon == 1 ? "tarea vence" : "tareas vencen")} en las próximas 24 h.",
            _ => $"Tenés {overdue} {(overdue == 1 ? "tarea vencida" : "tareas vencidas")} y {soon} por vencer en 24 h.",
        };
    });

    /// <summary>Aviso de vencimientos una vez por sesión, al entrar a la lista de tableros.</summary>
    public async Task ShowAlertReminderAsync()
    {
        if (!HasAlerts || session.AlertsShown)
        {
            return;
        }
        session.AlertsShown = true;
        if (await Dialogs.ConfirmAsync("Recordatorio de vencimientos", AlertText, "Ver alertas", "Más tarde"))
        {
            await Shell.Current.GoToAsync("//alerts");
        }
    }

    [RelayCommand]
    private Task OpenAsync(ProjectItem item) => Shell.Current.GoToAsync($"board?projectId={item.Id}");

    [RelayCommand]
    private Task NewAsync() => Shell.Current.GoToAsync("projectedit?projectId=0");

    [RelayCommand]
    private Task ShowAlertsAsync() => Shell.Current.GoToAsync("//alerts");

    [RelayCommand]
    private async Task MoreAsync(ProjectItem item)
    {
        switch (await Dialogs.ChooseAsync(item.Name, "Abrir", "Editar", "Eliminar"))
        {
            case "Abrir":
                await OpenAsync(item);
                break;
            case "Editar":
                await Shell.Current.GoToAsync($"projectedit?projectId={item.Id}");
                break;
            case "Eliminar":
                await DeleteAsync(item);
                break;
        }
    }

    private async Task DeleteAsync(ProjectItem item)
    {
        if (!await Dialogs.ConfirmAsync("Eliminar tablero",
                $"Se eliminará «{item.Name}» con sus {item.Summary.Total} tareas y comentarios.", "Eliminar"))
        {
            return;
        }
        await RunAsync(() => projects.DeleteAsync(session.UserId, item.Id));
        await LoadAsync();
    }
}

public sealed partial class ProjectEditorViewModel(ProjectService projects, AppSession session) : ViewModelBase, IQueryAttributable
{
    private int _projectId;

    public ObservableCollection<ColorChoice> Colors { get; } = new(Palette.Colors.Select(c => new ColorChoice(c)));

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _title = "Nuevo tablero";

    public bool IsExisting => _projectId != 0;

    private string SelectedColor => Colors.FirstOrDefault(c => c.IsSelected)?.Hex ?? Palette.Colors[4];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _projectId = Query.Int(query, "projectId");
        OnPropertyChanged(nameof(IsExisting));
        _ = RunAsync(async () =>
        {
            var color = Palette.Colors[4];
            if (IsExisting)
            {
                var project = await projects.GetAsync(session.UserId, _projectId);
                Name = project.Name;
                Description = project.Description;
                color = project.Color;
                Title = "Editar tablero";
            }
            SelectColor(Colors.FirstOrDefault(c => c.Hex == color) ?? Colors[4]);
        });
    }

    [RelayCommand]
    private void SelectColor(ColorChoice choice)
    {
        foreach (var c in Colors)
        {
            c.IsSelected = c == choice;
        }
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (IsExisting)
        {
            await projects.UpdateAsync(session.UserId, _projectId, Name, Description, SelectedColor);
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            var created = await projects.CreateAsync(session.UserId, Name, Description, SelectedColor);
            await Shell.Current.GoToAsync($"../board?projectId={created.Id}");
        }
    }, inlineErrors: true);

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");
}
