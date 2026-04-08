using System.Text.Json;
using AegisOps.Api.Models;

namespace AegisOps.Api.Services;

public sealed class KnowledgeBaseService
{
    private readonly IReadOnlyList<RunbookDocument> _runbooks;

    public KnowledgeBaseService(IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "runbooks.json");
        var text = File.ReadAllText(path);
        _runbooks = JsonSerializer.Deserialize<List<RunbookDocument>>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    public IReadOnlyList<RunbookDocument> ListRunbooks() => _runbooks;

    public IReadOnlyList<RunbookMatch> SearchRunbooks(string query, int take = 3)
    {
        return _runbooks
            .Select(runbook => new RunbookMatch(
                runbook.Id,
                runbook.Title,
                Similarity(query, $"{runbook.Title} {runbook.Summary} {string.Join(' ', runbook.Tags)} {string.Join(' ', runbook.Steps)}"),
                runbook.Steps))
            .OrderByDescending(match => match.Score)
            .Take(take)
            .ToList();
    }

    public IReadOnlyList<SimilarIncident> SearchIncidents(string query, IReadOnlyList<IncidentRecord> incidents, int take = 3)
    {
        return incidents
            .Select(incident => new SimilarIncident(
                incident.Id,
                incident.Title,
                Similarity(query, $"{incident.Title} {incident.Description} {incident.Service} {string.Join(' ', incident.Logs)}"),
                incident.Description))
            .OrderByDescending(match => match.Score)
            .Take(take)
            .ToList();
    }

    private static double Similarity(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var overlap = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return Math.Round((double)overlap / Math.Max(union, 1), 3);
    }

    private static HashSet<string> Tokenize(string text) =>
        text.Split([' ', '
', '', '	', ',', '.', ':', ';', '-', '_', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 2)
            .ToHashSet();
}
