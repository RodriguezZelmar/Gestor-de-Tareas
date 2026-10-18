using TaskManager.Core.Domain;
using TaskManager.Core.Security;
using TaskManager.Core.Services;

namespace TaskManager.Core.Tests;

public class AuthTests
{
    [Fact]
    public async Task Register_starts_a_session_that_survives_restart()
    {
        await using var h = await TestHarness.CreateAsync();
        var user = await h.Auth.RegisterAsync("  Ana@Example.COM ", "Ana", "Secreta123");

        Assert.Equal("ana@example.com", user.Email);
        var token = await h.Secrets.GetAsync(AuthService.SessionTokenKey);
        Assert.False(string.IsNullOrEmpty(token));

        // En la base sólo está el hash del token.
        var session = await h.Db.Connection.Table<SessionRecord>().FirstAsync();
        Assert.Equal(Tokens.Sha256Hex(token!), session.TokenHash);
        Assert.NotEqual(token, session.TokenHash);

        var restored = await h.Auth.RestoreSessionAsync();
        Assert.Equal(user.Id, restored?.Id);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        await using var h = await TestHarness.CreateAsync();
        await h.RegisterAsync();
        await Assert.ThrowsAsync<ValidationException>(() => h.Auth.RegisterAsync("ANA@example.com", "Otra", "Secreta123"));
    }

    [Fact]
    public async Task Login_errors_do_not_reveal_which_part_failed()
    {
        await using var h = await TestHarness.CreateAsync();
        await h.RegisterAsync();

        var wrongPassword = await Assert.ThrowsAsync<AuthException>(() => h.Auth.LoginAsync("ana@example.com", "Incorrecta1"));
        var unknownEmail = await Assert.ThrowsAsync<AuthException>(() => h.Auth.LoginAsync("nadie@example.com", "Incorrecta1"));
        Assert.Equal(wrongPassword.Message, unknownEmail.Message);
    }

    [Fact]
    public async Task Too_many_failures_lock_the_account_temporarily()
    {
        await using var h = await TestHarness.CreateAsync();
        await h.RegisterAsync();
        await h.Auth.LogoutAsync();

        for (var i = 0; i < AuthService.MaxFailedLogins; i++)
        {
            await Assert.ThrowsAsync<AuthException>(() => h.Auth.LoginAsync("ana@example.com", "Incorrecta1"));
        }
        var locked = await Assert.ThrowsAsync<AuthException>(() => h.Auth.LoginAsync("ana@example.com", "Secreta123"));
        Assert.Contains("intentos", locked.Message);

        h.Clock.Advance(AuthService.LockoutDuration + TimeSpan.FromSeconds(1));
        var user = await h.Auth.LoginAsync("ana@example.com", "Secreta123");
        Assert.Equal("ana@example.com", user.Email);
    }

    [Fact]
    public async Task Logout_and_expiry_end_the_session()
    {
        await using var h = await TestHarness.CreateAsync();
        await h.RegisterAsync();
        await h.Auth.LogoutAsync();
        Assert.Null(await h.Auth.RestoreSessionAsync());
        Assert.Null(await h.Secrets.GetAsync(AuthService.SessionTokenKey));

        await h.Auth.LoginAsync("ana@example.com", "Secreta123");
        h.Clock.Advance(AuthService.SessionLifetime + TimeSpan.FromMinutes(1));
        Assert.Null(await h.Auth.RestoreSessionAsync());
    }

    [Fact]
    public async Task Changing_password_revokes_other_sessions()
    {
        await using var h = await TestHarness.CreateAsync();
        var user = await h.RegisterAsync();
        var oldToken = await h.Secrets.GetAsync(AuthService.SessionTokenKey);

        await Assert.ThrowsAsync<AuthException>(() => h.Auth.ChangePasswordAsync(user.Id, "Incorrecta1", "NuevaClave9"));
        await h.Auth.ChangePasswordAsync(user.Id, "Secreta123", "NuevaClave9");

        var oldHash = Tokens.Sha256Hex(oldToken!);
        Assert.Equal(0, await h.Db.Connection.Table<SessionRecord>().Where(s => s.TokenHash == oldHash).CountAsync());
        Assert.NotNull(await h.Auth.RestoreSessionAsync());
        await h.Auth.LoginAsync("ana@example.com", "NuevaClave9");
    }

    [Fact]
    public async Task Deleting_the_account_removes_all_its_data()
    {
        await using var h = await TestHarness.CreateAsync();
        var ana = await h.RegisterAsync();
        var otro = await h.Auth.RegisterAsync("otro@example.com", "Otro", "Secreta123");

        await Assert.ThrowsAsync<AuthException>(() => h.Auth.DeleteAccountAsync(ana.Id, "Incorrecta1"));
        await h.Auth.DeleteAccountAsync(ana.Id, "Secreta123");

        Assert.Empty(await h.Projects.ListAsync(ana.Id));
        Assert.Empty(await h.Labels.ListAsync(ana.Id));
        Assert.NotEmpty(await h.Projects.ListAsync(otro.Id));
        Assert.Null(await h.Db.Connection.FindAsync<User>(ana.Id));
    }
}

