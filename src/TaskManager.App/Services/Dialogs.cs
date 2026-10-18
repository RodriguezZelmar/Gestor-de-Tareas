namespace TaskManager.App.Services;

/// <summary>Diálogos nativos (AlertDialog de Material en Android, ContentDialog en Windows).</summary>
public static class Dialogs
{
    private static Page Page => Shell.Current?.CurrentPage ?? Application.Current!.Windows[0].Page!;

    public static Task InfoAsync(string title, string message) => Page.DisplayAlertAsync(title, message, "Aceptar");

    public static Task<bool> ConfirmAsync(string title, string message, string accept = "Sí", string cancel = "Cancelar") =>
        Page.DisplayAlertAsync(title, message, accept, cancel);

    public static Task<string?> ChooseAsync(string title, params string[] options) =>
        Page.DisplayActionSheetAsync(title, "Cancelar", null, options);
}
