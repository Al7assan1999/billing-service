using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;

namespace Billing.IntegrationTests;

/// <summary>Skipped unless RUN_DOCKER_TESTS=true (set in CI), since these tests start a SQL Server container.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_DOCKER_TESTS") != "true")
            Skip = "Set RUN_DOCKER_TESTS=true to run against a SQL Server container.";
    }
}

/// <summary>One SQL Server container for the whole test run; each test gets its own database.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public Task InitializeAsync() =>
        Environment.GetEnvironmentVariable("RUN_DOCKER_TESTS") == "true" ? _sql.StartAsync() : Task.CompletedTask;

    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();

    /// <summary>A client for the real API, wired to a fresh database (migrations run on startup).</summary>
    public HttpClient CreateApiClient()
    {
        var connectionString = _sql.GetConnectionString().Replace("Database=master", $"Database=Billing_{Guid.NewGuid():N}");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Billing", connectionString);
            b.UseSetting("Billing:EnableScheduledRun", "false");
        });
        return factory.CreateClient();
    }
}

[CollectionDefinition(nameof(SqlServerCollection))]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
