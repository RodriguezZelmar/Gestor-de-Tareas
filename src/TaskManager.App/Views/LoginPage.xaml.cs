using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void OnEmailCompleted(object? sender, EventArgs e) => PasswordEntry.Focus();
}
