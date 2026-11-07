namespace SolarOps.Api.Infrastructure.Querying;

/// <summary>Helpers for <c>EF.Functions.ILike(column, pattern, SqlLike.EscapeCharacter)</c> substring searches.</summary>
public static class SqlLike
{
    /// <summary>Escape character to pass as the third argument of <c>ILike</c>.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>Escapes the LIKE wildcards (%, _) and the escape character itself, so user input is matched literally.</summary>
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>Pattern for a case-insensitive "contains" search: <c>%escaped%</c> of the trimmed term.</summary>
    public static string Contains(string term) => $"%{Escape(term.Trim())}%";
}
