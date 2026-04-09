using System.Text.Json;
using AegisOps.Api.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AegisOps.Api.Services;

public sealed class TriageArchiveService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TriageArchiveService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public TriageArchiveService(IConfiguration configuration, ILogger<TriageArchiveService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TriageArchiveResult> ArchiveAsync(
        IncidentRecord incident,
        TriageAssessment triage,
        CancellationToken cancellationToken)
    {
        var provider = (_configuration["Archive:Provider"] ?? "local").Trim().ToLowerInvariant();
        return provider == "azure-blob"
            ? await ArchiveToAzureBlobOrFallbackAsync(incident, triage, cancellationToken)
            : await ArchiveToLocalAsync(incident, triage, cancellationToken);
    }

    private async Task<TriageArchiveResult> ArchiveToAzureBlobOrFallbackAsync(
        IncidentRecord incident,
        TriageAssessment triage,
        CancellationToken cancellationToken)
    {
        var connectionString = _configuration["Archive:AzureBlobConnectionString"];
        var containerName = _configuration["Archive:AzureBlobContainer"];
        var prefix = (_configuration["Archive:AzureBlobPrefix"] ?? "triage-exports").Trim('/');

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(containerName))
        {
            _logger.LogWarning("Archive provider is azure-blob but Blob Storage settings are incomplete. Falling back to local storage.");
            return await ArchiveToLocalAsync(incident, triage, cancellationToken);
        }

        try
        {
            var payload = SerializeEnvelope(incident, triage);
            var blobPath = $"{prefix}/{incident.CreatedAtUtc:yyyy/MM/dd}/{incident.Id}.json";
            var containerClient = new BlobContainerClient(connectionString, containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            var blobClient = containerClient.GetBlobClient(blobPath);
            await blobClient.UploadAsync(BinaryData.FromString(payload), overwrite: true, cancellationToken);

            return new TriageArchiveResult(
                Provider: "azure-blob",
                Location: blobClient.Uri.ToString(),
                ArchivedAtUtc: DateTime.UtcNow);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to archive triage payload to Azure Blob Storage. Falling back to local storage.");
            return await ArchiveToLocalAsync(incident, triage, cancellationToken);
        }
    }

    private async Task<TriageArchiveResult> ArchiveToLocalAsync(
        IncidentRecord incident,
        TriageAssessment triage,
        CancellationToken cancellationToken)
    {
        var relativePath = _configuration["Archive:LocalPath"] ?? "Data/triage-exports";
        var archiveRoot = Path.GetFullPath(relativePath, AppContext.BaseDirectory);
        Directory.CreateDirectory(archiveRoot);

        var archivePath = Path.Combine(archiveRoot, $"{incident.Id}.json");
        await File.WriteAllTextAsync(archivePath, SerializeEnvelope(incident, triage), cancellationToken);

        return new TriageArchiveResult(
            Provider: "local",
            Location: archivePath,
            ArchivedAtUtc: DateTime.UtcNow);
    }

    private string SerializeEnvelope(IncidentRecord incident, TriageAssessment triage)
    {
        var payload = new
        {
            incident,
            triage
        };
        return JsonSerializer.Serialize(payload, _jsonOptions);
    }
}
