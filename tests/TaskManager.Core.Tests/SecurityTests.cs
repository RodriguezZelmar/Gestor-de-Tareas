using TaskManager.Core.Data;
using TaskManager.Core.Security;
using TaskManager.Core.Services;

namespace TaskManager.Core.Tests;

public class SecurityTests
{
    [Fact]
    public void Password_hash_is_salted_and_verifies()
    {
        var hasher = new PasswordHasher(1_000);
        var a = hasher.Hash("Secreta123");
        var b = hasher.Hash("Secreta123");

        Assert.NotEqual(a, b);
        Assert.DoesNotContain("Secreta123", a);
        Assert.True(hasher.Verify("Secreta123", a, out var rehash));
        Assert.False(rehash);
        Assert.False(hasher.Verify("secreta123", a, out _));
        Assert.False(hasher.Verify("x", "basura", out _));
    }

    [Fact]
    public void Old_iteration_count_requests_rehash()
    {
        var old = new PasswordHasher(500).Hash("Secreta123");
        Assert.True(new PasswordHasher(1_000).Verify("Secreta123", old, out var rehash));
        Assert.True(rehash);
    }

    [Theory]
    [InlineData("  Hola\u0000 mundo‮  ", "Hola mundo")]
    [InlineData("a\tb\nc", "a b c")]
    [InlineData("Robert'); DROP TABLE tasks;--", "Robert'); DROP TABLE tasks;--")]
    public void Text_is_cleaned_but_not_mangled(string input, string expected) =>
        Assert.Equal(expected, InputSanitizer.Text(input, "Campo", 100));

    [Fact]
    public void Multiline_text_keeps_line_breaks()
    {
        Assert.Equal("uno\ndos\n\ntres", InputSanitizer.Text("uno\r\ndos\n\n\n\n\ntres​", "Campo", 100, multiline: true));
    }

    [Fact]
    public void Text_rules_are_enforced()
    {
        Assert.Throws<ValidationException>(() => InputSanitizer.Text("   ", "Campo", 10));
        Assert.Throws<ValidationException>(() => InputSanitizer.Text(new string('a', 11), "Campo", 10));
        Assert.Equal("", InputSanitizer.Text(null, "Campo", 10, required: false));
    }

    [Theory]
    [InlineData("#ff0000", "#FF0000")]
    [InlineData(" #3f51b5 ", "#3F51B5")]
    public void Valid_colors_are_normalized(string input, string expected) => Assert.Equal(expected, InputSanitizer.Color(input));

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#12345G")]
    [InlineData("#000000; background:url(x)")]
    public void Invalid_colors_are_rejected(string input) => Assert.Throws<ValidationException>(() => InputSanitizer.Color(input));

    [Theory]
    [InlineData("corta1")]
    [InlineData("sololetras")]
    [InlineData("12345678")]
    public void Weak_passwords_are_rejected(string password) => Assert.Throws<ValidationException>(() => InputSanitizer.Password(password));

    [Fact]
    public async Task Database_is_encrypted_at_rest()
    {
        await using var h = await TestHarness.CreateAsync();
        await h.RegisterAsync("secreto@example.com");
        await h.Db.DisposeAsync();

        var bytes = await File.ReadAllBytesAsync(h.Path);
        Assert.False(System.Text.Encoding.ASCII.GetString(bytes, 0, 16).StartsWith("SQLite format 3", StringComparison.Ordinal));
        Assert.Equal(-1, System.Text.Encoding.UTF8.GetString(bytes).IndexOf("secreto@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Database_cannot_be_opened_with_another_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tm-key-{Guid.NewGuid():N}.db3");
        var secrets = new InMemorySecretStore();
        await (await AppDatabase.OpenAsync(path, secrets)).DisposeAsync();

        var other = new InMemorySecretStore();
        await other.SetAsync(AppDatabase.KeyName, Convert.ToHexString(new byte[32]));
        await Assert.ThrowsAsync<DatabaseKeyException>(() => AppDatabase.OpenAsync(path, other));
        await Assert.ThrowsAsync<DatabaseKeyException>(() => AppDatabase.OpenAsync(path, new InMemorySecretStore()));

        AppDatabase.Reset(path, secrets);
        Assert.False(File.Exists(path));
    }
}
