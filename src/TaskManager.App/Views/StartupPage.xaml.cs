using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class StartupPage : ContentPage
{
    private readonly StartupViewModel _vm;
    private bool _started;

    public StartupPage(StartupViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_started)
        {
            _started = true;
            await _vm.InitializeAsync();
        }
    }
}
