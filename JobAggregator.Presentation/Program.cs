using JobAggregator.BusinessLogic;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Services;
using JobAggregator.DataAccess;
using JobAggregator.DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);
builder.Services.AddCors(options => options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddBusinessLogic();
var app = builder.Build();
app.UseCors("AllowAll");

// Functional test build: authentication will be added with Cognito.
app.MapPost("/api/jobs/init-db", async (HttpContext context, AppDbContext db) =>
{
    if (!app.Environment.IsDevelopment()) return Results.NotFound();
    await db.Database.MigrateAsync();
    return Results.Ok(new { Message = "Migration complete" });
});
app.MapGet("/api/jobs/facebook-groups", async (int? page, JobCatalogService catalog) => Results.Ok(await catalog.GroupsAsync(page ?? 1)));
app.MapPost("/api/jobs/search", async (SearchCriteriaDto criteria, JobCatalogService catalog) =>
{
    try { return Results.Ok(await catalog.ReadAsync(criteria)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
});
app.MapPost("/api/jobs/scrape", async (SearchCriteriaDto criteria, HttpContext context, JobCatalogService catalog) =>
{
    try { return Results.Ok(await catalog.StartAsync(criteria, context.Request.Headers["Idempotency-Key"].ToString())); }
    catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
});
app.MapGet("/api/jobs/search/{requestId:guid}", async (Guid requestId, JobCatalogService catalog) =>
{
    var result = await catalog.StatusAsync(requestId);
    return result == null ? Results.NotFound() : Results.Ok(result);
});
app.MapPost("/api/jobs/scrape/{id:guid}/retry", async (Guid id, JobCatalogService catalog) =>
{
    try { return Results.Ok(await catalog.RetryAsync(id)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
});
app.Run();
