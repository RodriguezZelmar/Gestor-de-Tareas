using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TaskManager.App.Services;
using TaskManager.Core.Domain;
using TaskManager.Core.Services;

namespace TaskManager.App.ViewModels;

public sealed class TaskCardItem(TaskCard card, DateTime nowUtc)
{
    public int Id => card.Task.Id;
    public string Title => card.Task.Title;
    public TaskState State => card.Task.State;
    public string PriorityText => card.Task.Priority.Name();
    public string PriorityColor => Palette.PriorityColor(card.Task.Priority);
    public IReadOnlyList<LabelChip> Labels { get; } = card.Labels.Select(l => new LabelChip(l.Id, l.Name, l.Color)).ToList();
    public bool HasLabels => Labels.Count > 0;
    public bool HasDue => card.Task.DueUtc is not null;
    public string DueText => card.Task.DueUtc is { } due ? Formatting.Due(due) : "";
    public bool IsOverdue => card.Task.IsOverdue(nowUtc);
    public bool IsDone => card.Task.State == TaskState.Done;
    public int CommentCount => card.CommentCount;
    public bool HasComments => CommentCount > 0;
    public bool CanMoveBack => State != TaskState.ToDo;
    public bool CanMoveForward => State != TaskState.Done;
    public string BackText => CanMoveBack ? "← " + (State - 1).Name() : "";
    public string ForwardText => CanMoveForward ? (State + 1).Name() + " →" : "";
}

public sealed partial class BoardColumn(TaskState state) : ObservableObject
{
    public TaskState State { get; } = state;

    public string Title => State.Name();

    public string AccentColor => State switch
    {
        TaskState.ToDo => "#546E7A",
        TaskState.InProgress => "#1E88E5",
        _ => "#43A047",
    };

    public ObservableCollection<TaskCardItem> Cards { get; } = [];

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private int _count;
}

