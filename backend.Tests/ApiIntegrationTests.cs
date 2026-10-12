using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KiComLibBackend.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public string ConnectionString { get; }
    public bool DatabaseAvailable { get; private set; }

    public CustomWebApplicationFactory()
    {
        string testData = Path.Combine(Path.GetTempPath(), "kicomlib-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(testData, "keys"));
        Directory.CreateDirectory(Path.Combine(testData, "backups"));
        Environment.SetEnvironmentVariable("DATA_PROTECTION_KEYS_DIR", Path.Combine(testData, "keys"));
        Environment.SetEnvironmentVariable("BACKUP_DIR", Path.Combine(testData, "backups"));
        Environment.SetEnvironmentVariable("BACKUP_RESTART_AFTER_RESTORE", "false");
        Environment.SetEnvironmentVariable("PROJECT_WORKER_ENABLED", "false");
        Environment.SetEnvironmentVariable("GIT_ALLOW_FILE_PROTOCOL", "true");
        ClientOptions.HandleCookies = false;
        ConnectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? "Host=localhost;Port=15432;Database=kicaddbl_test;Username=kicad;Password=test123";

        try
        {
            ResetTestDatabaseAsync(ConnectionString).GetAwaiter().GetResult();
            NpgsqlConnectionStringBuilder csb = new(ConnectionString);
            Environment.SetEnvironmentVariable("DB_HOST", csb.Host);
            Environment.SetEnvironmentVariable("DB_PORT", csb.Port.ToString());
            Environment.SetEnvironmentVariable("DB_NAME", csb.Database);
            Environment.SetEnvironmentVariable("DB_USER", csb.Username);
            Environment.SetEnvironmentVariable("DB_PASSWORD", csb.Password);
            Environment.SetEnvironmentVariable("DB_AUTO_INIT", "true");
            Environment.SetEnvironmentVariable("PROJECT_WORKER_ENABLED", "false");
            Environment.SetEnvironmentVariable("GIT_ALLOW_FILE_PROTOCOL", "true");
            DatabaseAvailable = true;
        }
        catch (Exception)
        {
            DatabaseAvailable = false;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);
        if (!DatabaseAvailable)
        {
            return host;
        }

        // The seeded admin hash is the published default. Replace it with another hash of the
        // same password so the rest of the suite is not stuck on the forced password change.
        using IServiceScope scope = host.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.AppUsers.FirstOrDefault(u => u.Username == "admin");
        if (admin != null && PasswordPolicy.IsDefaultHash(admin.PasswordHash))
        {
            admin.PasswordHash = PasswordHasher.Hash(PasswordPolicy.DefaultPassword);
            db.SaveChanges();
        }

        return host;
    }

    public static async Task ResetTestDatabaseAsync(string connectionString)
    {
        NpgsqlConnectionStringBuilder csb = new(connectionString);
        string dbName = csb.Database!;
        csb.Database = "postgres";
        csb.Pooling = false;
        await using NpgsqlConnection admin = new(csb.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand term = new(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid()",
            admin))
        {
            await term.ExecuteNonQueryAsync();
        }

        await using (NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{dbName}\"", admin))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (NpgsqlCommand create = new($"CREATE DATABASE \"{dbName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        string scriptDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "backend", "Data", "Sql"));
        DatabaseInitializerOptions options = new()
        {
            ConnectionString = connectionString,
            ScriptDirectory = scriptDir,
            Owner = "kicad"
        };
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        DatabaseInitializer initializer = new(options, loggerFactory.CreateLogger<DatabaseInitializer>());
        await initializer.InitializeAsync();
    }
}

[Collection("kicomlib-db")]
public class ApiIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private string? _adminToken;

    public ApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        if (!factory.DatabaseAvailable)
        {
            return;
        }

        HttpResponseMessage login = _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "kicomlib" }).GetAwaiter().GetResult();
        string body = login.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!login.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"admin login failed ({(int)login.StatusCode}): {body}");
        }

        _adminToken = TestSessions.UseBearer(_client, login);
    }

    private void UseAdmin(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
    }

    private bool RequireDb()
    {
        return _factory.DatabaseAvailable;
    }

    [Fact]
    public async Task Datasheet_File_OpensInline_And_Downloads()
    {
        if (!RequireDb()) return;

        byte[] pdf = Encoding.UTF8.GetBytes("%PDF-1.4 datasheet");
        string hash = await PutBlobAsync(pdf, "application/pdf");
        JsonElement created = await PostCommitAsync(new
        {
            message = "create datasheet file",
            author = "tester",
            changes = new object[]
            {
                new { entity = "datasheet", op = "create", uuid = Guid.NewGuid(), blobHash = hash, directory = "Docs", filename = "Board.pdf" }
            }
        });
        int id = created.GetProperty("results")[0].GetProperty("id").GetInt32();

        HttpResponseMessage inline = await _client.GetAsync("/api/datasheet/" + id + "/file");
        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Equal("application/pdf", inline.Content.Headers.ContentType?.MediaType);
        string inlineDisposition = inline.Content.Headers.ContentDisposition?.ToString() ?? "";
        Assert.Contains("inline", inlineDisposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Board.pdf", inlineDisposition, StringComparison.Ordinal);
        Assert.Equal(pdf, await inline.Content.ReadAsByteArrayAsync());

        HttpResponseMessage download = await _client.GetAsync("/api/datasheet/" + id + "/file?download=true");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        string downloadDisposition = download.Content.Headers.ContentDisposition?.ToString() ?? "";
        Assert.Contains("attachment", downloadDisposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Board.pdf", downloadDisposition, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/datasheet/999999/file")).StatusCode);
    }

    [Fact]
    public async Task Blob_Put_HashMismatch_Returns400_And_IdempotentRePut_Returns200()
    {
        if (!RequireDb()) return;
        byte[] data = Encoding.UTF8.GetBytes("(symbol \"Test\" )\n");
        string hash = Sha256(data);

        using HttpRequestMessage bad = new(HttpMethod.Put, $"/api/blob/{new string('0', 64)}");
        bad.Content = new ByteArrayContent(data);
        bad.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        bad.Headers.Add("X-Media-Type", "text/x-kicad-sexpr");
        HttpResponseMessage badResp = await _client.SendAsync(bad);
        Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);

        using HttpRequestMessage put1 = new(HttpMethod.Put, $"/api/blob/{hash}");
        put1.Content = new ByteArrayContent(data);
        put1.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        put1.Headers.Add("X-Media-Type", "text/x-kicad-sexpr");
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(put1)).StatusCode);

        using HttpRequestMessage put2 = new(HttpMethod.Put, $"/api/blob/{hash}");
        put2.Content = new ByteArrayContent(data);
        put2.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        put2.Headers.Add("X-Media-Type", "text/x-kicad-sexpr");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(put2)).StatusCode);
    }

    [Fact]
    public async Task Commit_Symbol_Create_Update_Rename_Delete_And_Conflict()
    {
        if (!RequireDb()) return;

        string content1 = "(symbol \"S1\" (pin))\n";
        string content2 = "(symbol \"S1\" (pin) (property \"Value\" \"x\"))\n";
        string hash1 = await PutBlobAsync(content1);
        string hash2 = await PutBlobAsync(content2);

        JsonElement createResp = await PostCommitAsync(new
        {
            message = "create symbol",
            author = "tester",
            client = "tests",
            changes = new object[]
            {
                new { entity = "symbol", op = "create", blobHash = hash1, libraryFileId = 2, name = "S1", reference = "U" }
            }
        });
        Guid uuid = createResp.GetProperty("results")[0].GetProperty("uuid").GetGuid();
        string? headHash = createResp.GetProperty("results")[0].GetProperty("hash").GetString();

        JsonElement updateResp = await PostCommitAsync(new
        {
            message = "update symbol",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "update", uuid, expectedHash = headHash, blobHash = hash2, libraryFileId = 2, name = "S1" }
            }
        });
        headHash = updateResp.GetProperty("results")[0].GetProperty("hash").GetString();

        int countBefore = await CountSymbolRevisionsAsync(uuid);
        HttpResponseMessage conflict = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "conflict",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "update", uuid, expectedHash = hash1, blobHash = hash2, libraryFileId = 2, name = "S1" }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(countBefore, await CountSymbolRevisionsAsync(uuid));

        JsonElement renameResp = await PostCommitAsync(new
        {
            message = "rename",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "rename", uuid, expectedHash = headHash, name = "S1_renamed" }
            }
        });
        headHash = renameResp.GetProperty("results")[0].GetProperty("hash").GetString();

        await PostCommitAsync(new
        {
            message = "delete",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "delete", uuid, expectedHash = headHash }
            }
        });

        string body = await (await _client.GetAsync("/api/symbol")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("S1_renamed", body);
    }

    [Fact]
    public async Task Commit_Atomicity_OneBadChange_PersistsNothing()
    {
        if (!RequireDb()) return;
        string hash = await PutBlobAsync("(symbol \"A\" )\n");
        int commitsBefore = await CountCommitsAsync();

        HttpResponseMessage resp = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "atomic fail",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "create", blobHash = hash, libraryFileId = 2, name = "AtomicOk" },
                new { entity = "symbol", op = "update", uuid = Guid.NewGuid(), expectedHash = hash, blobHash = hash, name = "Nope" }
            }
        });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(commitsBefore, await CountCommitsAsync());
        Assert.DoesNotContain("AtomicOk", await (await _client.GetAsync("/api/symbol")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Footprint_WithTwoModels_RoundTrip()
    {
        if (!RequireDb()) return;
        string modelA = await PutBlobAsync(Encoding.UTF8.GetBytes("solid A"), "model/step");
        string modelB = await PutBlobAsync(Encoding.UTF8.GetBytes("solid B"), "model/step");
        string fpData = await PutBlobAsync("(module FP (layer F.Cu))\n");

        Guid uuidA = Guid.NewGuid();
        Guid uuidB = Guid.NewGuid();

        JsonElement commit = await PostCommitAsync(new
        {
            message = "fp with 2 models",
            author = "tester",
            changes = new object[]
            {
                new { entity = "model3d", op = "create", uuid = uuidA, blobHash = modelA, directory = "Pkg", filename = "A.step" },
                new { entity = "model3d", op = "create", uuid = uuidB, blobHash = modelB, directory = "Pkg", filename = "B.step" },
                new
                {
                    entity = "footprint",
                    op = "create",
                    blobHash = fpData,
                    libraryFileId = 1,
                    name = "FP_TWO",
                    type = 0,
                    layer = "F.Cu",
                    models = new object[]
                    {
                        new { position = 0, model3dUuid = uuidA, offset = new[] { 0f, 0f, 0f }, scale = new[] { 1f, 1f, 1f }, rotate = new[] { 0f, 0f, 0f }, hide = false },
                        new { position = 1, model3dUuid = uuidB, offset = new[] { 0f, 0f, 1.2f }, scale = new[] { 1f, 1f, 1f }, rotate = new[] { 0f, 0f, 90f }, hide = false }
                    }
                }
            }
        });

        int fpId = commit.GetProperty("results").EnumerateArray().First(r => r.GetProperty("entity").GetString() == "footprint").GetProperty("id").GetInt32();
        JsonElement body = await (await _client.GetAsync($"/api/footprint/{fpId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("models").GetArrayLength());
        Assert.Equal(1.2f, body.GetProperty("models")[1].GetProperty("offset")[2].GetSingle());
        Assert.Equal(90f, body.GetProperty("models")[1].GetProperty("rotate")[2].GetSingle());

        // List endpoint also includes models[] (no N+1 enrichment needed on client)
        JsonElement listBody = await (await _client.GetAsync("/api/footprint")).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement listed = listBody.EnumerateArray().First(e => e.GetProperty("id").GetInt32() == fpId);
        Assert.Equal(2, listed.GetProperty("models").GetArrayLength());
    }

    [Fact]
    public async Task Footprint_Update_WithoutModels_CarriesOverPreviousModels()
    {
        if (!RequireDb()) return;

        string modelA = await PutBlobAsync(Encoding.UTF8.GetBytes("carry A"), "model/step");
        string fp1 = await PutBlobAsync("(module FP_CARRY (layer F.Cu))\n");
        string fp2 = await PutBlobAsync("(module FP_CARRY (layer F.Cu) (attr smd))\n");
        Guid uuidA = Guid.NewGuid();

        JsonElement create = await PostCommitAsync(new
        {
            message = "create fp with model",
            author = "tester",
            changes = new object[]
            {
                new { entity = "model3d", op = "create", uuid = uuidA, blobHash = modelA, directory = "Pkg", filename = "Carry.step" },
                new
                {
                    entity = "footprint",
                    op = "create",
                    blobHash = fp1,
                    libraryFileId = 1,
                    name = "FP_CARRY",
                    type = 0,
                    layer = "F.Cu",
                    models = new object[]
                    {
                        new { position = 0, model3dUuid = uuidA, offset = new[] { 0f, 0f, 0.5f }, scale = new[] { 1f, 1f, 1f }, rotate = new[] { 0f, 0f, 0f }, hide = false }
                    }
                }
            }
        });

        JsonElement fpResult = create.GetProperty("results").EnumerateArray().First(r => r.GetProperty("entity").GetString() == "footprint");
        Guid fpUuid = fpResult.GetProperty("uuid").GetGuid();
        int fpId = fpResult.GetProperty("id").GetInt32();
        string? headHash = fpResult.GetProperty("hash").GetString();

        // Update without Models → previous model rows must be copied
        await PostCommitAsync(new
        {
            message = "update fp text only",
            author = "tester",
            changes = new object[]
            {
                new { entity = "footprint", op = "update", uuid = fpUuid, expectedHash = headHash, blobHash = fp2, libraryFileId = 1, name = "FP_CARRY" }
            }
        });

        JsonElement detail = await (await _client.GetAsync($"/api/footprint/{fpId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, detail.GetProperty("models").GetArrayLength());
        Assert.Equal(0.5f, detail.GetProperty("models")[0].GetProperty("offset")[2].GetSingle());
        Assert.Equal(uuidA, detail.GetProperty("models")[0].GetProperty("model3dUuid").GetGuid());
    }

    [Fact]
    public async Task Commit_Concurrent_SameExpectedHash_OneWinsOneConflicts()
    {
        if (!RequireDb()) return;

        string h1 = await PutBlobAsync("(symbol \"Conc\" (pin 1))\n");
        string h2a = await PutBlobAsync("(symbol \"Conc\" (pin 2a))\n");
        string h2b = await PutBlobAsync("(symbol \"Conc\" (pin 2b))\n");

        JsonElement created = await PostCommitAsync(new
        {
            message = "concurrent base",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "create", blobHash = h1, libraryFileId = 2, name = "ConcSym", reference = "U" }
            }
        });
        Guid uuid = created.GetProperty("results")[0].GetProperty("uuid").GetGuid();
        string? headHash = created.GetProperty("results")[0].GetProperty("hash").GetString();

        object payloadA = new
        {
            message = "concurrent A",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "update", uuid, expectedHash = headHash, blobHash = h2a, libraryFileId = 2, name = "ConcSym" }
            }
        };
        object payloadB = new
        {
            message = "concurrent B",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "update", uuid, expectedHash = headHash, blobHash = h2b, libraryFileId = 2, name = "ConcSym" }
            }
        };

        HttpClient clientA = _factory.CreateClient();
        HttpClient clientB = _factory.CreateClient();
        UseAdmin(clientA);
        UseAdmin(clientB);
        Task<HttpResponseMessage> tA = clientA.PostAsJsonAsync("/api/commit", payloadA);
        Task<HttpResponseMessage> tB = clientB.PostAsJsonAsync("/api/commit", payloadB);
        HttpResponseMessage[] responses = await Task.WhenAll(tA, tB);

        int createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        int conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, createdCount);
        Assert.Equal(1, conflictCount);
    }

    [Fact]
    public async Task Diff_Endpoint_Returns_UnifiedDiff_WithChangedLine()
    {
        if (!RequireDb()) return;
        string h1 = await PutBlobAsync("(symbol \"D\" (pin 1))\n");
        string h2 = await PutBlobAsync("(symbol \"D\" (pin 2))\n");

        JsonElement created = await PostCommitAsync(new
        {
            message = "diff create",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "create", blobHash = h1, libraryFileId = 2, name = "DiffSym", reference = "U" }
            }
        });
        Guid uuid = created.GetProperty("results")[0].GetProperty("uuid").GetGuid();
        int id = created.GetProperty("results")[0].GetProperty("id").GetInt32();
        string? hash = created.GetProperty("results")[0].GetProperty("hash").GetString();

        await PostCommitAsync(new
        {
            message = "diff update",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "update", uuid, expectedHash = hash, blobHash = h2, libraryFileId = 2, name = "DiffSym" }
            }
        });

        JsonElement body = await (await _client.GetAsync($"/api/symbol/{id}/diff?from=1&to=2")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unified", body.GetProperty("format").GetString());
        string text = body.GetProperty("diff").GetString()!;
        Assert.Contains("(pin 2)", text);
        Assert.Contains("-", text);
        Assert.Contains("+", text);
    }

    [Fact]
    public async Task Delete_Rejected_WhenSymbolFootprintDatasheetOrModelStillUsed()
    {
        if (!RequireDb()) return;

        string symHash = await PutBlobAsync("(symbol \"UsedSym\" )\n");
        JsonElement sym = await PostCommitAsync(new
        {
            message = "create used symbol",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "create", blobHash = symHash, libraryFileId = 2, name = "UsedSym" }
            }
        });
        Guid symUuid = sym.GetProperty("results")[0].GetProperty("uuid").GetGuid();
        int symId = sym.GetProperty("results")[0].GetProperty("id").GetInt32();
        string symHead = sym.GetProperty("results")[0].GetProperty("hash").GetString()!;

        string fpHash = await PutBlobAsync("(module UsedFp (layer F.Cu))\n");
        string modelHash = await PutBlobAsync(Encoding.UTF8.GetBytes("solid Used"), "model/step");
        string modelFpHash = await PutBlobAsync("(module ModelOnlyFp (layer F.Cu))\n");
        Guid modelUuid = Guid.NewGuid();
        JsonElement fp = await PostCommitAsync(new
        {
            message = "create used footprint and model",
            author = "tester",
            changes = new object[]
            {
                new { entity = "model3d", op = "create", uuid = modelUuid, blobHash = modelHash, directory = "Pkg", filename = "Used.step" },
                new
                {
                    entity = "footprint",
                    op = "create",
                    blobHash = fpHash,
                    libraryFileId = 1,
                    name = "UsedFp",
                    layer = "F.Cu"
                },
                new
                {
                    entity = "footprint",
                    op = "create",
                    blobHash = modelFpHash,
                    libraryFileId = 1,
                    name = "ModelOnlyFp",
                    layer = "F.Cu",
                    models = new object[]
                    {
                        new { position = 0, model3dUuid = modelUuid, offset = new[] { 0f, 0f, 0f }, scale = new[] { 1f, 1f, 1f }, rotate = new[] { 0f, 0f, 0f }, hide = false }
                    }
                }
            }
        });
        List<JsonElement> fpResults = fp.GetProperty("results").EnumerateArray().Where(r => r.GetProperty("entity").GetString() == "footprint").ToList();
        JsonElement usedFp = fpResults[0];
        JsonElement modelFp = fpResults[1];
        Guid fpUuid = usedFp.GetProperty("uuid").GetGuid();
        int fpId = usedFp.GetProperty("id").GetInt32();
        string fpHead = usedFp.GetProperty("hash").GetString()!;
        Guid modelFpUuid = modelFp.GetProperty("uuid").GetGuid();
        string modelFpHead = modelFp.GetProperty("hash").GetString()!;
        string modelHead = fp.GetProperty("results").EnumerateArray().First(r => r.GetProperty("entity").GetString() == "model3d").GetProperty("hash").GetString()!;

        string dsHash = await PutBlobAsync(Encoding.UTF8.GetBytes("%PDF-used"), "application/pdf");
        Guid dsUuid = Guid.NewGuid();
        JsonElement ds = await PostCommitAsync(new
        {
            message = "create used datasheet",
            author = "tester",
            changes = new object[]
            {
                new { entity = "datasheet", op = "create", uuid = dsUuid, blobHash = dsHash, directory = "Docs", filename = "Used.pdf" }
            }
        });
        int dsId = ds.GetProperty("results")[0].GetProperty("id").GetInt32();
        string dsHead = ds.GetProperty("results")[0].GetProperty("hash").GetString()!;

        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/part", new { name = "PartUsesAll", symbolId = symId, footprintId = fpId, datasheetId = dsId })).StatusCode);

        HttpResponseMessage deniedSym = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "delete used symbol",
            author = "tester",
            changes = new object[] { new { entity = "symbol", op = "delete", uuid = symUuid, expectedHash = symHead } }
        });
        string deniedSymBody = await deniedSym.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, deniedSym.StatusCode);
        Assert.Contains("PartUsesAll", deniedSymBody);
        Assert.Contains("still in use", deniedSymBody);

        HttpResponseMessage deniedFp = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "delete used footprint",
            author = "tester",
            changes = new object[] { new { entity = "footprint", op = "delete", uuid = fpUuid, expectedHash = fpHead } }
        });
        Assert.Equal(HttpStatusCode.Conflict, deniedFp.StatusCode);
        Assert.Contains("PartUsesAll", await deniedFp.Content.ReadAsStringAsync());

        HttpResponseMessage deniedDs = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "delete used datasheet",
            author = "tester",
            changes = new object[] { new { entity = "datasheet", op = "delete", uuid = dsUuid, expectedHash = dsHead } }
        });
        Assert.Equal(HttpStatusCode.Conflict, deniedDs.StatusCode);
        Assert.Contains("PartUsesAll", await deniedDs.Content.ReadAsStringAsync());

        HttpResponseMessage deniedModel = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "delete used model",
            author = "tester",
            changes = new object[] { new { entity = "model3d", op = "delete", uuid = modelUuid, expectedHash = modelHead } }
        });
        string deniedModelBody = await deniedModel.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, deniedModel.StatusCode);
        Assert.Contains("ModelOnlyFp", deniedModelBody);

        await PostCommitAsync(new
        {
            message = "delete footprint and its model together",
            author = "tester",
            changes = new object[]
            {
                new { entity = "model3d", op = "delete", uuid = modelUuid, expectedHash = modelHead },
                new { entity = "footprint", op = "delete", uuid = modelFpUuid, expectedHash = modelFpHead }
            }
        });
    }

    [Fact]
    public async Task Catalog_Part_IsVersioned_And_CanBeReverted()
    {
        if (!RequireDb()) return;

        string name = "RevertPart" + Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/part", new { name, value = "10k" });
        string createdBody = await created.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        int id = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetInt32();

        JsonElement history = await (await _client.GetAsync($"/api/part/{id}/history")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal("create", await OpOfLatestCommitChange("part", id));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutAsJsonAsync($"/api/part/{id}", new { id, name, value = "22k" })).StatusCode);
        history = await (await _client.GetAsync($"/api/part/{id}/history")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, history.GetArrayLength());
        int updateCommitId = history[1].GetProperty("commitId").GetInt32();

        JsonElement diff = await (await _client.GetAsync($"/api/part/{id}/diff?from=1&to=2")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unified", diff.GetProperty("format").GetString());
        Assert.Contains("22k", diff.GetProperty("diff").GetString());

        HttpResponseMessage stale = await _client.PostAsJsonAsync($"/api/commit/{history[0].GetProperty("commitId").GetInt32()}/revert", new { author = "tester" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        HttpResponseMessage reverted = await _client.PostAsJsonAsync($"/api/commit/{updateCommitId}/revert", new { author = "tester" });
        Assert.True(reverted.StatusCode == HttpStatusCode.Created, await reverted.Content.ReadAsStringAsync());
        JsonElement afterRevert = await (await _client.GetAsync($"/api/part/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("10k", afterRevert.GetProperty("value").GetString());

        HttpResponseMessage restored = await _client.PostAsJsonAsync($"/api/part/{id}/revert", new { author = "tester", revisionNo = 2 });
        Assert.Equal(HttpStatusCode.Created, restored.StatusCode);
        afterRevert = await (await _client.GetAsync($"/api/part/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("22k", afterRevert.GetProperty("value").GetString());

        int partNumberId;
        HttpResponseMessage partNumber = await _client.PostAsJsonAsync("/api/partnumber", new { partId = id, manufacturerPartNumber = "MPN-1", quantity = 1 });
        Assert.Equal(HttpStatusCode.Created, partNumber.StatusCode);
        partNumberId = (await partNumber.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/part/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/part/{id}")).StatusCode);

        JsonElement partNumberHistory = await (await _client.GetAsync($"/api/partnumber/{partNumberId}/history")).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement deleteRevision = partNumberHistory[partNumberHistory.GetArrayLength() - 1];
        Assert.True(deleteRevision.GetProperty("isDeleted").GetBoolean());
        int deleteCommitId = deleteRevision.GetProperty("commitId").GetInt32();

        HttpResponseMessage undeleted = await _client.PostAsJsonAsync($"/api/commit/{deleteCommitId}/revert", new { author = "tester" });
        Assert.Equal(HttpStatusCode.Created, undeleted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/part/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/partnumber/{partNumberId}")).StatusCode);
    }

    [Fact]
    public async Task Symbol_RevertCommit_RestoresPreviousContent()
    {
        if (!RequireDb()) return;

        string hash1 = await PutBlobAsync("(symbol \"RevSym\" (pin 1))\n");
        string hash2 = await PutBlobAsync("(symbol \"RevSym\" (pin 2))\n");
        JsonElement created = await PostCommitAsync(new
        {
            message = "create rev symbol",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "create", blobHash = hash1, libraryFileId = 2, name = "RevSym", reference = "U" }
            }
        });
        Guid uuid = created.GetProperty("results")[0].GetProperty("uuid").GetGuid();
        int id = created.GetProperty("results")[0].GetProperty("id").GetInt32();
        string headHash = created.GetProperty("results")[0].GetProperty("hash").GetString()!;

        await PostCommitAsync(new
        {
            message = "update rev symbol",
            author = "tester",
            changes = new object[]
            {
                new { entity = "symbol", op = "update", uuid, expectedHash = headHash, blobHash = hash2, libraryFileId = 2, name = "RevSym" }
            }
        });

        JsonElement history = await (await _client.GetAsync($"/api/symbol/{id}/history")).Content.ReadFromJsonAsync<JsonElement>();
        int updateCommitId = history[1].GetProperty("commitId").GetInt32();
        HttpResponseMessage reverted = await _client.PostAsJsonAsync($"/api/commit/{updateCommitId}/revert", new { author = "tester" });
        Assert.Equal(HttpStatusCode.Created, reverted.StatusCode);

        JsonElement detail = await (await _client.GetAsync($"/api/symbol/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(hash1, detail.GetProperty("hash").GetString());

        HttpResponseMessage again = await _client.PostAsJsonAsync($"/api/symbol/{id}/revert", new { author = "tester", revisionNo = 2 });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        detail = await (await _client.GetAsync($"/api/symbol/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(hash2, detail.GetProperty("hash").GetString());
    }

    private async Task<string> OpOfLatestCommitChange(string entity, int id)
    {
        JsonElement history = await (await _client.GetAsync($"/api/{entity}/{id}/history")).Content.ReadFromJsonAsync<JsonElement>();
        int commitId = history[history.GetArrayLength() - 1].GetProperty("commitId").GetInt32();
        JsonElement commit = await (await _client.GetAsync($"/api/commit/{commitId}")).Content.ReadFromJsonAsync<JsonElement>();
        foreach (JsonElement change in commit.GetProperty("changes").EnumerateArray())
        {
            if (change.GetProperty("entity").GetString() == entity && change.GetProperty("id").GetInt32() == id)
            {
                return change.GetProperty("op").GetString()!;
            }
        }

        throw new InvalidOperationException($"No {entity} change {id} on commit {commitId}");
    }

    private async Task<string> PutBlobAsync(string text, string mediaType = "text/x-kicad-sexpr") =>
        await PutBlobAsync(Encoding.UTF8.GetBytes(text), mediaType);

    private async Task<string> PutBlobAsync(byte[] data, string mediaType)
    {
        string hash = Sha256(data);
        using HttpRequestMessage put = new(HttpMethod.Put, $"/api/blob/{hash}");
        put.Content = new ByteArrayContent(data);
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        put.Headers.Add("X-Media-Type", mediaType);
        HttpResponseMessage resp = await _client.SendAsync(put);
        Assert.True(resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return hash;
    }

    private async Task<JsonElement> PostCommitAsync(object payload)
    {
        HttpResponseMessage resp = await _client.PostAsJsonAsync("/api/commit", payload);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<int> CountSymbolRevisionsAsync(Guid uuid)
    {
        await using NpgsqlConnection conn = new(_factory.ConnectionString);
        await conn.OpenAsync();
        await using NpgsqlCommand cmd = new(
            @"SELECT COUNT(*) FROM symbol_revision r JOIN symbol s ON s.id = r.symbol_id WHERE s.uuid = @u", conn);
        cmd.Parameters.AddWithValue("u", uuid);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountCommitsAsync()
    {
        await using NpgsqlConnection conn = new(_factory.ConnectionString);
        await conn.OpenAsync();
        return await ScalarIntAsync(conn, "SELECT COUNT(*) FROM commit");
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection conn, string sql)
    {
        await using NpgsqlCommand cmd = new(sql, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Fact]
    public async Task Part_Links_RoundTrip_And_RejectUnsafeAddress()
    {
        if (!RequireDb()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/part", new
        {
            name = "Linked" + suffix,
            value = "10k",
            links = new object[]
            {
                new { label = " Dev board ", url = "https://example.com/board" },
                new { label = "", url = "https://example.com/board" },
                new { label = "", url = "" },
                new { label = "Docs", url = "http://docs.example.com/part" }
            }
        });
        string createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        JsonElement createdPart = JsonDocument.Parse(createdBody).RootElement;
        int id = createdPart.GetProperty("id").GetInt32();
        JsonElement links = createdPart.GetProperty("links");
        Assert.Equal(2, links.GetArrayLength());
        Assert.Equal("Dev board", links[0].GetProperty("label").GetString());
        Assert.Equal("https://example.com/board", links[0].GetProperty("url").GetString());
        Assert.Equal("Docs", links[1].GetProperty("label").GetString());

        string loadedBody = await (await _client.GetAsync("/api/part/" + id)).Content.ReadAsStringAsync();
        Assert.Equal(2, JsonDocument.Parse(loadedBody).RootElement.GetProperty("links").GetArrayLength());

        HttpResponseMessage rejected = await _client.PutAsJsonAsync("/api/part/" + id, new
        {
            id,
            name = "Linked" + suffix,
            value = "10k",
            links = new object[] { new { label = "Bad", url = "javascript:alert(1)" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        loadedBody = await (await _client.GetAsync("/api/part/" + id)).Content.ReadAsStringAsync();
        Assert.Equal("https://example.com/board", JsonDocument.Parse(loadedBody).RootElement.GetProperty("links")[0].GetProperty("url").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/part/" + id)).StatusCode);
    }

    [Fact]
    public async Task Part_Datasheets_RoundTrip_And_BlockDelete()
    {
        if (!RequireDb()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        (int firstId, Guid firstUuid, string firstHash) = await CreateDatasheetAsync("A-" + suffix + ".pdf");
        (int secondId, Guid secondUuid, string secondHash) = await CreateDatasheetAsync("B-" + suffix + ".pdf");

        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/part", new
        {
            name = "Sheets" + suffix,
            value = "10k",
            datasheetIds = new[] { firstId, secondId, firstId, 0 }
        });
        string createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        JsonElement createdPart = JsonDocument.Parse(createdBody).RootElement;
        int partId = createdPart.GetProperty("id").GetInt32();
        Assert.Equal(firstId, createdPart.GetProperty("datasheetId").GetInt32());
        Assert.Equal(new[] { firstId, secondId }, Ids(createdPart, "datasheetIds"));

        string loadedBody = await (await _client.GetAsync("/api/part/" + partId)).Content.ReadAsStringAsync();
        JsonElement loaded = JsonDocument.Parse(loadedBody).RootElement;
        Assert.Equal(new[] { firstId, secondId }, Ids(loaded, "datasheetIds"));

        HttpResponseMessage legacy = await _client.PostAsJsonAsync("/api/part", new
        {
            name = "LegacySheet" + suffix,
            value = "1k",
            datasheetId = secondId
        });
        string legacyBody = await legacy.Content.ReadAsStringAsync();
        Assert.True(legacy.StatusCode == HttpStatusCode.Created, legacyBody);
        JsonElement legacyPart = JsonDocument.Parse(legacyBody).RootElement;
        Assert.Equal(new[] { secondId }, Ids(legacyPart, "datasheetIds"));

        HttpResponseMessage updated = await _client.PutAsJsonAsync("/api/part/" + partId, new
        {
            id = partId,
            name = "Sheets" + suffix,
            value = "10k",
            datasheetIds = new[] { secondId, firstId }
        });
        Assert.True(updated.StatusCode == HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync());
        loadedBody = await (await _client.GetAsync("/api/part/" + partId)).Content.ReadAsStringAsync();
        loaded = JsonDocument.Parse(loadedBody).RootElement;
        Assert.Equal(secondId, loaded.GetProperty("datasheetId").GetInt32());
        Assert.Equal(new[] { secondId, firstId }, Ids(loaded, "datasheetIds"));

        HttpResponseMessage denied = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "delete secondary datasheet",
            author = "tester",
            changes = new object[] { new { entity = "datasheet", op = "delete", uuid = firstUuid, expectedHash = firstHash } }
        });
        string deniedBody = await denied.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Contains("Sheets" + suffix, deniedBody);

        HttpResponseMessage missing = await _client.PutAsJsonAsync("/api/part/" + partId, new
        {
            id = partId,
            name = "Sheets" + suffix,
            value = "10k",
            datasheetIds = new[] { 999999 }
        });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/part/" + partId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/part/" + legacyPart.GetProperty("id").GetInt32())).StatusCode);
        HttpResponseMessage removed = await _client.PostAsJsonAsync("/api/commit", new
        {
            message = "delete unused datasheets",
            author = "tester",
            changes = new object[]
            {
                new { entity = "datasheet", op = "delete", uuid = firstUuid, expectedHash = firstHash },
                new { entity = "datasheet", op = "delete", uuid = secondUuid, expectedHash = secondHash }
            }
        });
        Assert.True(removed.StatusCode == HttpStatusCode.Created, await removed.Content.ReadAsStringAsync());
    }

    private async Task<(int Id, Guid Uuid, string Hash)> CreateDatasheetAsync(string filename)
    {
        string hash = await PutBlobAsync(Encoding.UTF8.GetBytes("%PDF-" + filename), "application/pdf");
        Guid uuid = Guid.NewGuid();
        JsonElement created = await PostCommitAsync(new
        {
            message = "create " + filename,
            author = "tester",
            changes = new object[]
            {
                new { entity = "datasheet", op = "create", uuid, blobHash = hash, directory = "Docs", filename }
            }
        });
        JsonElement result = created.GetProperty("results")[0];
        return (result.GetProperty("id").GetInt32(), uuid, result.GetProperty("hash").GetString()!);
    }

    private static int[] Ids(JsonElement part, string property) =>
        part.GetProperty(property).EnumerateArray().Select(item => item.GetInt32()).ToArray();

    [Fact]
    public async Task Part_Alternatives_RoundTrip_And_DropOwnId()
    {
        if (!RequireDb()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage other = await _client.PostAsJsonAsync("/api/part", new { name = "AltOther" + suffix, value = "1k" });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        int otherId = JsonDocument.Parse(await other.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/part", new { name = "AltMain" + suffix, value = "10k", alternatives = new[] { otherId, otherId, 0 } });
        string createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        JsonElement createdPart = JsonDocument.Parse(createdBody).RootElement;
        int id = createdPart.GetProperty("id").GetInt32();
        Assert.Equal(new[] { otherId }, createdPart.GetProperty("alternatives").EnumerateArray().Select(item => item.GetInt32()).ToArray());

        HttpResponseMessage loaded = await _client.GetAsync($"/api/part/{id}");
        string loadedBody = await loaded.Content.ReadAsStringAsync();
        Assert.True(loaded.IsSuccessStatusCode, loadedBody);
        Assert.Equal(new[] { otherId }, JsonDocument.Parse(loadedBody).RootElement.GetProperty("alternatives").EnumerateArray().Select(item => item.GetInt32()).ToArray());

        HttpResponseMessage updated = await _client.PutAsJsonAsync($"/api/part/{id}", new { id, name = "AltMain" + suffix, value = "10k", alternatives = new[] { otherId, id } });
        Assert.True(updated.StatusCode == HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync());
        loadedBody = await (await _client.GetAsync($"/api/part/{id}")).Content.ReadAsStringAsync();
        Assert.Equal(new[] { otherId }, JsonDocument.Parse(loadedBody).RootElement.GetProperty("alternatives").EnumerateArray().Select(item => item.GetInt32()).ToArray());

        HttpResponseMessage deleted = await _client.DeleteAsync($"/api/part/{id}");
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        HttpResponseMessage deletedOther = await _client.DeleteAsync($"/api/part/{otherId}");
        Assert.True(deletedOther.StatusCode == HttpStatusCode.NoContent, await deletedOther.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task KiCadPart_AlternativeFields_UseAlternativeOrSecondMpn()
    {
        if (!RequireDb()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        int primaryMaker = await CreateManufacturerAsync("Primary" + suffix);
        int secondMaker = await CreateManufacturerAsync("Second" + suffix);
        int altMaker = await CreateManufacturerAsync("AltMaker" + suffix);

        int mainId = await CreatePartAsync("Main" + suffix, "10k");
        await CreatePartNumberAsync(mainId, primaryMaker, "MPN-1");

        JsonElement alone = await GetKiCadPartAsync(mainId);
        Assert.Equal("Primary" + suffix, FieldValue(alone, "Manufacturer"));
        Assert.Equal("MPN-1", FieldValue(alone, "MPN"));
        Assert.Equal("", FieldValue(alone, "AlternativeManufacturer"));
        Assert.Equal("", FieldValue(alone, "AlternativeMPN"));

        await CreatePartNumberAsync(mainId, secondMaker, "MPN-2");
        JsonElement second = await GetKiCadPartAsync(mainId);
        Assert.Equal("MPN-1", FieldValue(second, "MPN"));
        Assert.Equal("Second" + suffix, FieldValue(second, "AlternativeManufacturer"));
        Assert.Equal("MPN-2", FieldValue(second, "AlternativeMPN"));

        int altId = await CreatePartAsync("Alt" + suffix, "10k");
        await CreatePartNumberAsync(altId, altMaker, "ALT-MPN");
        await CreatePartNumberAsync(altId, primaryMaker, "ALT-MPN-2");
        HttpResponseMessage linked = await _client.PutAsJsonAsync($"/api/part/{mainId}", new
        {
            id = mainId,
            name = "Main" + suffix,
            value = "10k",
            alternatives = new[] { altId }
        });
        Assert.True(linked.StatusCode == HttpStatusCode.NoContent, await linked.Content.ReadAsStringAsync());

        JsonElement withAlternative = await GetKiCadPartAsync(mainId);
        Assert.Equal("AltMaker" + suffix, FieldValue(withAlternative, "AlternativeManufacturer"));
        Assert.Equal("ALT-MPN", FieldValue(withAlternative, "AlternativeMPN"));
    }

    private async Task<int> CreateManufacturerAsync(string name)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/manufacturer", new { name });
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> CreatePartAsync(string name, string value)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/part", new { name, value });
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task CreatePartNumberAsync(int partId, int manufacturerId, string mpn)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/partnumber", new
        {
            partId,
            manufacturerId,
            manufacturerPartNumber = mpn,
            quantity = 1
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> GetKiCadPartAsync(int partId)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/v1/parts/{partId}.json");
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string FieldValue(JsonElement part, string field)
    {
        return part.GetProperty("fields").GetProperty(field).GetProperty("value").GetString() ?? "";
    }

    [Fact]
    public async Task KiCadPart_IncludesHiddenKiComLibUuid()
    {
        if (!RequireDb()) return;

        int partId = await CreatePartAsync("UuidPart" + Guid.NewGuid().ToString("N")[..8], "1k");
        await using NpgsqlConnection connection = new(_factory.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT uuid FROM part WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", partId);
        Guid uuid = (Guid)(await command.ExecuteScalarAsync())!;

        JsonElement part = await GetKiCadPartAsync(partId);
        JsonElement field = part.GetProperty("fields").GetProperty("KiComLibUUID");
        Assert.Equal(uuid.ToString(), field.GetProperty("value").GetString());
        Assert.Equal("False", field.GetProperty("visible").GetString());
    }

    [Fact]
    public async Task History_Author_IsTheAccountBehindTheToken()
    {
        if (!RequireDb()) return;

        string name = "HistAuth" + Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/manufacturer", new { name });
        string createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        int id = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetInt32();

        JsonElement history = await (await _client.GetAsync($"/api/manufacturer/{id}/history")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin", history[0].GetProperty("author").GetString());

        JsonElement commit = await PostCommitAsync(new
        {
            message = "spoofed author " + name,
            author = "not-the-account",
            client = "tests",
            changes = Array.Empty<object>()
        });
        int commitId = commit.GetProperty("commitId").GetInt32();
        JsonElement stored = await (await _client.GetAsync($"/api/commit/{commitId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin", stored.GetProperty("author").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/manufacturer/{id}")).StatusCode);
    }

    [Fact]
    public async Task Settings_SessionLifetime_IsStored_And_UsedForNewSessions()
    {
        if (!RequireDb()) return;

        HttpClient anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/settings")).StatusCode);

        JsonElement groups = await (await _client.GetAsync("/api/group")).Content.ReadFromJsonAsync<JsonElement>();
        int readerGroupId = groups.EnumerateArray().First(group => group.GetProperty("name").GetString() == "Read only").GetProperty("id").GetInt32();
        string readerName = "settings-reader-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/user", new { username = readerName, password = "reader-password", groupIds = new[] { readerGroupId } })).StatusCode);

        HttpClient reader = _factory.CreateClient();
        HttpResponseMessage readerLogin = await reader.PostAsJsonAsync("/api/auth/login", new { username = readerName, password = "reader-password" });
        string readerBody = await readerLogin.Content.ReadAsStringAsync();
        Assert.True(readerLogin.IsSuccessStatusCode, readerBody);
        TestSessions.UseBearer(reader, readerLogin);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/settings")).StatusCode);

        JsonElement current = await (await _client.GetAsync("/api/settings")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, current.GetProperty("sessionLifetimeDays").GetInt32());
        string versionFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "version.txt"));
        Assert.Equal(File.ReadAllText(versionFile).Trim(), current.GetProperty("version").GetString());
        Assert.Equal("v1", current.GetProperty("httpLibraryVersion").GetString());
        Assert.Equal(2, current.GetProperty("apiVersion").GetInt32());
        Assert.Equal(DatabaseInitializer.SchemaVersion, current.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync("/api/settings", new { sessionLifetimeDays = 0 })).StatusCode);

        try
        {
            HttpResponseMessage saved = await _client.PutAsJsonAsync("/api/settings", new { sessionLifetimeDays = 7 });
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            current = await (await _client.GetAsync("/api/settings")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(7, current.GetProperty("sessionLifetimeDays").GetInt32());

            HttpClient fresh = _factory.CreateClient();
            HttpResponseMessage login = await fresh.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "kicomlib" });
            Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());

            await using NpgsqlConnection conn = new(_factory.ConnectionString);
            await conn.OpenAsync();
            await using NpgsqlCommand cmd = new("SELECT EXTRACT(EPOCH FROM (expires_at - created_at)) FROM user_session ORDER BY id DESC LIMIT 1", conn);
            double seconds = Convert.ToDouble(await cmd.ExecuteScalarAsync());
            Assert.InRange(seconds, TimeSpan.FromDays(7).TotalSeconds - 5, TimeSpan.FromDays(7).TotalSeconds + 5);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync("/api/settings", new { sessionLifetimeDays = 1 })).StatusCode);
        }
    }

    [Fact]
    public async Task Settings_TrustedParts_StoresTheKeyEncrypted_AndOmitsItFromTheResponse()
    {
        if (!RequireDb()) return;

        HttpClient anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/settings/trustedparts", new { enabled = false })).StatusCode);

        JsonElement before = await (await _client.GetAsync("/api/settings")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(before.GetProperty("trustedParts").GetProperty("enabled").GetBoolean());
        Assert.False(before.GetProperty("trustedParts").GetProperty("apiKeySet").GetBoolean());

        const string apiKey = "trustedparts-test-key";
        try
        {
            HttpResponseMessage incomplete = await _client.PutAsJsonAsync("/api/settings/trustedparts", new { enabled = true, companyId = "" });
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);

            HttpResponseMessage saved = await _client.PutAsJsonAsync("/api/settings/trustedparts", new
            {
                enabled = true,
                companyId = " ACME ",
                apiKey
            });
            string savedBody = await saved.Content.ReadAsStringAsync();
            Assert.True(saved.IsSuccessStatusCode, savedBody);
            Assert.DoesNotContain(apiKey, savedBody);
            JsonElement stored = JsonDocument.Parse(savedBody).RootElement.GetProperty("trustedParts");
            Assert.True(stored.GetProperty("enabled").GetBoolean());
            Assert.Equal("ACME", stored.GetProperty("companyId").GetString());
            Assert.True(stored.GetProperty("apiKeySet").GetBoolean());

            HttpResponseMessage kept = await _client.PutAsJsonAsync("/api/settings/trustedparts", new
            {
                enabled = true,
                companyId = "ACME",
                apiKey = ""
            });
            Assert.True(kept.IsSuccessStatusCode, await kept.Content.ReadAsStringAsync());
            JsonElement keptBody = await kept.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(keptBody.GetProperty("trustedParts").GetProperty("apiKeySet").GetBoolean());

            await using NpgsqlConnection conn = new(_factory.ConnectionString);
            await conn.OpenAsync();
            await using NpgsqlCommand cmd = new("SELECT value FROM app_setting WHERE key = 'trustedparts_api_key'", conn);
            string protectedKey = Assert.IsType<string>(await cmd.ExecuteScalarAsync());
            Assert.DoesNotContain(apiKey, protectedKey);

            using IServiceScope scope = _factory.Services.CreateScope();
            TrustedPartsClient client = scope.ServiceProvider.GetRequiredService<TrustedPartsClient>();
            TrustedPartsCredentials credentials = Assert.IsType<TrustedPartsCredentials>(await client.CredentialsAsync(CancellationToken.None));
            Assert.Equal("ACME", credentials.CompanyId);
            Assert.Equal(apiKey, credentials.ApiKey);
            Assert.Equal(TrustedPartsClient.SearchEndpoint, "https://api.trustedparts.com/v2/search");
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync("/api/settings/trustedparts", new
            {
                enabled = false,
                companyId = "",
                clearApiKey = true
            })).StatusCode);
        }
    }
}

[CollectionDefinition("kicomlib-db")]
public class KicomlibDbCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}

[Collection("kicomlib-db")]
public class AccessControlTests
{
    private readonly CustomWebApplicationFactory _factory;

    public AccessControlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void DefaultAdminPassword_MatchesSeedHash()
    {
        const string stored = "pbkdf2-sha256$210000$S2lDb21MaWJEZWZhdWx0MDEh$YmQ8zYnT28KrSBN7lde4pCY4uSzWLd/93RTsEKXv4k8=";
        Assert.Equal(PasswordPolicy.DefaultHash, stored);
        Assert.True(PasswordHasher.Verify("kicomlib", stored));
        Assert.False(PasswordHasher.Verify("Kicomlib", stored));
        Assert.False(PasswordHasher.Verify("wrong", stored));
    }

    [Fact]
    public async Task Login_RequiresExactUsernameAndPasswordCase()
    {
        if (!_factory.DatabaseAvailable)
        {
            return;
        }

        HttpClient client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "kicomlib" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "Admin", password = "kicomlib" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "ADMIN", password = "kicomlib" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Kicomlib" })).StatusCode);
    }

    [Fact]
    public async Task AnonymousWrite_IsRejected_ReadOnlyToken_CannotEdit_And_MeReportsRights()
    {
        if (!_factory.DatabaseAvailable)
        {
            return;
        }

        HttpClient anonymous = _factory.CreateClient();
        HttpResponseMessage denied = await anonymous.PostAsJsonAsync("/api/manufacturer", new { name = "Nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/manufacturer")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/info")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/blob/exists", new[] { "abc" })).StatusCode);

        HttpClient admin = _factory.CreateClient();
        HttpResponseMessage login = await admin.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "kicomlib" });
        string loginBody = await login.Content.ReadAsStringAsync();
        Assert.True(login.IsSuccessStatusCode, loginBody);
        TestSessions.UseBearer(admin, login);
        using JsonDocument loginDoc = JsonDocument.Parse(loginBody);
        Assert.True(loginDoc.RootElement.GetProperty("user").GetProperty("canWrite").GetBoolean());
        Assert.True(loginDoc.RootElement.GetProperty("user").GetProperty("isAdmin").GetBoolean());
        Assert.Contains(
            loginDoc.RootElement.GetProperty("user").GetProperty("groups").EnumerateArray(),
            g => g.GetProperty("name").GetString() == "Admins" && g.GetProperty("canAdmin").GetBoolean() && g.GetProperty("canWrite").GetBoolean());

        JsonElement groups = JsonDocument.Parse(await (await admin.GetAsync("/api/group")).Content.ReadAsStringAsync()).RootElement;
        int readOnlyId = groups.EnumerateArray().First(g => g.GetProperty("name").GetString() == "Read only").GetProperty("id").GetInt32();
        string username = "reader-" + Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/user", new
        {
            username,
            password = "reader-password",
            isActive = true,
            groupIds = new[] { readOnlyId }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        HttpClient reader = _factory.CreateClient();
        HttpResponseMessage readerLogin = await reader.PostAsJsonAsync("/api/auth/login", new { username, password = "reader-password" });
        string readerBody = await readerLogin.Content.ReadAsStringAsync();
        Assert.True(readerLogin.IsSuccessStatusCode, readerBody);
        TestSessions.UseBearer(reader, readerLogin);
        using JsonDocument readerDoc = JsonDocument.Parse(readerBody);
        Assert.False(readerDoc.RootElement.GetProperty("user").GetProperty("canWrite").GetBoolean());
        Assert.True(readerDoc.RootElement.GetProperty("user").GetProperty("canRead").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/manufacturer", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/manufacturer")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/info")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/user")).StatusCode);

        HttpResponseMessage tokenResp = await reader.PostAsJsonAsync("/api/token", new { name = "KiCad" });
        string tokenBody = await tokenResp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, tokenResp.StatusCode);
        string apiToken = JsonDocument.Parse(tokenBody).RootElement.GetProperty("token").GetString()!;

        HttpClient plugin = _factory.CreateClient();
        plugin.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", apiToken);
        HttpResponseMessage me = await plugin.GetAsync("/api/auth/me");
        string meBody = await me.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using JsonDocument meDoc = JsonDocument.Parse(meBody);
        Assert.True(meDoc.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(username, meDoc.RootElement.GetProperty("username").GetString());
        Assert.False(meDoc.RootElement.GetProperty("canWrite").GetBoolean());
        Assert.Equal("api-token", meDoc.RootElement.GetProperty("authType").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await plugin.PostAsJsonAsync("/api/part", new { name = "Blocked" })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await reader.PostAsJsonAsync("/api/token", new { name = "nope", canWrite = true })).StatusCode);

        HttpResponseMessage readTokenResp = await admin.PostAsJsonAsync("/api/token", new { name = "read-only", canWrite = false });
        string readTokenBody = await readTokenResp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, readTokenResp.StatusCode);
        using JsonDocument readTokenDoc = JsonDocument.Parse(readTokenBody);
        Assert.False(readTokenDoc.RootElement.GetProperty("canWrite").GetBoolean());
        string readToken = readTokenDoc.RootElement.GetProperty("token").GetString()!;

        HttpResponseMessage writeTokenResp = await admin.PostAsJsonAsync("/api/token", new { name = "read-write", canWrite = true });
        string writeTokenBody = await writeTokenResp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, writeTokenResp.StatusCode);
        using JsonDocument writeTokenDoc = JsonDocument.Parse(writeTokenBody);
        Assert.True(writeTokenDoc.RootElement.GetProperty("canWrite").GetBoolean());
        string writeToken = writeTokenDoc.RootElement.GetProperty("token").GetString()!;

        HttpClient readPlugin = _factory.CreateClient();
        readPlugin.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", readToken);
        using JsonDocument readMe = JsonDocument.Parse(await (await readPlugin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        Assert.True(readMe.RootElement.GetProperty("canRead").GetBoolean());
        Assert.False(readMe.RootElement.GetProperty("canWrite").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await readPlugin.PostAsJsonAsync("/api/manufacturer", new { name = "BlockedByToken" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await readPlugin.GetAsync("/api/manufacturer")).StatusCode);

        HttpClient writePlugin = _factory.CreateClient();
        writePlugin.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", writeToken);
        using JsonDocument writeMe = JsonDocument.Parse(await (await writePlugin.GetAsync("/api/auth/me")).Content.ReadAsStringAsync());
        Assert.True(writeMe.RootElement.GetProperty("canWrite").GetBoolean());
        string marker = "token-write-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.Created, (await writePlugin.PostAsJsonAsync("/api/manufacturer", new { name = marker })).StatusCode);

        HttpResponseMessage anonMe = await anonymous.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, anonMe.StatusCode);
        Assert.False(JsonDocument.Parse(await anonMe.Content.ReadAsStringAsync()).RootElement.GetProperty("authenticated").GetBoolean());

        HttpClient rejected = _factory.CreateClient();
        rejected.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "kpt_not-a-real-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await rejected.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await rejected.PostAsJsonAsync("/api/manufacturer", new { name = "Nope" })).StatusCode);
    }
}

public class AccessTokenHeaderTests
{
    [Theory]
    [InlineData("Bearer secret", "secret")]
    [InlineData("bearer secret", "secret")]
    [InlineData("Token secret", "secret")]
    [InlineData("token secret", "secret")]
    [InlineData("TOKEN  secret", "secret")]
    [InlineData("kps_rawtoken", "kps_rawtoken")]
    [InlineData("Token ", null)]
    public void ExtractToken_AcceptsBearerAndKiCadTokenScheme(string header, string? expected)
    {
        DefaultHttpContext http = new();
        http.Request.Headers.Authorization = header;
        Assert.Equal(expected, AccessService.ExtractToken(http.Request));
    }
}
