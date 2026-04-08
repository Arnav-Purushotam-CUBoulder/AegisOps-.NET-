using System.Text.Json;
using AegisOps.Api.Models;
using Microsoft.Data.Sqlite;

namespace AegisOps.Api.Services;

public sealed class IncidentRepository
{
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public IncidentRepository(IConfiguration configuration)
    {
        var dbPath = configuration["Database:Path"] ?? "Data/aegisops.db";
        var fullPath = Path.GetFullPath(dbPath, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = $"Data Source={fullPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS incidents (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                service TEXT NOT NULL,
                environment TEXT NOT NULL,
                logs_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS triage_runs (
                id TEXT PRIMARY KEY,
                incident_id TEXT NOT NULL,
                category TEXT NOT NULL,
                severity TEXT NOT NULL,
                summary TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                generated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IncidentRecord SaveIncident(CreateIncidentRequest request)
    {
        var incident = new IncidentRecord(
            Id: Guid.NewGuid().ToString("N"),
            Title: request.Title,
            Description: request.Description,
            Service: request.Service,
            Environment: request.Environment,
            Logs: request.Logs,
            CreatedAtUtc: DateTime.UtcNow);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO incidents (id, title, description, service, environment, logs_json, created_at_utc)
            VALUES ($id, $title, $description, $service, $environment, $logsJson, $createdAtUtc)
            """;
        command.Parameters.AddWithValue("$id", incident.Id);
        command.Parameters.AddWithValue("$title", incident.Title);
        command.Parameters.AddWithValue("$description", incident.Description);
        command.Parameters.AddWithValue("$service", incident.Service);
        command.Parameters.AddWithValue("$environment", incident.Environment);
        command.Parameters.AddWithValue("$logsJson", JsonSerializer.Serialize(incident.Logs, _jsonOptions));
        command.Parameters.AddWithValue("$createdAtUtc", incident.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
        return incident;
    }

    public IReadOnlyList<IncidentRecord> ListIncidents()
    {
        var items = new List<IncidentRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, description, service, environment, logs_json, created_at_utc FROM incidents ORDER BY created_at_utc DESC";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new IncidentRecord(
                Id: reader.GetString(0),
                Title: reader.GetString(1),
                Description: reader.GetString(2),
                Service: reader.GetString(3),
                Environment: reader.GetString(4),
                Logs: JsonSerializer.Deserialize<List<string>>(reader.GetString(5), _jsonOptions) ?? [],
                CreatedAtUtc: DateTime.Parse(reader.GetString(6)).ToUniversalTime()));
        }
        return items;
    }

    public IncidentRecord? GetIncident(string id) =>
        ListIncidents().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void SaveTriage(string incidentId, TriageAssessment triage)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO triage_runs (id, incident_id, category, severity, summary, payload_json, generated_at_utc)
            VALUES ($id, $incidentId, $category, $severity, $summary, $payloadJson, $generatedAtUtc)
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$incidentId", incidentId);
        command.Parameters.AddWithValue("$category", triage.IncidentCategory);
        command.Parameters.AddWithValue("$severity", triage.Severity);
        command.Parameters.AddWithValue("$summary", triage.ExecutiveSummary);
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(triage, _jsonOptions));
        command.Parameters.AddWithValue("$generatedAtUtc", triage.GeneratedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }
}
