using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class ProjectsPage : ContentPage
{
    private readonly ProjectsViewModel _vm;

    public ProjectsPage(ProjectsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        SizeChanged += (_, _) => UpdateSpan();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        await _vm.ShowAlertReminderAsync();
    }

    // Una columna en teléfonos, una cuadrícula en tablets y escritorio.
    private void UpdateSpan()
    {
        if (ProjectList.ItemsLayout is GridItemsLayout grid)
        {
            var span = Math.Clamp((int)(Width / 340), 1, 4);
            if (grid.Span != span)
            {
                grid.Span = span;
            }
        }
    }
}
