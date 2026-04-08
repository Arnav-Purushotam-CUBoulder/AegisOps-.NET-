namespace AegisOps.Api.Models;

public sealed record CreateIncidentRequest(
    string Title,
    string Description,
    string Service,
    string Environment,
    IReadOnlyList<string> Logs);

public sealed record IncidentRecord(
    string Id,
    string Title,
    string Description,
    string Service,
    string Environment,
    IReadOnlyList<string> Logs,
    DateTime CreatedAtUtc);

public sealed record RunbookDocument(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Steps);

public sealed record SimilarIncident(
    string IncidentId,
    string Title,
    double Score,
    string Summary);

public sealed record RunbookMatch(
    string RunbookId,
    string Title,
    double Score,
    IReadOnlyList<string> Steps);

public sealed record TriageAssessment(
    string IncidentCategory,
    string Severity,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> SuspectedRootCauses,
    IReadOnlyList<string> ImmediateActions,
    IReadOnlyList<RunbookMatch> Runbooks,
    IReadOnlyList<SimilarIncident> SimilarIncidents,
    string ExecutiveSummary,
    DateTime GeneratedAtUtc);
