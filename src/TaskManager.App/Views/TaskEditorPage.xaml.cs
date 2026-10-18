using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class TaskEditorPage : ContentPage
{
    public TaskEditorPage(TaskEditorViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        SizeChanged += (_, _) => ApplyLayout();
    }

    // Formulario y comentarios lado a lado en pantallas anchas; apilados en teléfonos.
    private void ApplyLayout()
    {
        var wide = Width >= 900;
        EditorGrid.ColumnDefinitions.Clear();
        EditorGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
        if (wide)
        {
            EditorGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        }
        Grid.SetColumn(CommentsPanel, wide ? 1 : 0);
        Grid.SetRow(CommentsPanel, wide ? 0 : 1);
    }
}
