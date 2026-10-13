using System.Text.Json.Serialization;
using Billing.Api;
using Billing.Infrastructure;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBilling(builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BillingExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.MapOpenApi();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

public partial class Program;
