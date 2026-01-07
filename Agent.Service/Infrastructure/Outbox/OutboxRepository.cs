using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Agent.Shared.Config;
using System.Text.Json;
// Wait, I didn't add Dapper. I should stick to plain ADO.NET or add Dapper.
// I will use raw ADO.NET to avoid extra dependencies if not needed, but Dapper is nicer.
// The user didn't forbid new packages, but keeping it minimal is good. I'll use ADO.NET.

namespace Agent.Service.Infrastructure.Outbox;

public class OutboxRepository
{
    private readonly string _connectionString;
    private readonly ILogger<OutboxRepository> _logger;
    private readonly AgentConfig _config;

    public OutboxRepository(IOptions<AgentConfig> config, ILogger<OutboxRepository> logger)
    {
        _config = config.Value;
        _logger = logger;
        
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EmployeeTracker", "agent.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();
    }

    public void Init()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // 1. Check schema version
        var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        var version = (long)versionCmd.ExecuteScalar()!;

        if (version == 0)
        {
            var createCmd = connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Outbox (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Type TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    AttemptCount INTEGER DEFAULT 0,
                    NextAttemptAt TEXT NOT NULL,
                    LockedUntilUtc TEXT,
                    LockedBy TEXT,
                    SentAt TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_Outbox_Process ON Outbox (SentAt, LockedUntilUtc, NextAttemptAt);
                PRAGMA user_version = 1;
            ";
            createCmd.ExecuteNonQuery();
            _logger.LogInformation("Initialized Outbox DB (v1).");
            version = 1;
        }

        if (version == 1)
        {
            // V2: Add Sequence table
            var v2Cmd = connection.CreateCommand();
            v2Cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Sequence (
                    Key TEXT PRIMARY KEY, 
                    Value INTEGER NOT NULL
                );
                INSERT OR IGNORE INTO Sequence (Key, Value) VALUES ('Global', 0);
                PRAGMA user_version = 2;
            ";
            v2Cmd.ExecuteNonQuery();
            _logger.LogInformation("Upgraded Outbox DB to v2.");
            version = 2;
        }
    }

    public async Task<long> GetNextSequenceAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();
        try 
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE Sequence SET Value = Value + 1 WHERE Key = 'Global' RETURNING Value";
            var result = await cmd.ExecuteScalarAsync();
            
            // Fallback for older sqlite versions if RETURNING not supported (unlikely in 8.0 but safe)
            if (result == null)
            {
                 cmd.CommandText = "UPDATE Sequence SET Value = Value + 1 WHERE Key = 'Global'";
                 await cmd.ExecuteNonQueryAsync();
                 cmd.CommandText = "SELECT Value FROM Sequence WHERE Key = 'Global'";
                 result = await cmd.ExecuteScalarAsync();
            }
            
            await transaction.CommitAsync();
            return (long)(result ?? 0L);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task EnqueueAsync(string type, object payload)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var json = JsonSerializer.Serialize(payload);
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Outbox (Type, PayloadJson, CreatedAt, NextAttemptAt)
            VALUES ($type, $json, $now, $now);
        ";
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$now", now);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<OutboxItem>> LeaseBatchAsync(string type, int limit, string instanceId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var nowStr = now.ToString("O");
        var leaseExpiry = now.AddSeconds(_config.OutboxLeaseSeconds).ToString("O");

        // Select IDs to lock
        // Logic: Not Sent AND (Not Locked OR Lock Expired) AND Due
        using var selectCmd = connection.CreateCommand();
        selectCmd.Transaction = transaction;
        selectCmd.CommandText = @"
            SELECT Id, Type, PayloadJson, CreatedAt, AttemptCount, NextAttemptAt, LockedUntilUtc, LockedBy, SentAt
            FROM Outbox
            WHERE Type = $type
              AND SentAt IS NULL
              AND (LockedUntilUtc IS NULL OR LockedUntilUtc < $now)
              AND NextAttemptAt <= $now
            ORDER BY Id ASC
            LIMIT $limit
        ";
        selectCmd.Parameters.AddWithValue("$type", type);
        selectCmd.Parameters.AddWithValue("$now", nowStr);
        selectCmd.Parameters.AddWithValue("$limit", limit);

        var items = new List<OutboxItem>();
        using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                items.Add(new OutboxItem
                {
                    Id = reader.GetInt64(0),
                    Type = reader.GetString(1),
                    PayloadJson = reader.GetString(2),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                    AttemptCount = reader.GetInt32(4),
                    NextAttemptAt = DateTimeOffset.Parse(reader.GetString(5)),
                    LockedUntilUtc = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                    LockedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                    SentAt = reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8))
                });
            }
        }

        if (items.Count > 0)
        {
            var ids = string.Join(",", items.Select(i => i.Id));
            using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = $@"
                UPDATE Outbox
                SET LockedUntilUtc = $expiry, LockedBy = $instanceId
                WHERE Id IN ({ids})
            ";
            updateCmd.Parameters.AddWithValue("$expiry", leaseExpiry);
            updateCmd.Parameters.AddWithValue("$instanceId", instanceId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return items;
    }

    public async Task DeleteAsync(IEnumerable<long> ids)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var idList = ids.ToList();
        if (idList.Count == 0) return;

        using var transaction = connection.BeginTransaction();
        var idStr = string.Join(",", idList);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"DELETE FROM Outbox WHERE Id IN ({idStr})";
        await cmd.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task AbandonAsync(IEnumerable<long> ids)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var idList = ids.ToList();
        if (idList.Count == 0) return;

        using var transaction = connection.BeginTransaction();

        var rnd = new Random();
        foreach (var id in idList)
        {
            using var getCmd = connection.CreateCommand();
            getCmd.Transaction = transaction;
            getCmd.CommandText = "SELECT AttemptCount FROM Outbox WHERE Id = $id";
            getCmd.Parameters.AddWithValue("$id", id);
            var attempts = (long)(await getCmd.ExecuteScalarAsync() ?? 0L);

            var nextAttempts = attempts + 1;
            var baseBackoff = Math.Min(Math.Pow(2, nextAttempts), _config.OutboxMaxBackoffSeconds);
            var jitter = rnd.NextDouble() * 3.0;
            var nextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(baseBackoff + jitter).ToString("O");

            using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = @"
                UPDATE Outbox
                SET LockedUntilUtc = NULL, LockedBy = NULL, AttemptCount = $attempts, NextAttemptAt = $next
                WHERE Id = $id
            ";
            updateCmd.Parameters.AddWithValue("$id", id);
            updateCmd.Parameters.AddWithValue("$attempts", nextAttempts);
            updateCmd.Parameters.AddWithValue("$next", nextAttemptAt);
            await updateCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Type, COUNT(*)
            FROM Outbox
            WHERE SentAt IS NULL
            GROUP BY Type
        ";

        var stats = new Dictionary<string, int>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            stats[reader.GetString(0)] = reader.GetInt32(1);
        }
        return stats;
    }
}