public class BoardTests
{
    private static TaskInput Input(string title, TaskState state = TaskState.ToDo, Priority priority = Priority.Medium,
        DateTime? due = null, params int[] labels) => new(title, null, state, priority, due, labels);

    private static async Task<(TestHarness H, User User, Project Project)> SetupAsync()
    {
        var h = await TestHarness.CreateAsync();
        var user = await h.RegisterAsync();
        var project = await h.Projects.CreateAsync(user.Id, "Proyecto", "", "#123456");
        return (h, user, project);
    }

    private static async Task<List<string>> ColumnAsync(TestHarness h, User u, Project p, TaskState state) =>
        (await h.Tasks.GetBoardAsync(u.Id, p.Id, TaskFilter.None))[state].Select(c => c.Task.Title).ToList();

    [Fact]
    public async Task New_account_gets_a_sample_board_and_labels()
    {
        await using var h = await TestHarness.CreateAsync();
        var user = await h.RegisterAsync();
        var summary = Assert.Single(await h.Projects.ListAsync(user.Id));
        Assert.Equal(4, summary.Total);
        Assert.Equal(4, (await h.Labels.ListAsync(user.Id)).Count);
    }

    [Fact]
    public async Task Users_cannot_touch_each_others_data()
    {
        var (h, ana, project) = await SetupAsync();
        await using var _ = h;
        var task = await h.Tasks.CreateAsync(ana.Id, project.Id, Input("Privada"));
        var otro = await h.Auth.RegisterAsync("otro@example.com", "Otro", "Secreta123");
        var otroLabel = (await h.Labels.ListAsync(otro.Id))[0];

        await Assert.ThrowsAsync<NotFoundException>(() => h.Projects.GetAsync(otro.Id, project.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Tasks.GetAsync(otro.Id, task.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Tasks.MoveAsync(otro.Id, task.Id, TaskState.Done));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Comments.AddAsync(otro.Id, task.Id, "hola"));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Projects.DeleteAsync(otro.Id, project.Id));

        // Una etiqueta ajena se ignora silenciosamente.
        await h.Tasks.UpdateAsync(ana.Id, task.Id, Input("Privada", labels: otroLabel.Id));
        Assert.Empty((await h.Tasks.GetAsync(ana.Id, task.Id)).LabelIds);
    }

    [Fact]
    public async Task Drag_and_drop_reorders_and_changes_columns()
    {
        var (h, u, p) = await SetupAsync();
        await using var _ = h;
        var a = await h.Tasks.CreateAsync(u.Id, p.Id, Input("A"));
        var b = await h.Tasks.CreateAsync(u.Id, p.Id, Input("B"));
        var c = await h.Tasks.CreateAsync(u.Id, p.Id, Input("C"));
        Assert.Equal(["A", "B", "C"], await ColumnAsync(h, u, p, TaskState.ToDo));

        await h.Tasks.MoveAsync(u.Id, c.Id, TaskState.ToDo, beforeTaskId: a.Id);
        Assert.Equal(["C", "A", "B"], await ColumnAsync(h, u, p, TaskState.ToDo));

        await h.Tasks.MoveAsync(u.Id, a.Id, TaskState.InProgress);
        await h.Tasks.MoveAsync(u.Id, b.Id, TaskState.InProgress, beforeTaskId: a.Id);
        Assert.Equal(["C"], await ColumnAsync(h, u, p, TaskState.ToDo));
        Assert.Equal(["B", "A"], await ColumnAsync(h, u, p, TaskState.InProgress));

        await h.Tasks.MoveAsync(u.Id, a.Id, TaskState.Done);
        var done = (await h.Tasks.GetAsync(u.Id, a.Id)).Task;
        Assert.NotNull(done.CompletedUtc);
        await h.Tasks.MoveAsync(u.Id, a.Id, TaskState.ToDo);
        Assert.Null((await h.Tasks.GetAsync(u.Id, a.Id)).Task.CompletedUtc);

        var history = await h.Comments.ListAsync(u.Id, a.Id);
        Assert.Contains(history, x => x.Comment.IsSystem && x.Comment.Body.Contains("«En proceso» a «Completado»"));
    }

