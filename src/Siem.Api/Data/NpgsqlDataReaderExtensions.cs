using Npgsql;

namespace Siem.Api.Data;

public static class NpgsqlDataReaderExtensions
{
    public static T Get<T>(this NpgsqlDataReader reader, string column)
        => reader.GetFieldValue<T>(reader.GetOrdinal(column));

    public static T? GetOrDefault<T>(this NpgsqlDataReader reader, string column) where T : struct
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
    }

    public static string? GetStringOrNull(this NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static T GetOrFallback<T>(this NpgsqlDataReader reader, string column, T fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetFieldValue<T>(ordinal);
    }
}
