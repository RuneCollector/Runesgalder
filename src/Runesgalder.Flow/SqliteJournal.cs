using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Runesgalder;

/// <summary>A durable SQLite journal that also stores checkpoints.</summary>
/// <remarks>
/// The file uses WAL, is versioned with <c>PRAGMA user_version</c>, and is scoped to a single writer process.
/// </remarks>
public sealed class SqliteJournal : IJournal, ICheckpointStore, IAsyncDisposable
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Opens or creates a journal file and applies the schema.</summary>
    /// <param name="path">The journal file path.</param>
    public SqliteJournal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        try
        {
            Initialize();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask AppendAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (type, payload) = RunEventCodec.Encode(runEvent);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (runEvent is RunStarted runStarted)
            {
                await using var insertRun = _connection.CreateCommand();
                insertRun.CommandText =
                    "INSERT OR IGNORE INTO runs(run_id, flow_name, started_at) VALUES ($id, $flow, $at)";
                insertRun.Parameters.AddWithValue("$id", runStarted.RunId.ToString());
                insertRun.Parameters.AddWithValue("$flow", runStarted.FlowName);
                insertRun.Parameters.AddWithValue("$at", Format(runStarted.Timestamp));
                await insertRun.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var insertEvent = _connection.CreateCommand();
            insertEvent.CommandText =
                "INSERT INTO events(run_id, type, payload) VALUES ($id, $type, $payload)";
            insertEvent.Parameters.AddWithValue("$id", runEvent.RunId.ToString());
            insertEvent.Parameters.AddWithValue("$type", type);
            insertEvent.Parameters.AddWithValue("$payload", payload);
            await insertEvent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadAsync(
        RunId runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rows = new List<(string Type, string Payload)>();

        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT type, payload FROM events WHERE run_id = $id ORDER BY sequence";
            command.Parameters.AddWithValue("$id", runId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (type, payload) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return RunEventCodec.Decode(type, payload);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Checkpoint?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT key, flow_name, output_type, output_json, created_at FROM checkpoints WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new Checkpoint(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.ParseExact(reader.GetString(4), "O", CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO checkpoints(key, flow_name, output_type, output_json, created_at)
                VALUES ($key, $flow, $type, $json, $at)
                """;
            command.Parameters.AddWithValue("$key", checkpoint.Key);
            command.Parameters.AddWithValue("$flow", checkpoint.FlowName);
            command.Parameters.AddWithValue("$type", checkpoint.OutputType);
            command.Parameters.AddWithValue("$json", checkpoint.OutputJson);
            command.Parameters.AddWithValue("$at", Format(checkpoint.CreatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the journal file.</summary>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static string Format(DateTimeOffset timestamp) =>
        timestamp.ToString("O", CultureInfo.InvariantCulture);

    private void Initialize()
    {
        using (var wal = _connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteScalar();
        }

        int version;
        using (var versionCommand = _connection.CreateCommand())
        {
            versionCommand.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (version > SchemaVersion)
        {
            throw new NotSupportedException(
                $"The journal schema version {version} is newer than the supported version {SchemaVersion}.");
        }

        if (version == SchemaVersion)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction();
        using var schema = _connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = $"""
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT PRIMARY KEY,
                flow_name TEXT NOT NULL,
                started_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS events (
                sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                type TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_events_run ON events(run_id, sequence);
            CREATE TABLE IF NOT EXISTS checkpoints (
                key TEXT PRIMARY KEY,
                flow_name TEXT NOT NULL,
                output_type TEXT NOT NULL,
                output_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            PRAGMA user_version = {SchemaVersion};
            """;
        schema.ExecuteNonQuery();
        transaction.Commit();
    }
}
