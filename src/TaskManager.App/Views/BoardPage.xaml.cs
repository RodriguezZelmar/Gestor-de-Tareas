using TaskManager.App.ViewModels;

namespace TaskManager.App.Views;

public partial class BoardPage : ContentPage
{
    /// <summary>Por encima de este ancho se muestran las tres columnas juntas.</summary>
    private const double WideThreshold = 760;

    private readonly BoardViewModel _vm;

    public BoardPage(BoardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        SizeChanged += (_, _) => ApplyLayout();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private void ApplyLayout()
    {
        var wide = Width >= WideThreshold;
        if (wide == _vm.IsWide && BoardGrid.ColumnDefinitions.Count == (wide ? 3 : 1))
        {
            return;
        }
        _vm.IsWide = wide;

        BoardGrid.ColumnDefinitions.Clear();
        var columns = new[] { Column0, Column1, Column2 };
        for (var i = 0; i < columns.Length; i++)
        {
            if (wide || i == 0)
            {
                BoardGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }
            Grid.SetColumn(columns[i], wide ? i : 0);
        }
    }

    private void OnFocusSearch(object? sender, EventArgs e) => Search.Focus();
}
