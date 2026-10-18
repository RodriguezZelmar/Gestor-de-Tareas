using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class LabelsPage : ContentPage
{
    private readonly LabelsViewModel _vm;

    public LabelsPage(LabelsViewModel vm)
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
