using AegisOps.Api.Models;

namespace AegisOps.Api.Services;

public sealed class TriageService
{
    private readonly KnowledgeBaseService _knowledgeBase;
    private readonly IncidentRepository _incidentRepository;
    private readonly OpenAiSummaryClient _summaryClient;

    public TriageService(
        KnowledgeBaseService knowledgeBase,
        IncidentRepository incidentRepository,
        OpenAiSummaryClient summaryClient)
    {
        _knowledgeBase = knowledgeBase;
        _incidentRepository = incidentRepository;
        _summaryClient = summaryClient;
    }

    public async Task<TriageAssessment> AssessAsync(IncidentRecord incident, CancellationToken cancellationToken)
    {
        var corpus = $"{incident.Title}\n{incident.Description}\n{string.Join(' ', incident.Logs)}".ToLowerInvariant();
        var category = Classify(corpus);
        var severity = ScoreSeverity(corpus);
        var evidence = ExtractEvidence(incident, category, severity);
        var rootCauses = InferRootCauses(corpus, category);
        var actions = RecommendActions(category, severity);
        var runbooks = _knowledgeBase.SearchRunbooks(corpus);
        var similarIncidents = _knowledgeBase.SearchIncidents(corpus, _incidentRepository.ListIncidents().Where(x => x.Id != incident.Id).ToList());

        var draft = new TriageAssessment(
            IncidentCategory: category,
            Severity: severity,
            Evidence: evidence,
            SuspectedRootCauses: rootCauses,
            ImmediateActions: actions,
            Runbooks: runbooks,
            SimilarIncidents: similarIncidents,
            ExecutiveSummary: BuildFallbackSummary(incident, category, severity, rootCauses, actions, runbooks),
            GeneratedAtUtc: DateTime.UtcNow);

        var aiSummary = await _summaryClient.TrySummarizeAsync(incident, draft, cancellationToken);
        return draft with { ExecutiveSummary = aiSummary ?? draft.ExecutiveSummary };
    }

    private static string Classify(string corpus)
    {
        if (ContainsAny(corpus, "unauthorized", "token", "auth", "forbidden", "permission")) return "Authentication / Authorization";
        if (ContainsAny(corpus, "oom", "out of memory", "memory", "evicted", "oomkill")) return "Memory Pressure";
        if (ContainsAny(corpus, "latency", "queue", "timeout", "slow", "p99")) return "Performance Regression";
        if (ContainsAny(corpus, "dns", "network", "connection", "refused", "unreachable")) return "Network Connectivity";
        if (ContainsAny(corpus, "deploy", "migration", "schema", "rollout", "config")) return "Deployment / Configuration";
        return "General Service Degradation";
    }

    private static string ScoreSeverity(string corpus)
    {
        var critical = CountAny(corpus, "outage", "sev1", "down", "all traffic", "data loss", "payment failures");
        var high = CountAny(corpus, "p99", "error rate", "timeout", "queue", "gpu-utilization", "5xx");
        if (critical >= 1) return "SEV-1";
        if (high >= 2) return "SEV-2";
        if (high == 1 || corpus.Contains("prod")) return "SEV-3";
        return "SEV-4";
    }

    private static IReadOnlyList<string> ExtractEvidence(IncidentRecord incident, string category, string severity)
    {
        var evidence = new List<string>
        {
            $"Assigned category: {category}",
            $"Assigned severity: {severity}",
            $"Service impacted: {incident.Service} in {incident.Environment}"
        };
        evidence.AddRange(incident.Logs.Take(3).Select(log => $"Observed signal: {log}"));
        return evidence;
    }

    private static IReadOnlyList<string> InferRootCauses(string corpus, string category)
    {
        var items = new List<string>();
        if (category == "Performance Regression")
        {
            items.Add("Recent deploy likely changed cache behavior or request scheduling.");
            items.Add("Rising queue depth suggests saturation upstream or reduced worker throughput.");
        }
        if (category == "Memory Pressure")
        {
            items.Add("Container or node memory ceiling is likely too low for current batch size.");
            items.Add("A recent build may have increased model footprint or cache retention.");
        }
        if (category == "Authentication / Authorization")
        {
            items.Add("Expired credentials or rotated secrets likely broke downstream calls.");
        }
        if (category == "Deployment / Configuration")
        {
            items.Add("Configuration drift or an incompatible rollout likely introduced the failure.");
        }
        if (items.Count == 0)
        {
            items.Add("The failure pattern suggests a dependency or configuration regression.");
        }
        return items;
    }

    private static IReadOnlyList<string> RecommendActions(string category, string severity)
    {
        var items = new List<string>();
        if (severity == "SEV-1" || severity == "SEV-2")
        {
            items.Add("Page the on-call owner and freeze further rollouts until the blast radius is understood.");
        }
        if (category == "Performance Regression")
        {
            items.Add("Compare pre- and post-deploy latency, cache hit rate, and worker concurrency.");
            items.Add("Consider traffic shifting or rollback if queue growth continues.");
        }
        else if (category == "Memory Pressure")
        {
            items.Add("Inspect RSS / GPU memory growth and lower batch size or cache limits.");
        }
        else if (category == "Authentication / Authorization")
        {
            items.Add("Validate tokens, secret rotation status, and IAM or RBAC changes.");
        }
        else
        {
            items.Add("Review the latest deploy, config diff, and the first failing dependency call.");
        }
        return items;
    }

    private static string BuildFallbackSummary(
        IncidentRecord incident,
        string category,
        string severity,
        IReadOnlyList<string> rootCauses,
        IReadOnlyList<string> actions,
        IReadOnlyList<RunbookMatch> runbooks)
    {
        return $"""
## Situation
- {incident.Title} impacts `{incident.Service}` in `{incident.Environment}`.
- Classified as **{category}** with **{severity}** severity.

## Likely causes
- {string.Join("\n- ", rootCauses)}

## Immediate actions
- {string.Join("\n- ", actions)}

## Suggested runbooks
- {string.Join("\n- ", runbooks.Select(r => r.Title))}
""";
    }

    private static bool ContainsAny(string corpus, params string[] needles) =>
        needles.Any(corpus.Contains);

    private static int CountAny(string corpus, params string[] needles) =>
        needles.Count(corpus.Contains);
}
