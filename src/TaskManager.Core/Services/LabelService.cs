using TaskManager.Core.Data;
using TaskManager.Core.Domain;
using TaskManager.Core.Security;

namespace TaskManager.Core.Services;

public sealed class LabelService(AppDatabase db)
{
    public async Task<IReadOnlyList<Label>> ListAsync(int userId) =>
        (await db.Connection.Table<Label>().Where(l => l.OwnerId == userId).ToListAsync())
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task<Label> CreateAsync(int userId, string? name, string? color)
    {
        var label = new Label { OwnerId = userId };
        await ApplyAsync(userId, label, name, color);
        await db.Connection.InsertAsync(label);
        return label;
    }

    public async Task<Label> UpdateAsync(int userId, int labelId, string? name, string? color)
    {
        var label = await GetAsync(userId, labelId);
        await ApplyAsync(userId, label, name, color);
        await db.Connection.UpdateAsync(label);
        return label;
    }

    public async Task DeleteAsync(int userId, int labelId)
    {
        await GetAsync(userId, labelId);
        await db.Connection.RunInTransactionAsync(conn =>
        {
            conn.Table<TaskLabel>().Delete(tl => tl.LabelId == labelId);
            conn.Delete<Label>(labelId);
        });
    }

    private async Task<Label> GetAsync(int userId, int labelId)
    {
        var label = await db.Connection.FindAsync<Label>(labelId);
        return label is not null && label.OwnerId == userId ? label : throw new NotFoundException("La etiqueta");
    }

    private async Task ApplyAsync(int userId, Label label, string? name, string? color)
    {
        var cleanName = InputSanitizer.Text(name, "El nombre de la etiqueta", 30);
        var existing = await ListAsync(userId);
        if (existing.Any(l => l.Id != label.Id && string.Equals(l.Name, cleanName, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new ValidationException("Ya tenés una etiqueta con ese nombre.");
        }
        label.Name = cleanName;
        label.Color = InputSanitizer.Color(color);
    }
}
