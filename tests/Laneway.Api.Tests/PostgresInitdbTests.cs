using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Laneway.Api.Tests;

public sealed class PostgresInitdbTests : IAsyncLifetime
{
    private const string MigratorPassword = "migrator-test-password";
    private const string AppPassword = "app-test-password";
    private const string KeyName = "initdb-test";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("laneway")
        .WithEnvironment("MIGRATOR_PASSWORD", MigratorPassword)
        .WithEnvironment("APP_PASSWORD", AppPassword)
        .WithResourceMapping(
            new FileInfo(PathOf("terraform/vm/postgres-initdb.sh")),
            "/docker-entrypoint-initdb.d/",
            fileMode: Unix.FileMode755)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task The_app_role_reads_and_writes_tables_the_migrator_created()
    {
        await using (var migrator = Context("laneway_migrator", MigratorPassword))
        {
            await migrator.Database.MigrateAsync();
        }

        await using var app = Context("laneway_app", AppPassword);
        app.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = KeyName, Xml = "<key />" });
        await app.SaveChangesAsync();

        Assert.Equal(1, await app.DataProtectionKeys.CountAsync(key => key.FriendlyName == KeyName));
        Assert.Equal(1, await app.DataProtectionKeys.Where(key => key.FriendlyName == KeyName).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task The_app_role_cannot_create_tables()
    {
        await using var connection = new NpgsqlConnection(ConnectionString("laneway_app", AppPassword));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE TABLE app_owned (id int)", connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private ApplicationDbContext Context(string user, string password) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString(user, password)).Options);

    private string ConnectionString(string user, string password) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = user,
            Password = password,
            Pooling = false
        }.ConnectionString;

    private static string PathOf(string relative, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "../..", relative);
}
