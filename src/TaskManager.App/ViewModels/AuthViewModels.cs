using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TaskManager.App.Services;
using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.App.ViewModels;

public sealed partial class StartupViewModel(AppSession session, IServiceProvider services) : ViewModelBase
{
    [ObservableProperty]
    private string _status = "Abriendo la base de datos cifrada…";

    public async Task InitializeAsync()
    {
        try
        {
            await session.OpenDatabaseAsync();
        }
        catch (DatabaseKeyException)
        {
            var reset = await Dialogs.ConfirmAsync("Datos ilegibles",
                "No se pudo descifrar la base de datos local (se perdió su clave). ¿Querés borrarla y empezar de cero?",
                "Borrar datos", "Salir");
            if (!reset)
            {
                Application.Current?.Quit();
                return;
            }
            await session.ResetDatabaseAsync();
            await session.OpenDatabaseAsync();
        }

        Status = "Restaurando la sesión…";
        var user = await services.GetRequiredService<AuthService>().RestoreSessionAsync();
        session.SetUser(user);
        await Shell.Current.GoToAsync(user is null ? "//login" : "//projects");
    }
}

public sealed partial class LoginViewModel(AuthService auth, AppSession session) : ViewModelBase
{
    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [RelayCommand]
    private Task LoginAsync() => RunAsync(async () =>
    {
        var user = await auth.LoginAsync(Email, Password);
        Password = "";
        session.SetUser(user);
        await Shell.Current.GoToAsync("//projects");
    }, inlineErrors: true);

    [RelayCommand]
    private Task GoToRegisterAsync() => Shell.Current.GoToAsync("register");
}

public sealed partial class RegisterViewModel(AuthService auth, AppSession session) : ViewModelBase
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    [RelayCommand]
    private Task RegisterAsync() => RunAsync(async () =>
    {
        if (Password != ConfirmPassword)
        {
            throw new ValidationException("Las contraseñas no coinciden.");
        }
        var user = await auth.RegisterAsync(Email, Name, Password);
        Password = ConfirmPassword = "";
        session.SetUser(user);
        await Shell.Current.GoToAsync("//projects");
    }, inlineErrors: true);

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");
}

public sealed partial class ProfileViewModel(AuthService auth, AppSession session) : ViewModelBase
{
    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _currentPassword = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    [ObservableProperty]
    private string _deletePassword = "";

    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public string MemberSince => $"Miembro desde {session.User.CreatedUtc.AsUtc().ToLocalTime():d}";

    public void Load()
    {
        DisplayName = session.User.DisplayName;
        Email = session.User.Email;
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(MemberSince));
    }

    [RelayCommand]
    private Task SaveProfileAsync() => RunAsync(async () =>
    {
        var user = await auth.UpdateProfileAsync(session.UserId, DisplayName, Email);
        session.SetUser(user);
        session.AlertsShown = true;
        Load();
        await Dialogs.InfoAsync("Perfil", "Los cambios se guardaron.");
    });

    [RelayCommand]
    private Task ChangePasswordAsync() => RunAsync(async () =>
    {
        if (NewPassword != ConfirmPassword)
        {
            throw new ValidationException("Las contraseñas nuevas no coinciden.");
        }
        await auth.ChangePasswordAsync(session.UserId, CurrentPassword, NewPassword);
        CurrentPassword = NewPassword = ConfirmPassword = "";
        await Dialogs.InfoAsync("Contraseña", "La contraseña se cambió y se cerraron las demás sesiones.");
    });

    [RelayCommand]
    private Task LogoutAsync() => RunAsync(async () =>
    {
        await auth.LogoutAsync();
        session.SetUser(null);
        await Shell.Current.GoToAsync("//login");
    });

    [RelayCommand]
    private Task DeleteAccountAsync() => RunAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Eliminar cuenta",
                "Se borrarán tu cuenta, tus tableros, tareas, etiquetas y comentarios. Esta acción no se puede deshacer.",
                "Eliminar", "Cancelar"))
        {
            return;
        }
        await auth.DeleteAccountAsync(session.UserId, DeletePassword);
        DeletePassword = "";
        session.SetUser(null);
        await Shell.Current.GoToAsync("//login");
    });
}
