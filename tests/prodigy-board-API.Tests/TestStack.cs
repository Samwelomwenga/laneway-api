using System.Net;
using System.Net.Http.Json;
using DefaultNamespace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Supabase.Storage;
using Supabase.Storage.Interfaces;
using Testcontainers.PostgreSql;
using Xunit;

namespace prodigy_board_API.Tests;

public sealed class TestStack : IAsyncLifetime
{
    public const string BucketName = "attachments-test";

    private const string TemplateDatabase = "prodigy_template";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .WithDatabase(TemplateDatabase)
        .Build();

    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(TestStack).Assembly)
        .AddEnvironmentVariables()
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await MigrateTemplateAsync();
        await PrepareBucketAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public async Task<string> NewDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():n}";
        await RunAsync($"""CREATE DATABASE "{name}" TEMPLATE "{TemplateDatabase}" """);
        return ConnectionStringFor(name);
    }

    public async Task DropDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await RunAsync($"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""");
    }

    private async Task MigrateTemplateAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionStringFor(TemplateDatabase))
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
    }

    // 'supabase start' reads supabase/config.toml only when the stack comes up, so a stack already
    // running has neither the bucket nor its limit.
    private async Task PrepareBucketAsync()
    {
        using var storage = Storage();

        using var found = await storage.GetAsync($"bucket/{BucketName}");
        if (found.StatusCode != HttpStatusCode.OK)
        {
            using var created = await storage.PostAsJsonAsync("bucket", new
            {
                id = BucketName,
                name = BucketName,
                @public = false,
                file_size_limit = FieldLimits.AttachmentBytes
            });
            created.EnsureSuccessStatusCode();
        }

        using var limited = await storage.PutAsJsonAsync($"bucket/{BucketName}", new
        {
            id = BucketName,
            @public = false,
            file_size_limit = FieldLimits.AttachmentBytes
        });
        limited.EnsureSuccessStatusCode();

        await EmptyBucketAsync();
    }

    // The objects go one by one, because the stack's own empty route queues the work for up to an hour.
    private async Task EmptyBucketAsync()
    {
        var bucket = new Client(StorageUrl(), StorageHeaders()).From(BucketName);
        var keys = await KeysAsync(bucket, string.Empty);
        if (keys.Count > 0)
        {
            await bucket.Remove(keys);
        }
    }

    // Storage lists one prefix at a time, a page at a time, and reports a folder as an entry with no id.
    private static async Task<List<string>> KeysAsync(IStorageFileApi<FileObject> bucket, string prefix)
    {
        const int pageSize = 1000;

        var keys = new List<string>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await bucket.List(prefix, new SearchOptions { Limit = pageSize, Offset = offset }) ?? [];
            foreach (var listed in page)
            {
                var key = prefix.Length == 0 ? listed.Name! : $"{prefix}/{listed.Name}";
                if (listed.Id is null)
                {
                    keys.AddRange(await KeysAsync(bucket, key));
                }
                else
                {
                    keys.Add(key);
                }
            }

            if (page.Count < pageSize)
            {
                return keys;
            }
        }
    }

    private HttpClient Storage()
    {
        var storage = new HttpClient { BaseAddress = new Uri($"{StorageUrl()}/") };
        foreach (var (name, value) in StorageHeaders())
        {
            storage.DefaultRequestHeaders.Add(name, value);
        }

        return storage;
    }

    private string StorageUrl()
    {
        var url = _configuration["Supabase:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw MissingSettings();
        }

        return $"{url.TrimEnd('/')}/storage/v1";
    }

    private Dictionary<string, string> StorageHeaders()
    {
        var key = _configuration["Supabase:ServiceRoleKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw MissingSettings();
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["apikey"] = key,
            ["Authorization"] = $"Bearer {key}"
        };
    }

    private static InvalidOperationException MissingSettings() =>
        new("The test run needs Supabase:Url and Supabase:ServiceRoleKey in user secrets, and "
            + "'supabase start' up on this project's own ports.");

    private async Task RunAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    // Pooling off, so a database has no connection left to stop it being copied or dropped.
    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = database,
            Pooling = false
        }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<TestStack>
{
    public const string Name = "api";
}
