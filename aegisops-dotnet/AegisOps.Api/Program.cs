using AegisOps.Api.Models;
using AegisOps.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IncidentRepository>();
builder.Services.AddSingleton<KnowledgeBaseService>();
builder.Services.AddSingleton<OpenAiSummaryClient>();
builder.Services.AddSingleton<TriageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "AegisOps.Api" }));

app.MapGet("/api/incidents", (IncidentRepository repository) => Results.Ok(repository.ListIncidents()));

app.MapGet("/api/incidents/{id}", (string id, IncidentRepository repository) =>
{
    var incident = repository.GetIncident(id);
    return incident is null ? Results.NotFound() : Results.Ok(incident);
});

app.MapGet("/api/runbooks", (KnowledgeBaseService kb) => Results.Ok(kb.ListRunbooks()));

app.MapPost("/api/incidents", (CreateIncidentRequest request, IncidentRepository repository) =>
{
    var incident = repository.SaveIncident(request);
    return Results.Created($"/api/incidents/{incident.Id}", incident);
});

app.MapPost("/api/triage", async (CreateIncidentRequest request, IncidentRepository repository, TriageService triageService, CancellationToken cancellationToken) =>
{
    var incident = repository.SaveIncident(request);
    var triage = await triageService.AssessAsync(incident, cancellationToken);
    repository.SaveTriage(incident.Id, triage);
    return Results.Ok(new { incident, triage });
});

app.Run();
