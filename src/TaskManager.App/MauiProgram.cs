using Microsoft.Extensions.Logging;

using TaskManager.App.Services;
using TaskManager.App.ViewModels;
using TaskManager.App.Views;
using TaskManager.Core.Security;
using TaskManager.Core.Services;

namespace TaskManager.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var services = builder.Services;

        // Infraestructura
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretStore, PlatformSecretStore>();
        services.AddSingleton(new PasswordHasher());
        services.AddSingleton<AppSession>();

        // Dominio: se resuelven después de abrir la base (StartupViewModel), por eso usan fábricas.
        services.AddSingleton(sp => new AuthService(Db(sp), sp.GetRequiredService<ISecretStore>(), sp.GetRequiredService<PasswordHasher>(), Clock(sp)));
        services.AddSingleton(sp => new ProjectService(Db(sp), Clock(sp)));
        services.AddSingleton(sp => new LabelService(Db(sp)));
        services.AddSingleton(sp => new TaskService(Db(sp), sp.GetRequiredService<ProjectService>(), Clock(sp)));
        services.AddSingleton(sp => new CommentService(Db(sp), sp.GetRequiredService<TaskService>(), Clock(sp)));

        // Pantallas
        services.AddTransient<StartupViewModel>().AddTransient<StartupPage>();
        services.AddTransient<LoginViewModel>().AddTransient<LoginPage>();
        services.AddTransient<RegisterViewModel>().AddTransient<RegisterPage>();
        services.AddTransient<ProjectsViewModel>().AddTransient<ProjectsPage>();
        services.AddTransient<ProjectEditorViewModel>().AddTransient<ProjectEditorPage>();
        services.AddTransient<BoardViewModel>().AddTransient<BoardPage>();
        services.AddTransient<TaskEditorViewModel>().AddTransient<TaskEditorPage>();
        services.AddTransient<LabelsViewModel>().AddTransient<LabelsPage>();
        services.AddTransient<AlertsViewModel>().AddTransient<AlertsPage>();
        services.AddTransient<ProfileViewModel>().AddTransient<ProfilePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    private static Core.Data.AppDatabase Db(IServiceProvider sp) => sp.GetRequiredService<AppSession>().Database;

    private static TimeProvider Clock(IServiceProvider sp) => sp.GetRequiredService<TimeProvider>();
}
