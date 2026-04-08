using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AegisOps.Api.Models;

namespace AegisOps.Api.Services;

public sealed class OpenAiSummaryClient
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<OpenAiSummaryClient> _logger;

    public OpenAiSummaryClient(IHttpClientFactory clientFactory, ILogger<OpenAiSummaryClient> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<string?> TrySummarizeAsync(IncidentRecord incident, TriageAssessment triage, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1-mini";
        var client = _clientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model,
            input = $"""
You are an incident commander assistant. Produce a concise markdown triage brief.

Incident:
- Title: {incident.Title}
- Service: {incident.Service}
- Environment: {incident.Environment}
- Description: {incident.Description}
- Logs: {string.Join(" | ", incident.Logs)}

Assessment:
- Category: {triage.IncidentCategory}
- Severity: {triage.Severity}
- Evidence: {string.Join("; ", triage.Evidence)}
- Root causes: {string.Join("; ", triage.SuspectedRootCauses)}
- Immediate actions: {string.Join("; ", triage.ImmediateActions)}
- Runbooks: {string.Join("; ", triage.Runbooks.Select(r => r.Title))}
- Similar incidents: {string.Join("; ", triage.SimilarIncidents.Select(r => r.Title))}

Return 4 sections with short bullets: Situation, Likely Causes, Immediate Actions, What To Check Next.
"""
        };

        using var response = await client.PostAsync(
            "https://api.openai.com/v1/responses",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI summary request failed with status {StatusCode}", response.StatusCode);
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString();
        }

        return null;
    }
}
