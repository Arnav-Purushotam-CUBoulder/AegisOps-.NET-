using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AegisOps.Api.Models;
using AegisOps.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AegisOps.Api.Tests;

public sealed class TriageWorkflowTests
{
    [Fact]
    public async Task AssessAsync_ClassifiesAuthenticationIncidentAndReturnsRunbooks()
    {
        using var fixture = new TriageFixture();
        var service = fixture.CreateTriageService();

        var priorIncident = fixture.Repository.SaveIncident(new CreateIncidentRequest(
            Title: "Auth token failures on checkout API",
            Description: "Rotated secrets broke downstream calls for the checkout workflow.",
            Service: "checkout-api",
            Environment: "production",
            Logs: ["401 unauthorized from payment provider", "token validation failed"]));

        var incident = fixture.Repository.SaveIncident(new CreateIncidentRequest(
            Title: "Production outage after token rotation",
            Description: "All traffic started failing after an auth token rollout.",
            Service: "checkout-api",
            Environment: "production",
            Logs: ["401 unauthorized from billing service", "permission denied for webhook callback", "sev1 outage"]));

        var assessment = await service.AssessAsync(incident, CancellationToken.None);

        assessment.IncidentCategory.Should().Be("Authentication / Authorization");
        assessment.Severity.Should().Be("SEV-1");
        assessment.Runbooks.Should().NotBeEmpty();
        assessment.ExecutiveSummary.Should().Contain("## Situation");
        assessment.ImmediateActions.Should().Contain(action => action.Contains("Validate tokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchiveAsync_FallsBackToLocalStorageWhenAzureBlobSettingsAreIncomplete()
    {
        using var fixture = new TriageFixture(new Dictionary<string, string?>
        {
            ["Archive:Provider"] = "azure-blob",
            ["Archive:LocalPath"] = "Data/test-triage-exports",
        });

        var incident = fixture.Repository.SaveIncident(new CreateIncidentRequest(
            Title: "Queue latency spike",
            Description: "Background jobs are timing out in production.",
            Service: "worker-api",
            Environment: "production",
            Logs: ["queue depth rising", "p99 timeout in worker"]));

        var triage = new TriageAssessment(
            IncidentCategory: "Performance Regression",
            Severity: "SEV-2",
            Evidence: ["Assigned severity: SEV-2"],
            SuspectedRootCauses: ["Workers are saturated."],
            ImmediateActions: ["Consider rollback."],
            Runbooks: [],
            SimilarIncidents: [],
            ExecutiveSummary: "Fallback summary",
            GeneratedAtUtc: DateTime.UtcNow);

        var result = await fixture.ArchiveService.ArchiveAsync(incident, triage, CancellationToken.None);

        result.Provider.Should().Be("local");
        File.Exists(result.Location).Should().BeTrue();
    }

    private sealed class TriageFixture : IDisposable
    {
        private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"aegisops-tests-{Guid.NewGuid():N}");
        private readonly TestHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        public TriageFixture(Dictionary<string, string?>? overrides = null)
        {
            Directory.CreateDirectory(_tempRoot);
            var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../AegisOps.Api"));
            _environment = new TestHostEnvironment(apiRoot);

            var settings = new Dictionary<string, string?>
            {
                ["Database:Path"] = Path.Combine(_tempRoot, "aegisops.db"),
                ["Archive:LocalPath"] = Path.Combine(_tempRoot, "triage-exports"),
            };

            if (overrides is not null)
            {
                foreach (var pair in overrides)
                {
                    settings[pair.Key] = pair.Value;
                }
            }

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            Repository = new IncidentRepository(_configuration);
            KnowledgeBase = new KnowledgeBaseService(_environment);
            SummaryClient = new OpenAiSummaryClient(new StubHttpClientFactory(), NullLogger<OpenAiSummaryClient>.Instance);
            ArchiveService = new TriageArchiveService(_configuration, NullLogger<TriageArchiveService>.Instance);
        }

        public IncidentRepository Repository { get; }

        public KnowledgeBaseService KnowledgeBase { get; }

        public OpenAiSummaryClient SummaryClient { get; }

        public TriageArchiveService ArchiveService { get; }

        public TriageService CreateTriageService() => new(KnowledgeBase, Repository, SummaryClient);

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "AegisOps.Api.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
