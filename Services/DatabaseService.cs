using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MonkMode.Models;

namespace MonkMode.Services;

/// <summary>
/// SQLite database service for persisting session logs, interventions, and blocklist configurations.
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _isDisposed;

    public DatabaseService()
    {
        // Store database in AppData
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MonkMode");
        
        Directory.CreateDirectory(appDataPath);
        
        _databasePath = Path.Combine(appDataPath, "monkmode.db");
        _connectionString = $"Data Source={_databasePath}";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS SessionLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskName TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                IntensityLevel INTEGER NOT NULL,
                InterventionCount INTEGER NOT NULL,
                FlowRating INTEGER DEFAULT 0,
                UserNotes TEXT,
                AiCoachResponse TEXT,
                BlockedProcesses TEXT,
                BlockedDomains TEXT
            );

            CREATE TABLE IF NOT EXISTS InterventionLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                InterventionType TEXT NOT NULL,
                TargetName TEXT NOT NULL,
                FOREIGN KEY (SessionId) REFERENCES SessionLogs(Id)
            );

            CREATE TABLE IF NOT EXISTS BlocklistConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Processes TEXT,
                Domains TEXT,
                IsDefault INTEGER DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_starttime ON SessionLogs(StartTime);
            CREATE INDEX IF NOT EXISTS idx_interventions_session ON InterventionLogs(SessionId);
        ";
        command.ExecuteNonQuery();

        // Insert default blocklist if none exists
        command.CommandText = "SELECT COUNT(*) FROM BlocklistConfigs WHERE IsDefault = 1";
        var count = (long)(command.ExecuteScalar() ?? 0);
        
        if (count == 0)
        {
            InsertDefaultBlocklist(connection);
        }
    }

    private void InsertDefaultBlocklist(SqliteConnection connection)
    {
        var defaultProcesses = new List<string> { "steam", "discord", "spotify", "slack" };
        var defaultDomains = new List<string> 
        { 
            "youtube.com", "twitter.com", "reddit.com", 
            "instagram.com", "facebook.com", "tiktok.com",
            "twitch.tv", "netflix.com"
        };

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO BlocklistConfigs (Name, Processes, Domains, IsDefault, CreatedAt, UpdatedAt)
            VALUES (@Name, @Processes, @Domains, 1, @CreatedAt, @UpdatedAt)";
        
        command.Parameters.AddWithValue("@Name", "Default");
        command.Parameters.AddWithValue("@Processes", JsonSerializer.Serialize(defaultProcesses));
        command.Parameters.AddWithValue("@Domains", JsonSerializer.Serialize(defaultDomains));
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("O"));
        
        command.ExecuteNonQuery();
    }

    #region Session Logs

    public int SaveSession(SessionLog session)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO SessionLogs 
            (TaskName, StartTime, EndTime, IntensityLevel, InterventionCount, 
             FlowRating, UserNotes, AiCoachResponse, BlockedProcesses, BlockedDomains)
            VALUES 
            (@TaskName, @StartTime, @EndTime, @IntensityLevel, @InterventionCount,
             @FlowRating, @UserNotes, @AiCoachResponse, @BlockedProcesses, @BlockedDomains);
            SELECT last_insert_rowid();";

        command.Parameters.AddWithValue("@TaskName", session.TaskName);
        command.Parameters.AddWithValue("@StartTime", session.StartTime.ToString("O"));
        command.Parameters.AddWithValue("@EndTime", session.EndTime.ToString("O"));
        command.Parameters.AddWithValue("@IntensityLevel", session.IntensityLevel);
        command.Parameters.AddWithValue("@InterventionCount", session.InterventionCount);
        command.Parameters.AddWithValue("@FlowRating", session.FlowRating);
        command.Parameters.AddWithValue("@UserNotes", (object?)session.UserNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("@AiCoachResponse", (object?)session.AiCoachResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("@BlockedProcesses", JsonSerializer.Serialize(session.BlockedProcesses));
        command.Parameters.AddWithValue("@BlockedDomains", JsonSerializer.Serialize(session.BlockedDomains));

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public List<SessionLog> GetRecentSessions(int count = 10)
    {
        var sessions = new List<SessionLog>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM SessionLogs 
            ORDER BY StartTime DESC 
            LIMIT @Count";
        command.Parameters.AddWithValue("@Count", count);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(MapSessionLog(reader));
        }

        return sessions;
    }

    public SessionLog? GetSession(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SessionLogs WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? MapSessionLog(reader) : null;
    }

    public void UpdateSessionRating(int sessionId, int rating, string? notes, string? aiResponse)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE SessionLogs 
            SET FlowRating = @Rating, 
                UserNotes = @Notes,
                AiCoachResponse = @AiResponse
            WHERE Id = @Id";
        
        command.Parameters.AddWithValue("@Id", sessionId);
        command.Parameters.AddWithValue("@Rating", rating);
        command.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@AiResponse", (object?)aiResponse ?? DBNull.Value);
        
        command.ExecuteNonQuery();
    }

    private static SessionLog MapSessionLog(SqliteDataReader reader)
    {
        return new SessionLog
        {
            Id = reader.GetInt32(0),
            TaskName = reader.GetString(1),
            StartTime = DateTime.Parse(reader.GetString(2)),
            EndTime = DateTime.Parse(reader.GetString(3)),
            IntensityLevel = reader.GetInt32(4),
            InterventionCount = reader.GetInt32(5),
            FlowRating = reader.GetInt32(6),
            UserNotes = reader.IsDBNull(7) ? null : reader.GetString(7),
            AiCoachResponse = reader.IsDBNull(8) ? null : reader.GetString(8),
            BlockedProcesses = reader.IsDBNull(9) 
                ? new List<string>() 
                : JsonSerializer.Deserialize<List<string>>(reader.GetString(9)) ?? new(),
            BlockedDomains = reader.IsDBNull(10) 
                ? new List<string>() 
                : JsonSerializer.Deserialize<List<string>>(reader.GetString(10)) ?? new()
        };
    }

    #endregion

    #region Intervention Logs

    public void LogIntervention(int sessionId, string type, string targetName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO InterventionLogs (SessionId, Timestamp, InterventionType, TargetName)
            VALUES (@SessionId, @Timestamp, @Type, @Target)";
        
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@Target", targetName);
        
        command.ExecuteNonQuery();
    }

    public List<InterventionLog> GetInterventionsForSession(int sessionId)
    {
        var interventions = new List<InterventionLog>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM InterventionLogs 
            WHERE SessionId = @SessionId 
            ORDER BY Timestamp";
        command.Parameters.AddWithValue("@SessionId", sessionId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            interventions.Add(new InterventionLog
            {
                Id = reader.GetInt32(0),
                SessionId = reader.GetInt32(1),
                Timestamp = DateTime.Parse(reader.GetString(2)),
                InterventionType = reader.GetString(3),
                TargetName = reader.GetString(4)
            });
        }

        return interventions;
    }

    #endregion

    #region Blocklist Configs

    public BlocklistConfig? GetDefaultBlocklist()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM BlocklistConfigs WHERE IsDefault = 1";

        using var reader = command.ExecuteReader();
        return reader.Read() ? MapBlocklistConfig(reader) : null;
    }

    public void SaveBlocklist(BlocklistConfig config)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        
        if (config.Id == 0)
        {
            command.CommandText = @"
                INSERT INTO BlocklistConfigs (Name, Processes, Domains, IsDefault, CreatedAt, UpdatedAt)
                VALUES (@Name, @Processes, @Domains, @IsDefault, @CreatedAt, @UpdatedAt)";
            command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("O"));
        }
        else
        {
            command.CommandText = @"
                UPDATE BlocklistConfigs 
                SET Name = @Name, Processes = @Processes, Domains = @Domains, 
                    IsDefault = @IsDefault, UpdatedAt = @UpdatedAt
                WHERE Id = @Id";
            command.Parameters.AddWithValue("@Id", config.Id);
        }

        command.Parameters.AddWithValue("@Name", config.Name);
        command.Parameters.AddWithValue("@Processes", JsonSerializer.Serialize(config.Processes));
        command.Parameters.AddWithValue("@Domains", JsonSerializer.Serialize(config.Domains));
        command.Parameters.AddWithValue("@IsDefault", config.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

    private static BlocklistConfig MapBlocklistConfig(SqliteDataReader reader)
    {
        return new BlocklistConfig
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Processes = reader.IsDBNull(2) 
                ? new List<string>() 
                : JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? new(),
            Domains = reader.IsDBNull(3) 
                ? new List<string>() 
                : JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new(),
            IsDefault = reader.GetInt32(4) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            UpdatedAt = DateTime.Parse(reader.GetString(6))
        };
    }

    #endregion

    #region Statistics

    public (int totalSessions, TimeSpan totalTime, int totalInterventions) GetAllTimeStats()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                COUNT(*) as TotalSessions,
                COALESCE(SUM(InterventionCount), 0) as TotalInterventions
            FROM SessionLogs";

        int totalSessions = 0;
        int totalInterventions = 0;

        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                totalSessions = reader.GetInt32(0);
                totalInterventions = reader.GetInt32(1);
            }
        }

        // Calculate total time
        command.CommandText = "SELECT StartTime, EndTime FROM SessionLogs";
        var totalTime = TimeSpan.Zero;

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var start = DateTime.Parse(reader.GetString(0));
                var end = DateTime.Parse(reader.GetString(1));
                totalTime += (end - start);
            }
        }

        return (totalSessions, totalTime, totalInterventions);
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
