using CommunityToolkit.Mvvm.ComponentModel;

using TaskManager.App.Services;
using TaskManager.Core.Services;

namespace TaskManager.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>Error de validación mostrado en línea (formularios).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool IsNotBusy => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Ejecuta una acción mostrando el indicador de carga. Los errores de dominio se muestran al usuario;
    /// el resto se muestran con un mensaje genérico (sin detalles internos).
    /// </summary>
    protected async Task RunAsync(Func<Task> action, bool inlineErrors = false)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (DomainException ex)
        {
            if (inlineErrors)
            {
                ErrorMessage = ex.Message;
            }
            else
            {
                await Dialogs.InfoAsync("No se pudo completar", ex.Message);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            await Dialogs.InfoAsync("Error inesperado", "Ocurrió un error inesperado. Intentá de nuevo.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
