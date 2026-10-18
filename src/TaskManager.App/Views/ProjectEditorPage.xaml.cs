using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class ProjectEditorPage : ContentPage
{
    public ProjectEditorPage(ProjectEditorViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
