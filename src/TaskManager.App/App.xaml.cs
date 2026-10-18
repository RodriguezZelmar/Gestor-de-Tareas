using TaskManager.App.Services;

namespace TaskManager.App;

public partial class App : Application
{
    private readonly AppSession _session;

    public App(AppSession session)
    {
        InitializeComponent();
        _session = session;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = new AppShell(_session);
        var window = new Window(shell)
        {
            Title = "Gestor de Tareas",
            MinimumWidth = 400,
            MinimumHeight = 560,
            Width = 1280,
            Height = 800,
        };
#if WINDOWS
        window.SizeChanged += (_, _) => shell.AdaptToWidth(window.Width);
#endif
        return window;
    }
}
