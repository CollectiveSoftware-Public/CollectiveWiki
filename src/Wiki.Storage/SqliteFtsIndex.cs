// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Data.Sqlite;
using Wiki.Core.Models;
using Wiki.Core.Search;

namespace Wiki.Storage;

/// <summary>A persistent <see cref="IFtsIndex"/> over SQLite FTS5, living in the vault's
/// <c>.cwiki/index.db</c> sidecar. A rebuildable cache — notes remain the source of truth. Holds one
/// open connection (so <c>:memory:</c> databases persist for the object's lifetime). FTS5 ships in the
/// bundled native SQLite. Ranking uses FTS5 bm25 via the <c>rank</c> column.</summary>
public sealed class SqliteFtsIndex : IFtsIndex, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteFtsIndex(string dbPath)
    {
        // One long-lived connection, so pooling buys nothing — and disabling it makes Dispose fully
        // release the file handle (pooled connections keep the .db locked after Dispose).
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        _connection = new SqliteConnection(cs);
        _connection.Open();
        Exec("CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(path UNINDEXED, body);");
    }

    public void Add(string notePath, string content) => Update(notePath, content);

    public void Update(string notePath, string content)
    {
        Remove(notePath);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO notes_fts(path, body) VALUES(@p, @b);";
        cmd.Parameters.AddWithValue("@p", notePath);
        cmd.Parameters.AddWithValue("@b", content);
        cmd.ExecuteNonQuery();
    }

    public void Remove(string notePath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM notes_fts WHERE path = @p;";
        cmd.Parameters.AddWithValue("@p", notePath);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 50)
    {
        string match = BuildMatch(query);
        if (match.Length == 0) return Array.Empty<SearchHit>();

        using var cmd = _connection.CreateCommand();
        // bm25 rank is more-negative for better matches; negate so a higher SearchHit.Score = better.
        cmd.CommandText = "SELECT path, rank FROM notes_fts WHERE notes_fts MATCH @q ORDER BY rank LIMIT @n;";
        cmd.Parameters.AddWithValue("@q", match);
        cmd.Parameters.AddWithValue("@n", limit);

        var hits = new List<SearchHit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            hits.Add(new SearchHit(reader.GetString(0), -reader.GetDouble(1)));
        return hits;
    }

    // Tokenize the user query the same way the in-memory index does, then AND the terms as FTS5 phrases.
    // Quoting each token defuses FTS5 operator characters in arbitrary input.
    private static string BuildMatch(string query)
    {
        var terms = new List<string>();
        int i = 0;
        while (i < query.Length)
        {
            if (!char.IsLetterOrDigit(query[i])) { i++; continue; }
            int start = i;
            while (i < query.Length && char.IsLetterOrDigit(query[i])) i++;
            terms.Add("\"" + query[start..i] + "\"");
        }
        return string.Join(" ", terms);
    }

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
