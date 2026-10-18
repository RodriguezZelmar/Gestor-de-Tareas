using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using TaskManager.Core.Services;

namespace TaskManager.Core.Security;

/// <summary>
/// Validación y limpieza de toda entrada de usuario antes de persistirla.
/// La inyección SQL se evita además porque todas las consultas son parametrizadas (sqlite-net), nunca concatenadas.
/// </summary>
public static partial class InputSanitizer
{
    /// <summary>
    /// Normaliza (NFC), recorta y elimina caracteres de control y de override bidireccional (ataques "trojan source").
    /// En textos multilínea conserva saltos de línea y tabulaciones.
    /// </summary>
    public static string Text(string? value, string field, int maxLength, bool required = true, bool multiline = false)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (c == '\r')
            {
                continue;
            }
            if (c is '\n' or '\t')
            {
                sb.Append(multiline ? c : ' ');
                continue;
            }
            // Control: \0, ESC, etc. Format: U+200B, U+202E (RLO), U+2066..2069, etc. Los surrogates (emojis) se conservan.
            // El ZWJ (U+200D) se conserva porque une emojis compuestos.
            if (c != '‍' && char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }
            sb.Append(c);
        }

        var result = multiline ? CollapseBlankLines(sb.ToString()).Trim() : WhitespaceRun().Replace(sb.ToString(), " ").Trim();
        if (required && result.Length == 0)
        {
            throw new ValidationException($"{field} es obligatorio.");
        }
        if (result.Length > maxLength)
        {
            throw new ValidationException($"{field} no puede superar {maxLength} caracteres.");
        }
        return result;
    }

    public static string Email(string? value)
    {
        var email = Text(value, "El email", 254).ToLowerInvariant();
        if (!EmailPattern().IsMatch(email))
        {
            throw new ValidationException("El email no tiene un formato válido.");
        }
        return email;
    }

    /// <summary>Exige 8–128 caracteres con al menos una letra y un número. No se recorta: los espacios cuentan.</summary>
    public static void Password(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
        {
            throw new ValidationException("La contraseña debe tener al menos 8 caracteres.");
        }
        if (value.Length > 128)
        {
            throw new ValidationException("La contraseña no puede superar 128 caracteres.");
        }
        if (!value.Any(char.IsLetter) || !value.Any(char.IsDigit))
        {
            throw new ValidationException("La contraseña debe incluir letras y números.");
        }
    }

    /// <summary>Sólo colores hexadecimales #RRGGBB.</summary>
    public static string Color(string? value)
    {
        var color = (value ?? "").Trim().ToUpperInvariant();
        if (!HexColor().IsMatch(color))
        {
            throw new ValidationException("El color debe tener el formato #RRGGBB.");
        }
        return color;
    }

    private static string CollapseBlankLines(string text) => BlankLines().Replace(text, "\n\n");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9\-]+(\.[a-z0-9\-]+)*\.[a-z]{2,}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColor();
}
