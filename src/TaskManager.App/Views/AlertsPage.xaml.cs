using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class AlertsPage : ContentPage
{
    private readonly AlertsViewModel _vm;

    public AlertsPage(AlertsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
