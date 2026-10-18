using TaskManager.Core.Data;
using TaskManager.Core.Security;
using TaskManager.Core.Services;

namespace TaskManager.Core.Tests;

public sealed class FakeClock(DateTimeOffset start, TimeZoneInfo? zone = null) : TimeProvider
{
    public DateTimeOffset Current { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Current;

    public override TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => Current += by;
}

/// <summary>Base cifrada temporal con todos los servicios conectados.</summary>
public sealed class TestHarness : IAsyncDisposable
{
    private TestHarness(string path, AppDatabase db, InMemorySecretStore secrets, FakeClock clock)
    {
        Path = path;
        Db = db;
        Secrets = secrets;
        Clock = clock;
        var hasher = new PasswordHasher(iterations: 1_000);
        Auth = new AuthService(db, secrets, hasher, clock);
        Projects = new ProjectService(db, clock);
        Labels = new LabelService(db);
        Tasks = new TaskService(db, Projects, clock);
        Comments = new CommentService(db, Tasks, clock);
    }

    public string Path { get; }
    public AppDatabase Db { get; }
    public InMemorySecretStore Secrets { get; }
    public FakeClock Clock { get; }
    public AuthService Auth { get; }
    public ProjectService Projects { get; }
    public LabelService Labels { get; }
    public TaskService Tasks { get; }
    public CommentService Comments { get; }

    public static async Task<TestHarness> CreateAsync()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tm-test-{Guid.NewGuid():N}.db3");
        var secrets = new InMemorySecretStore();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
        var db = await AppDatabase.OpenAsync(path, secrets);
        return new TestHarness(path, db, secrets, clock);
    }

    public Task<Domain.User> RegisterAsync(string email = "ana@example.com") =>
        Auth.RegisterAsync(email, "Ana", "Secreta123");

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        AppDatabase.Reset(Path, Secrets);
    }
}