    [Fact]
    public async Task Positions_stay_contiguous_after_deletes()
    {
        var (h, u, p) = await SetupAsync();
        await using var _ = h;
        var a = await h.Tasks.CreateAsync(u.Id, p.Id, Input("A"));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("B"));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("C"));
        await h.Tasks.DeleteAsync(u.Id, a.Id);

        var cards = (await h.Tasks.GetBoardAsync(u.Id, p.Id, TaskFilter.None))[TaskState.ToDo];
        Assert.Equal([0, 1], cards.Select(c => c.Task.Position));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Tasks.GetAsync(u.Id, a.Id));
    }

    [Fact]
    public async Task Filters_combine_search_priority_label_and_due_date()
    {
        var (h, u, p) = await SetupAsync();
        await using var _ = h;
        var bug = (await h.Labels.ListAsync(u.Id)).Single(l => l.Name == "Bug");
        var now = h.Clock.GetUtcNow().UtcDateTime;

        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Arreglar login", priority: Priority.High, due: now.AddHours(-2), labels: bug.Id));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Diseñar logo", priority: Priority.Low, due: now.AddHours(3)));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Escribir README", priority: Priority.High, due: now.AddDays(5)));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Sin fecha", priority: Priority.Medium));

        async Task<string[]> Titles(TaskFilter f) =>
            (await h.Tasks.GetBoardAsync(u.Id, p.Id, f)).Values.SelectMany(x => x).Select(c => c.Task.Title).Order().ToArray();

        Assert.Equal(["Arreglar login", "Diseñar logo"], await Titles(new TaskFilter(Search: "LO")));
        Assert.Equal(["Arreglar login", "Escribir README"], await Titles(new TaskFilter(Priority: Priority.High)));
        Assert.Equal(["Arreglar login"], await Titles(new TaskFilter(LabelId: bug.Id)));
        Assert.Equal(["Arreglar login"], await Titles(new TaskFilter(Due: DueFilter.Overdue)));
        Assert.Equal(["Arreglar login", "Diseñar logo"], await Titles(new TaskFilter(Due: DueFilter.Today)));
        Assert.Equal(["Arreglar login", "Diseñar logo", "Escribir README"], await Titles(new TaskFilter(Due: DueFilter.Next7Days)));
        Assert.Equal(["Sin fecha"], await Titles(new TaskFilter(Due: DueFilter.NoDate)));
        Assert.Equal(["Escribir README"], await Titles(new TaskFilter(Search: "readme", Priority: Priority.High, Due: DueFilter.Next7Days)));
    }

    [Fact]
    public async Task Due_alerts_list_overdue_and_upcoming_unfinished_tasks()
    {
        var (h, u, p) = await SetupAsync();
        await using var _ = h;
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Vencida", due: now.AddHours(-1)));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Pronto", due: now.AddHours(5)));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Lejos", due: now.AddDays(3)));
        await h.Tasks.CreateAsync(u.Id, p.Id, Input("Hecha", TaskState.Done, due: now.AddHours(-1)));

        var alerts = (await h.Tasks.GetDueAlertsAsync(u.Id, TimeSpan.FromHours(24)))
            .Where(a => a.ProjectName == "Proyecto").ToList();
        Assert.Equal(["Vencida", "Pronto"], alerts.Select(a => a.Task.Title));
        Assert.True(alerts[0].IsOverdue);
        Assert.False(alerts[1].IsOverdue);
    }

    [Fact]
    public async Task Comments_keep_history_and_only_author_can_delete()
    {
        var (h, u, p) = await SetupAsync();
        await using var _ = h;
        var task = await h.Tasks.CreateAsync(u.Id, p.Id, Input("Con comentarios"));

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var first = await h.Comments.AddAsync(u.Id, task.Id, "  Primero  ");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.Comments.AddAsync(u.Id, task.Id, "Segundo\nlínea 2");
        await Assert.ThrowsAsync<ValidationException>(() => h.Comments.AddAsync(u.Id, task.Id, " "));

        var list = await h.Comments.ListAsync(u.Id, task.Id);
        Assert.Equal(["Creó la tarea en «Por hacer».", "Primero", "Segundo\nlínea 2"], list.Select(c => c.Comment.Body));
        Assert.All(list, c => Assert.Equal("Ana", c.AuthorName));

        var board = (await h.Tasks.GetBoardAsync(u.Id, p.Id, TaskFilter.None))[TaskState.ToDo];
        Assert.Equal(2, board.Single(c => c.Task.Id == task.Id).CommentCount);

        await Assert.ThrowsAsync<NotFoundException>(() => h.Comments.DeleteAsync(u.Id, list[0].Comment.Id));
        await h.Comments.DeleteAsync(u.Id, first.Id);
        Assert.Equal(2, (await h.Comments.ListAsync(u.Id, task.Id)).Count);
    }

    [Fact]
    public async Task Deleting_a_label_or_project_cascades()
    {
        var (h, u, p) = await SetupAsync();
        await using var _ = h;
        var label = await h.Labels.CreateAsync(u.Id, "Cliente", "#00ff00");
        await Assert.ThrowsAsync<ValidationException>(() => h.Labels.CreateAsync(u.Id, "cliente", "#00FF00"));
        var task = await h.Tasks.CreateAsync(u.Id, p.Id, Input("Etiquetada", labels: label.Id));

        await h.Labels.DeleteAsync(u.Id, label.Id);
        Assert.Empty((await h.Tasks.GetAsync(u.Id, task.Id)).LabelIds);

        await h.Projects.DeleteAsync(u.Id, p.Id);
        Assert.Equal(0, await h.Db.Connection.Table<TaskItem>().Where(t => t.ProjectId == p.Id).CountAsync());
        Assert.Equal(0, await h.Db.Connection.Table<Comment>().Where(c => c.TaskId == task.Id).CountAsync());
    }
}
