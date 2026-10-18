using TaskManager.App.Services;
using TaskManager.App.Views;

namespace TaskManager.App;

public partial class AppShell : Shell
{
    public AppShell(AppSession session)
    {
        InitializeComponent();
        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("board", typeof(BoardPage));
        Routing.RegisterRoute("projectedit", typeof(ProjectEditorPage));
        Routing.RegisterRoute("task", typeof(TaskEditorPage));

        session.UserChanged += (_, _) => UserLabel.Text = session.IsSignedIn ? session.User.Email : "";
    }

    /// <summary>En Windows el menú queda fijo a la izquierda; si la ventana es angosta pasa a menú hamburguesa.</summary>
    public void AdaptToWidth(double width)
    {
        var behavior = width >= 760 ? FlyoutBehavior.Locked : FlyoutBehavior.Flyout;
        if (FlyoutBehavior != behavior)
        {
            FlyoutBehavior = behavior;
        }
    }
}
