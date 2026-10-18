using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TaskManager.App.Services;
using TaskManager.Core.Domain;
using TaskManager.Core.Services;

namespace TaskManager.App.ViewModels;

public sealed record CommentItem(int Id, string Author, string Body, string When, bool IsSystem, bool CanDelete)
{
    public string Initial => string.IsNullOrEmpty(Author) ? "?" : Author[..1].ToUpperInvariant();
}

public sealed partial class TaskEditorViewModel(TaskService tasks, LabelService labels, CommentService comments, AppSession session)
    : ViewModelBase, IQueryAttributable
{
    private int _projectId;
    private int _taskId;

    public IReadOnlyList<Option<TaskState>> StateOptions { get; } =
        Enum.GetValues<TaskState>().Select(s => new Option<TaskState>(s.Name(), s)).ToList();

    public IReadOnlyList<Option<Priority>> PriorityOptions { get; } =
        Enum.GetValues<Priority>().Reverse().Select(p => new Option<Priority>(p.Name(), p)).ToList();

    public ObservableCollection<SelectableLabel> Labels { get; } = [];

    public ObservableCollection<CommentItem> Comments { get; } = [];

    [ObservableProperty]
    private string _pageTitle = "Nueva tarea";

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _taskTitle = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private Option<TaskState>? _selectedState;

    [ObservableProperty]
    private Option<Priority>? _selectedPriority;

    [ObservableProperty]
    private bool _hasDueDate;

    [ObservableProperty]
    private DateTime? _dueDate = DateTime.Today.AddDays(1);

    [ObservableProperty]
    private TimeSpan? _dueTime = new TimeSpan(18, 0, 0);

    [ObservableProperty]
    private string _newComment = "";

    [ObservableProperty]
    private string _meta = "";

    [ObservableProperty]
    private bool _hasLabels;

    public bool IsExisting => _taskId != 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _projectId = Query.Int(query, "projectId");
        _taskId = Query.Int(query, "taskId");
        var state = (TaskState)Query.Int(query, "state");
        var initialState = Enum.IsDefined(state) ? state : TaskState.ToDo;
        OnPropertyChanged(nameof(IsExisting));
        _ = LoadAsync(initialState);
    }

    private Task LoadAsync(TaskState initialState) => RunAsync(async () =>
    {
        var selectedLabels = new HashSet<int>();
        if (IsExisting)
        {
            var details = await tasks.GetAsync(session.UserId, _taskId);
            var task = details.Task;
            _projectId = task.ProjectId;
            PageTitle = "Editar tarea";
            ProjectName = details.Project.Name;
            TaskTitle = task.Title;
            Description = task.Description;
            SelectedState = StateOptions.First(o => o.Value == task.State);
            SelectedPriority = PriorityOptions.First(o => o.Value == task.Priority);
            HasDueDate = task.DueUtc is not null;
            if (task.DueUtc is { } due)
            {
                var local = due.AsUtc().ToLocalTime();
                DueDate = local.Date;
                DueTime = local.TimeOfDay;
            }
            Meta = $"Creada {Formatting.When(task.CreatedUtc)} · Actualizada {Formatting.When(task.UpdatedUtc)}"
                + (task.CompletedUtc is { } done ? $" · Completada {Formatting.When(done)}" : "");
            selectedLabels.UnionWith(details.LabelIds);
            await LoadCommentsAsync();
        }
        else
        {
            SelectedState = StateOptions.First(o => o.Value == initialState);
            SelectedPriority = PriorityOptions.First(o => o.Value == Priority.Medium);
        }

        Labels.Clear();
        foreach (var label in await labels.ListAsync(session.UserId))
        {
            Labels.Add(new SelectableLabel(new LabelChip(label.Id, label.Name, label.Color), selectedLabels.Contains(label.Id)));
        }
        HasLabels = Labels.Count > 0;
    });

    private async Task LoadCommentsAsync()
    {
        var list = await comments.ListAsync(session.UserId, _taskId);
        Comments.Clear();
        // Lo más reciente primero.
        foreach (var c in list.Reverse())
        {
            Comments.Add(new CommentItem(c.Comment.Id, c.AuthorName, c.Comment.Body, Formatting.When(c.Comment.CreatedUtc),
                c.Comment.IsSystem, !c.Comment.IsSystem && c.Comment.AuthorId == session.UserId));
        }
    }

    [RelayCommand]
    private void ToggleLabel(SelectableLabel label) => label.IsSelected = !label.IsSelected;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        DateTime? dueUtc = null;
        if (HasDueDate)
        {
            var date = (DueDate ?? DateTime.Today).Date + (DueTime ?? TimeSpan.Zero);
            dueUtc = DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();
        }
        var input = new TaskInput(
            TaskTitle,
            Description,
            SelectedState?.Value ?? TaskState.ToDo,
            SelectedPriority?.Value ?? Priority.Medium,
            dueUtc,
            Labels.Where(l => l.IsSelected).Select(l => l.Label.Id).ToList());

        if (IsExisting)
        {
            await tasks.UpdateAsync(session.UserId, _taskId, input);
        }
        else
        {
            await tasks.CreateAsync(session.UserId, _projectId, input);
        }
        await Shell.Current.GoToAsync("..");
    }, inlineErrors: true);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!IsExisting || !await Dialogs.ConfirmAsync("Eliminar tarea", $"¿Eliminar «{TaskTitle}» y sus comentarios?", "Eliminar"))
        {
            return;
        }
        await RunAsync(async () =>
        {
            await tasks.DeleteAsync(session.UserId, _taskId);
            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task AddCommentAsync() => RunAsync(async () =>
    {
        await comments.AddAsync(session.UserId, _taskId, NewComment);
        NewComment = "";
        await LoadCommentsAsync();
    });

    [RelayCommand]
    private async Task DeleteCommentAsync(CommentItem comment)
    {
        if (await Dialogs.ConfirmAsync("Eliminar comentario", "¿Eliminar este comentario?", "Eliminar"))
        {
            await RunAsync(async () =>
            {
                await comments.DeleteAsync(session.UserId, comment.Id);
                await LoadCommentsAsync();
            });
        }
    }
}