public sealed partial class BoardViewModel(TaskService tasks, ProjectService projects, LabelService labels, AppSession session, TimeProvider clock)
    : ViewModelBase, IQueryAttributable
{
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private CancellationTokenSource? _searchDebounce;
    private bool _filtersReady;
    private TaskCardItem? _dragged;

    public int ProjectId { get; private set; }

    public ObservableCollection<BoardColumn> Columns { get; } = new(Enum.GetValues<TaskState>().Select(s => new BoardColumn(s)));

    public IReadOnlyList<Option<Priority?>> PriorityOptions { get; } =
    [
        new("Todas las prioridades", null),
        .. Enum.GetValues<Priority>().Reverse().Select(p => new Option<Priority?>("Prioridad " + p.Name().ToLowerInvariant(), p)),
    ];

    public IReadOnlyList<Option<DueFilter>> DueOptions { get; } = Enum.GetValues<DueFilter>().Select(d => new Option<DueFilter>(d.Name(), d)).ToList();

    public ObservableCollection<Option<int?>> LabelOptions { get; } = [];

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _projectColor = "#3F51B5";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private Option<Priority?>? _selectedPriority;

    [ObservableProperty]
    private Option<int?>? _selectedLabel;

    [ObservableProperty]
    private Option<DueFilter>? _selectedDue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FiltersVisible))]
    private bool _hasActiveFilters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FiltersVisible))]
    private bool _isWide = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FiltersVisible))]
    private bool _showFilters;

    /// <summary>En pantallas angostas los selectores de filtro se pliegan detrás del botón "Filtros".</summary>
    public bool FiltersVisible => IsWide || ShowFilters || HasActiveFilters;

    [ObservableProperty]
    private TaskState _selectedState = TaskState.ToDo;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var id = Query.Int(query, "projectId");
        if (id != 0 && id != ProjectId)
        {
            ProjectId = id;
            _filtersReady = false;
        }
    }

    /// <summary>Carga el tablero. La primera vez también arma los filtros.</summary>
    public async Task LoadAsync()
    {
        if (!_filtersReady)
        {
            await RunAsync(async () =>
            {
                var project = await projects.GetAsync(session.UserId, ProjectId);
                ProjectName = project.Name;
                ProjectColor = project.Color;

                LabelOptions.Clear();
                LabelOptions.Add(new Option<int?>("Todas las etiquetas", null));
                foreach (var label in await labels.ListAsync(session.UserId))
                {
                    LabelOptions.Add(new Option<int?>(label.Name, label.Id));
                }
                SelectedPriority = PriorityOptions[0];
                SelectedLabel = LabelOptions[0];
                SelectedDue = DueOptions[0];
                SearchText = "";
                _filtersReady = true;
            });
        }
        await ReloadAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = DebouncedReloadAsync();

    partial void OnSelectedPriorityChanged(Option<Priority?>? value) => _ = ReloadAsync();

    partial void OnSelectedLabelChanged(Option<int?>? value) => _ = ReloadAsync();

    partial void OnSelectedDueChanged(Option<DueFilter>? value) => _ = ReloadAsync();

    partial void OnIsWideChanged(bool value) => UpdateColumnVisibility();

    partial void OnSelectedStateChanged(TaskState value) => UpdateColumnVisibility();

    private TaskFilter CurrentFilter => new(
        SearchText,
        SelectedPriority?.Value,
        SelectedLabel?.Value,
        SelectedDue?.Value ?? DueFilter.All);

    private async Task DebouncedReloadAsync()
    {
        _searchDebounce?.Cancel();
        var cts = _searchDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            await ReloadAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }

    /// <summary>Recarga las columnas. Serializada para que cambios rápidos de filtros no se pisen.</summary>
    private async Task ReloadAsync()
    {
        if (!_filtersReady)
        {
            return;
        }
        await _reloadGate.WaitAsync();
        try
        {
            var filter = CurrentFilter;
            HasActiveFilters = filter.IsActive;
            var board = await tasks.GetBoardAsync(session.UserId, ProjectId, filter);
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var column in Columns)
            {
                column.Cards.Clear();
                foreach (var card in board[column.State])
                {
                    column.Cards.Add(new TaskCardItem(card, now));
                }
                column.Count = column.Cards.Count;
            }
            UpdateColumnVisibility();
        }
        catch (DomainException ex)
        {
            await Dialogs.InfoAsync("Tablero", ex.Message);
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private void UpdateColumnVisibility()
    {
        foreach (var column in Columns)
        {
            column.IsSelected = column.State == SelectedState;
            column.IsVisible = IsWide || column.IsSelected;
        }
    }

    [RelayCommand]
    private void SelectColumn(BoardColumn column) => SelectedState = column.State;

    [RelayCommand]
    private void ToggleFilters() => ShowFilters = !ShowFilters;

    [RelayCommand]
    private void ClearFilters()
    {
        _filtersReady = false;
        SearchText = "";
        SelectedPriority = PriorityOptions[0];
        SelectedLabel = LabelOptions.FirstOrDefault();
        SelectedDue = DueOptions[0];
        _filtersReady = true;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private Task NewTaskAsync(BoardColumn? column)
    {
        var state = column?.State ?? (IsWide ? TaskState.ToDo : SelectedState);
        return Shell.Current.GoToAsync($"task?projectId={ProjectId}&taskId=0&state={(int)state}");
    }

    [RelayCommand]
    private Task OpenTaskAsync(TaskCardItem card) => Shell.Current.GoToAsync($"task?projectId={ProjectId}&taskId={card.Id}");

    [RelayCommand]
    private Task EditProjectAsync() => Shell.Current.GoToAsync($"projectedit?projectId={ProjectId}");

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    // --- Arrastrar y soltar ---

    [RelayCommand]
    private void StartDrag(TaskCardItem card) => _dragged = card;

    [RelayCommand]
    private void DragOver(BoardColumn column) => column.IsDropTarget = _dragged is not null;

    [RelayCommand]
    private void DragLeave(BoardColumn column) => column.IsDropTarget = false;

    /// <summary>Soltar sobre una tarjeta: la tarea arrastrada queda justo antes de ella.</summary>
    [RelayCommand]
    private Task DropOnCardAsync(TaskCardItem target)
    {
        var dragged = TakeDragged();
        return dragged is null || dragged.Id == target.Id ? Task.CompletedTask : MoveAsync(dragged, target.State, target.Id);
    }

    /// <summary>Soltar sobre el espacio libre de una columna: va al final.</summary>
    [RelayCommand]
    private Task DropOnColumnAsync(BoardColumn column)
    {
        var dragged = TakeDragged();
        return dragged is null ? Task.CompletedTask : MoveAsync(dragged, column.State, null);
    }

    // La tarjeta y su columna reciben el mismo "drop"; sólo lo procesa el primero que lo toma.
    private TaskCardItem? TakeDragged()
    {
        var dragged = _dragged;
        _dragged = null;
        foreach (var column in Columns)
        {
            column.IsDropTarget = false;
        }
        return dragged;
    }

    // --- Mover sin arrastrar (deslizar en Android, menú contextual en Windows) ---

    [RelayCommand]
    private Task MoveForwardAsync(TaskCardItem card) => card.CanMoveForward ? MoveAsync(card, card.State + 1, null) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveBackAsync(TaskCardItem card) => card.CanMoveBack ? MoveAsync(card, card.State - 1, null) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveToToDoAsync(TaskCardItem card) => MoveAsync(card, TaskState.ToDo, null);

    [RelayCommand]
    private Task MoveToInProgressAsync(TaskCardItem card) => MoveAsync(card, TaskState.InProgress, null);

    [RelayCommand]
    private Task MoveToDoneAsync(TaskCardItem card) => MoveAsync(card, TaskState.Done, null);

    [RelayCommand]
    private async Task DeleteTaskAsync(TaskCardItem card)
    {
        if (await Dialogs.ConfirmAsync("Eliminar tarea", $"¿Eliminar «{card.Title}» y sus comentarios?", "Eliminar"))
        {
            await RunAsync(() => tasks.DeleteAsync(session.UserId, card.Id));
            await ReloadAsync();
        }
    }

    private async Task MoveAsync(TaskCardItem card, TaskState state, int? beforeTaskId)
    {
        await RunAsync(() => tasks.MoveAsync(session.UserId, card.Id, state, beforeTaskId));
        await ReloadAsync();
    }
}
