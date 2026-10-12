using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace KiComLibBackend.Tests;

[Collection("kicomlib-db")]
public class ProjectAnalysisTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ProjectAnalysisTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private bool Database => _factory.DatabaseAvailable;

    [Fact]
    public async Task MatchMethods_EolIsLive_FieldOutdated_AndRematchSkipsRunner()
    {
        if (!Database) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        HttpClient admin = await AdminClientAsync();
        int maker = await CreateManufacturerAsync(admin, "Acme" + suffix);
        int uuidPart = await CreatePartAsync(admin, "Uuid" + suffix, "10k");
        int namePart = await CreatePartAsync(admin, "Name" + suffix, "22k");
        int mpnPart = await CreatePartAsync(admin, "Mpn" + suffix, "4k7");
        int amb1 = await CreatePartAsync(admin, "Amb1" + suffix, "1k");
        int amb2 = await CreatePartAsync(admin, "Amb2" + suffix, "2k");
        int oldPart = await CreatePartAsync(admin, "Old" + suffix, "NEW");
        int dnpPart = await CreatePartAsync(admin, "Dnp" + suffix, "0R");
        await CreatePartNumberAsync(admin, mpnPart, maker, "MPN-" + suffix);
        await CreatePartNumberAsync(admin, amb1, maker, "AMB-" + suffix);
        await CreatePartNumberAsync(admin, amb2, maker, "AMB-" + suffix);
        Guid uuid = await PartUuidAsync(uuidPart);
        Guid oldUuid = await PartUuidAsync(oldPart);
        Guid dnpUuid = await PartUuidAsync(dnpPart);

        string json = JsonSerializer.Serialize(new
        {
            kicad = "10.0.4",
            netlistXml = "<?xml version=\"1.0\"?><export></export>",
            warnings = Array.Empty<string>(),
            components = new object[]
            {
                Component("/uuid", "R1", "10k", null, null, uuid.ToString(), null, null, false),
                Component("/name", "R2", "22k", "KiComLib:Name" + suffix, null, null, null, null, false),
                Component("/mpn", "R3", "4k7", "Other:Nope", null, null, "MPN-" + suffix, "Acme" + suffix, false),
                Component("/amb", "R4", "1k", null, null, null, "AMB-" + suffix, null, false),
                Component("/none", "R5", "zzz", "Other:Missing", null, null, null, null, false),
                Component("/old", "R6", "OLD", null, "Lib:Old", oldUuid.ToString(), null, null, false),
                Component("/dnp", "R7", "0R", null, null, dnpUuid.ToString(), null, null, true)
            }
        });

        int projectId = await CreateProjectAsync(admin, "match-" + suffix);
        string sha = new string('a', 40);
        int revisionId = await InsertRevisionAsync(projectId, sha, json, "refs/heads/main");

        FakeRunner runner = new();
        using WebApplicationFactory<Program> host = FakeHost(runner);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        RematchJobHandler rematch = scope.ServiceProvider.GetRequiredService<RematchJobHandler>();
        await rematch.RunAsync(new ProjectJob
        {
            Kind = "rematch",
            ProjectId = projectId,
            Payload = JsonSerializer.Serialize(new { revisionId, commitSha = sha })
        }, CancellationToken.None);
        Assert.Equal(0, runner.AnalyzeCalls);

        await using NpgsqlConnection connection = await OpenAsync();
        Assert.Equal("uuid", await MatchMethodAsync(connection, revisionId, "/uuid"));
        Assert.Equal("name", await MatchMethodAsync(connection, revisionId, "/name"));
        Assert.Equal("mpn", await MatchMethodAsync(connection, revisionId, "/mpn"));
        Assert.Equal("none", await MatchMethodAsync(connection, revisionId, "/amb"));
        Assert.Equal("none", await MatchMethodAsync(connection, revisionId, "/none"));
        Assert.Equal(uuidPart, await PartIdAsync(connection, revisionId, "/uuid"));
        Assert.Equal(namePart, await PartIdAsync(connection, revisionId, "/name"));
        Assert.Equal(mpnPart, await PartIdAsync(connection, revisionId, "/mpn"));
        Assert.True(await ScalarIsNullAsync(connection, "SELECT part_id FROM public.project_component WHERE revision_id = @r AND symbol_path = '/amb'", revisionId));

        JsonElement bom = await BomAsync(admin, projectId, sha, "none");
        Assert.Equal("ok", StatusOf(bom, "R1"));
        Assert.Equal("ok", StatusOf(bom, "R2"));
        Assert.Equal("ok", StatusOf(bom, "R3"));
        Assert.Equal("ambiguous", StatusOf(bom, "R4"));
        Assert.Equal("not_in_catalog", StatusOf(bom, "R5"));
        Assert.Equal("field_outdated", StatusOf(bom, "R6"));
        Assert.Contains("Value", OutdatedOf(bom, "R6"));
        JsonElement dnp = Line(bom, "R7");
        Assert.True(dnp.GetProperty("dnp").GetBoolean());
        Assert.Equal(0, dnp.GetProperty("quantity").GetInt32());
        Assert.Equal(1, Line(bom, "R1").GetProperty("quantity").GetInt32());

        await using (NpgsqlCommand eol = new("UPDATE public.part SET lifecycle_status = 'eol' WHERE id = @id", connection))
        {
            eol.Parameters.AddWithValue("id", uuidPart);
            await eol.ExecuteNonQueryAsync();
        }

        JsonElement after = await BomAsync(admin, projectId, sha, "none");
        Assert.Equal("eol", StatusOf(after, "R1"));

        await using NpgsqlCommand head = new("SELECT head_revision_id FROM public.project WHERE id = @id", connection);
        head.Parameters.AddWithValue("id", projectId);
        Assert.Equal(revisionId, Convert.ToInt32(await head.ExecuteScalarAsync()));

        string blob = (string)(await ScalarAsync(connection, "SELECT convert_from(data, 'UTF8') FROM public.project_blob WHERE hash = (SELECT netlist_blob_hash FROM public.project_revision WHERE id = @r)", revisionId))!;
        Assert.Contains("netlistXml", blob);

        HttpResponseMessage tokenResponse = await admin.PostAsJsonAsync("/api/token", new { name = "ro-" + suffix, canWrite = false });
        string tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(tokenResponse.StatusCode == HttpStatusCode.Created, tokenBody);
        HttpClient readOnly = _factory.CreateClient();
        readOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JsonDocument.Parse(tokenBody).RootElement.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync("/api/project/" + projectId + "/revision/" + sha + "/analyze", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync("/api/project/" + projectId + "/revision/" + sha + "/rematch", new { })).StatusCode);
    }

    [Fact]
    public async Task BomDiff_IncludesRenamedAndReplaced()
    {
        if (!Database) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        HttpClient admin = await AdminClientAsync();
        int projectId = await CreateProjectAsync(admin, "diff-" + suffix);
        string baseSha = new string('b', 40);
        string targetSha = new string('c', 40);
        int baseId = await InsertRevisionAsync(projectId, baseSha, null, "refs/heads/main");
        int targetId = await InsertRevisionAsync(projectId, targetSha, null, "refs/heads/main");
        await InsertComponentAsync(baseId, "/s1", "R1", "10k", "FP");
        await InsertComponentAsync(baseId, "/s2", "R2", "10k", "FP");
        await InsertComponentAsync(baseId, "/s3", "P5", "MOUNTING_HOLE", "Hole");
        await InsertComponentAsync(baseId, "/s4", "C2", "680nF", "OLD");
        await InsertComponentAsync(targetId, "/s1", "R1", "10k", "FP");
        await InsertComponentAsync(targetId, "/s2", "R20", "10k", "FP");
        await InsertComponentAsync(targetId, "/s5", "P5", "CONN_1", "Conn");
        await InsertComponentAsync(targetId, "/s4", "C2", "680nF", "NEW");

        HttpResponseMessage response = await admin.GetAsync("/api/project/" + projectId + "/diff?base=" + baseSha + "&target=" + targetSha);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        JsonElement diff = JsonDocument.Parse(body).RootElement;
        JsonElement summary = diff.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("added").GetInt32());
        Assert.Equal(0, summary.GetProperty("removed").GetInt32());
        Assert.Equal(1, summary.GetProperty("replaced").GetInt32());
        Assert.Equal(1, summary.GetProperty("changed").GetInt32());
        Assert.Equal(1, summary.GetProperty("renamed").GetInt32());
        Assert.Equal("P5", diff.GetProperty("replaced")[0].GetProperty("reference").GetString());
        Assert.Equal("MOUNTING_HOLE", diff.GetProperty("replaced")[0].GetProperty("from").GetProperty("value").GetString());
        Assert.Equal("CONN_1", diff.GetProperty("replaced")[0].GetProperty("to").GetProperty("value").GetString());
        Assert.Equal("R20", diff.GetProperty("renamed")[0].GetProperty("reference").GetString());
        JsonElement footprint = diff.GetProperty("changed")[0].GetProperty("changes").GetProperty("footprint");
        Assert.Equal("OLD", footprint[0].GetString());
        Assert.Equal("NEW", footprint[1].GetString());
    }

    [Fact]
    public async Task Usage_HidesProjectsTheCallerCannotRead()
    {
        if (!Database) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        HttpClient admin = await AdminClientAsync();
        int readers = await CreateGroupAsync(admin, "usage-" + suffix, false);
        string userName = "usage-" + suffix;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/user", new { username = userName, password = "usage-password", groupIds = new[] { readers } })).StatusCode);
        int partId = await CreatePartAsync(admin, "Used" + suffix, "10k");
        int visible = await CreateProjectAsync(admin, "visible-" + suffix);
        int hidden = await CreateProjectAsync(admin, "hidden-" + suffix);
        Assert.True((await admin.PutAsJsonAsync("/api/project/" + visible + "/access", new object[] { new { groupId = readers, canWrite = false } })).IsSuccessStatusCode);
        string visibleSha = new string('d', 40);
        string hiddenSha = new string('e', 40);
        int visibleRevision = await InsertRevisionAsync(visible, visibleSha, null, "refs/heads/main");
        int hiddenRevision = await InsertRevisionAsync(hidden, hiddenSha, null, "refs/heads/main");
        await InsertComponentAsync(visibleRevision, "/u", "R1", "10k", null, partId, "uuid");
        await InsertComponentAsync(visibleRevision, "/u2", "R2", "10k", null, partId, "uuid");
        await InsertComponentAsync(hiddenRevision, "/u", "R9", "10k", null, partId, "uuid");
        await SetHeadAsync(visible, visibleRevision);
        await SetHeadAsync(hidden, hiddenRevision);

        HttpClient user = await LoginAsync(userName, "usage-password");
        HttpResponseMessage response = await user.GetAsync("/api/project/usage/part/" + partId);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("hidden-" + suffix, body);
        JsonElement usage = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, usage.GetArrayLength());
        Assert.Equal("visible-" + suffix, usage[0].GetProperty("projectName").GetString());
        Assert.Equal(2, usage[0].GetProperty("quantity").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/project/usage/part/" + partId)).StatusCode);
    }

    [Fact]
    public async Task Analyze_UnsupportedVersion_AndSuccessfulMatch()
    {
        if (!Database || !GitOnPath()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), "kicomlib-an-" + suffix);
        try
        {
            string work = Path.Combine(root, "work");
            string bare = Path.Combine(root, "origin.git");
            Directory.CreateDirectory(work);
            Git("init -b main", work);
            WriteProject(work, "v1");
            Commit(work, "A");
            Git("init --bare \"" + bare + "\"");
            Git("remote add origin \"" + bare + "\"", work);
            Git("push -u origin HEAD", work);

            HttpClient admin = await AdminClientAsync();
            int repositoryId = await CreateRepositoryAsync(admin, ToFileUrl(bare));
            await RunFetchAsync(repositoryId);
            HttpResponseMessage created = await admin.PostAsJsonAsync("/api/project", new
            {
                repositoryId,
                projectFile = "hw/main/main.kicad_pro",
                name = "Analyze " + suffix,
                status = "development"
            });
            string createdBody = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
            int projectId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetInt32();
            await RunFetchAsync(repositoryId);

            int partId = await CreatePartAsync(admin, "AnPart" + suffix, "10k");
            Guid partUuid = await PartUuidAsync(partId);
            (int revisionId, string sha) = await LatestRevisionAsync(projectId);

            FakeRunner runner = new() { FailCode = "unsupported_version" };
            using WebApplicationFactory<Program> host = FakeHost(runner);
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            AnalyzeJobHandler analyze = scope.ServiceProvider.GetRequiredService<AnalyzeJobHandler>();
            await analyze.RunAsync(new ProjectJob
            {
                Kind = "analyze",
                ProjectId = projectId,
                Payload = JsonSerializer.Serialize(new { revisionId, commitSha = sha })
            }, CancellationToken.None);
            Assert.Equal(1, runner.AnalyzeCalls);
            Assert.Equal("unsupported", await AnalysisStatusAsync(revisionId));

            runner.FailCode = null;
            runner.AnalyzeJson = JsonSerializer.Serialize(new
            {
                kicad = "10.0.4",
                files = new { generatorVersion = "10.0.4" },
                netlistXml = "<?xml version=\"1.0\"?><export></export>",
                components = new object[]
                {
                    Component("/u1", "U1", "10k", null, null, partUuid.ToString(), null, null, false)
                }
            });
            await analyze.RunAsync(new ProjectJob
            {
                Kind = "analyze",
                ProjectId = projectId,
                Payload = JsonSerializer.Serialize(new { revisionId, commitSha = sha })
            }, CancellationToken.None);
            Assert.Equal("done", await AnalysisStatusAsync(revisionId));
            await using NpgsqlConnection connection = await OpenAsync();
            Assert.Equal("uuid", await MatchMethodAsync(connection, revisionId, "/u1"));
            await using NpgsqlCommand head = new("SELECT head_revision_id FROM public.project WHERE id = @id", connection);
            head.Parameters.AddWithValue("id", projectId);
            Assert.Equal(revisionId, Convert.ToInt32(await head.ExecuteScalarAsync()));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task VisualDiff_CacheRoundTrip_Rebuild_AndPermissions()
    {
        if (!Database || !GitOnPath()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), "kicomlib-vd-" + suffix);
        string cache = Path.Combine(root, "cache");
        string? previousCache = Environment.GetEnvironmentVariable("PROJECT_CACHE_DIR");
        string? previousMax = Environment.GetEnvironmentVariable("PROJECT_CACHE_MAX_MB");
        Environment.SetEnvironmentVariable("PROJECT_CACHE_DIR", cache);
        Environment.SetEnvironmentVariable("PROJECT_CACHE_MAX_MB", "5120");
        try
        {
            string work = Path.Combine(root, "work");
            string bare = Path.Combine(root, "origin.git");
            Directory.CreateDirectory(work);
            Git("init -b main", work);
            WriteProject(work, "v1");
            Commit(work, "A");
            Git("init --bare \"" + bare + "\"");
            Git("remote add origin \"" + bare + "\"", work);
            Git("push -u origin HEAD", work);

            HttpClient admin = await AdminClientAsync();
            int repositoryId = await CreateRepositoryAsync(admin, ToFileUrl(bare));
            await RunFetchAsync(repositoryId);
            HttpResponseMessage created = await admin.PostAsJsonAsync("/api/project", new
            {
                repositoryId,
                projectFile = "hw/main/main.kicad_pro",
                name = "Diff " + suffix,
                status = "development"
            });
            string createdBody = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
            int projectId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetInt32();
            await RunFetchAsync(repositoryId);
            WriteProject(work, "v2");
            Commit(work, "B");
            Git("push origin HEAD", work);
            await RunFetchAsync(repositoryId);
            List<(int Id, string Sha)> revisions = await RevisionsAsync(projectId);
            Assert.True(revisions.Count >= 2);
            string baseSha = revisions[0].Sha;
            string targetSha = revisions[1].Sha;

            string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>";
            string manifest = JsonSerializer.Serialize(new
            {
                version = 1,
                kicad = "10.0.4",
                summary = new { parts = new { added = 1 } },
                pages = new object[]
                {
                    new
                    {
                        id = "sch:1",
                        page = "1",
                        @base = "sch/p1.svg",
                        layers = new object[]
                        {
                            new { status = "added", category = "parts", file = "sch/p1/added-parts.svg", aligned = true }
                        }
                    }
                }
            });
            FakeRunner runner = new()
            {
                VisualZip = Zip(
                    ("manifest.json", manifest),
                    ("sch/p1.svg", svg),
                    ("sch/p1/added-parts.svg", svg)),
                RenderZip = Zip(
                    ("manifest.json", "{\"pages\":[{\"page\":\"1\",\"file\":\"sch/p1.svg\"}]}"),
                    ("sch/p1.svg", svg))
            };
            using WebApplicationFactory<Program> host = FakeHost(runner);
            HttpClient client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

            HttpResponseMessage posted = await client.PostAsJsonAsync("/api/project/" + projectId + "/visual-diff", new { @base = baseSha, target = targetSha });
            string postedBody = await posted.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, posted.StatusCode);
            int diffId = JsonDocument.Parse(postedBody).RootElement.GetProperty("id").GetInt32();

            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                VisualDiffJobHandler handler = scope.ServiceProvider.GetRequiredService<VisualDiffJobHandler>();
                await handler.RunAsync(new ProjectJob
                {
                    Kind = "visual_diff",
                    ProjectId = projectId,
                    DedupKey = "visual_diff:" + diffId,
                    Payload = JsonSerializer.Serialize(new { diffId })
                }, CancellationToken.None);
            }

            HttpResponseMessage status = await client.GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId);
            string statusBody = await status.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            JsonElement envelope = JsonDocument.Parse(statusBody).RootElement;
            Assert.Equal("done", envelope.GetProperty("status").GetString());
            Assert.Equal("sch/p1/added-parts.svg", envelope.GetProperty("manifest").GetProperty("pages")[0].GetProperty("layers")[0].GetProperty("file").GetString());

            HttpResponseMessage file = await client.GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId + "/file/sch/p1/added-parts.svg");
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Contains("<svg", await file.Content.ReadAsStringAsync());
            HttpStatusCode traversal = (await client.GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId + "/file/sch/%2e%2e/%2e%2e/secret.svg")).StatusCode;
            Assert.True(traversal is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, traversal.ToString());

            string stored = Path.Combine(cache, "diff");
            string svgPath = Directory.GetFiles(stored, "added-parts.svg", SearchOption.AllDirectories).Single();
            File.Delete(svgPath);
            HttpResponseMessage missing = await client.GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId + "/file/sch/p1/added-parts.svg");
            Assert.Equal(HttpStatusCode.Accepted, missing.StatusCode);

            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                VisualDiffJobHandler handler = scope.ServiceProvider.GetRequiredService<VisualDiffJobHandler>();
                await handler.RunAsync(new ProjectJob
                {
                    Kind = "visual_diff",
                    ProjectId = projectId,
                    Payload = JsonSerializer.Serialize(new { diffId })
                }, CancellationToken.None);
            }

            HttpResponseMessage restored = await client.GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId + "/file/sch/p1/added-parts.svg");
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

            HttpResponseMessage pageMissing = await client.GetAsync("/api/project/" + projectId + "/revision/" + targetSha + "/page/1.svg");
            Assert.Equal(HttpStatusCode.Accepted, pageMissing.StatusCode);
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                RenderJobHandler render = scope.ServiceProvider.GetRequiredService<RenderJobHandler>();
                (int revisionId, string sha) = revisions[1];
                await render.RunAsync(new ProjectJob
                {
                    Kind = "render",
                    ProjectId = projectId,
                    Payload = JsonSerializer.Serialize(new { revisionId, commitSha = sha })
                }, CancellationToken.None);
            }

            HttpResponseMessage page = await client.GetAsync("/api/project/" + projectId + "/revision/" + targetSha + "/page/1.svg");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("<svg", await page.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.CreateClient().GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId + "/file/sch/p1/added-parts.svg")).StatusCode);
            int outsiders = await CreateGroupAsync(admin, "out-" + suffix, false);
            string outsiderName = "out-" + suffix;
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/user", new { username = outsiderName, password = "outsider-password", groupIds = new[] { outsiders } })).StatusCode);
            HttpClient outsider = await LoginAsync(outsiderName, "outsider-password");
            HttpResponseMessage forbidden = await outsider.GetAsync("/api/project/" + projectId + "/visual-diff/" + diffId + "/file/sch/p1/added-parts.svg");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_CACHE_DIR", previousCache);
            Environment.SetEnvironmentVariable("PROJECT_CACHE_MAX_MB", previousMax);
            TryDelete(root);
        }
    }

    [Fact]
    public void CacheEviction_KeepsLastUsedDiff_AndRespectsLimit()
    {
        if (!Database) return;

        string root = Path.Combine(Path.GetTempPath(), "kicomlib-evict-" + Guid.NewGuid().ToString("N"));
        string? previousCache = Environment.GetEnvironmentVariable("PROJECT_CACHE_DIR");
        string? previousMax = Environment.GetEnvironmentVariable("PROJECT_CACHE_MAX_MB");
        Environment.SetEnvironmentVariable("PROJECT_CACHE_DIR", root);
        Environment.SetEnvironmentVariable("PROJECT_CACHE_MAX_MB", "5120");
        try
        {
            ProjectCache cache = _factory.Services.GetRequiredService<ProjectCache>();
            Guid older = Guid.NewGuid();
            Guid newer = Guid.NewGuid();
            cache.Unpack(cache.DiffRelative(older), Zip(("a.svg", new string('x', 2 * 1024 * 1024))), protect: false);
            cache.Unpack(cache.DiffRelative(newer), Zip(("b.svg", "<svg/>")), protect: false);
            cache.NoteAccess(cache.DiffRelative(older), DateTime.UtcNow.AddHours(-2));
            cache.NoteAccess(cache.DiffRelative(newer), DateTime.UtcNow);
            Environment.SetEnvironmentVariable("PROJECT_CACHE_MAX_MB", "1");
            cache.Evict(null);
            Assert.False(Directory.Exists(Path.Combine(cache.Root, cache.DiffRelative(older).Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(Directory.Exists(Path.Combine(cache.Root, cache.DiffRelative(newer).Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(cache.TotalBytes() <= 1024L * 1024L);

            Guid fresh = Guid.NewGuid();
            cache.Unpack(cache.DiffRelative(fresh), Zip(("c.svg", new string('y', 2 * 1024 * 1024))), protect: true);
            Assert.True(Directory.Exists(Path.Combine(cache.Root, cache.DiffRelative(fresh).Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_CACHE_DIR", previousCache);
            Environment.SetEnvironmentVariable("PROJECT_CACHE_MAX_MB", previousMax);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Release_StoresArtifact_Download_Withdrawn_AndPermissions()
    {
        if (!Database || !GitOnPath()) return;

        string suffix = Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), "kicomlib-rel-" + suffix);
        try
        {
            string work = Path.Combine(root, "work");
            string bare = Path.Combine(root, "origin.git");
            Directory.CreateDirectory(work);
            Git("init -b main", work);
            WriteProject(work, "v1");
            Commit(work, "A");
            Git("init --bare \"" + bare + "\"");
            Git("remote add origin \"" + bare + "\"", work);
            Git("push -u origin HEAD", work);

            HttpClient admin = await AdminClientAsync();
            int repositoryId = await CreateRepositoryAsync(admin, ToFileUrl(bare));
            await RunFetchAsync(repositoryId);
            HttpResponseMessage created = await admin.PostAsJsonAsync("/api/project", new
            {
                repositoryId,
                projectFile = "hw/main/main.kicad_pro",
                name = "Release " + suffix,
                status = "development"
            });
            string createdBody = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
            int projectId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetInt32();
            await RunFetchAsync(repositoryId);
            Git("tag v1", work);
            Git("push origin v1", work);
            await RunFetchAsync(repositoryId);
            (int revisionId, string sha) = await LatestRevisionAsync(projectId);

            HttpResponseMessage posted = await admin.PostAsJsonAsync("/api/project/" + projectId + "/release", new
            {
                tag = "v1",
                name = "Rev " + suffix,
                notes = "first"
            });
            string postedBody = await posted.Content.ReadAsStringAsync();
            Assert.True(posted.StatusCode == HttpStatusCode.Created, postedBody);
            JsonElement release = JsonDocument.Parse(postedBody).RootElement;
            Assert.Equal(sha, release.GetProperty("sha").GetString());
            Assert.Equal("v1", release.GetProperty("tag").GetString());
            int releaseId = release.GetProperty("id").GetInt32();

            string pdf = "SCHEMATIC-PDF";
            string erc = JsonSerializer.Serialize(new
            {
                violations = new object[]
                {
                    new { severity = "error", description = "pin" },
                    new { severity = "warning", description = "clearance" }
                }
            });
            string manifest = JsonSerializer.Serialize(new
            {
                kicad = "10.0.6",
                outputs = new object[]
                {
                    new { kind = "schematic_pdf", fileName = "schematic.pdf" },
                    new { kind = "erc_json", fileName = "erc.json" }
                }
            });
            FakeRunner runner = new()
            {
                OutputsZip = Zip(
                    ("manifest.json", manifest),
                    ("schematic.pdf", pdf),
                    ("erc.json", erc))
            };
            using WebApplicationFactory<Program> host = FakeHost(runner);
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                OutputsJobHandler handler = scope.ServiceProvider.GetRequiredService<OutputsJobHandler>();
                await handler.RunAsync(new ProjectJob
                {
                    Kind = "outputs",
                    ProjectId = projectId,
                    DedupKey = ProjectJobQueue.OutputsKey(releaseId),
                    Payload = JsonSerializer.Serialize(new { releaseId, revisionId, commitSha = sha })
                }, CancellationToken.None);
            }

            Assert.Equal(1, runner.OutputsCalls);
            HttpResponseMessage listed = await admin.GetAsync("/api/project/" + projectId + "/release");
            string listedBody = await listed.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            JsonElement row = JsonDocument.Parse(listedBody).RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == releaseId);
            Assert.Equal(1, row.GetProperty("erc").GetProperty("errors").GetInt32());
            Assert.Equal(1, row.GetProperty("erc").GetProperty("warnings").GetInt32());
            JsonElement artifact = row.GetProperty("artifacts").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "schematic_pdf");
            int artifactId = artifact.GetProperty("id").GetInt32();

            HttpResponseMessage download = await admin.GetAsync("/api/project/" + projectId + "/artifact/" + artifactId);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(pdf, await download.Content.ReadAsStringAsync());

            HttpResponseMessage withdrawn = await admin.PutAsJsonAsync("/api/project/" + projectId + "/release/" + releaseId, new { state = "withdrawn" });
            string withdrawnBody = await withdrawn.Content.ReadAsStringAsync();
            Assert.True(withdrawn.StatusCode == HttpStatusCode.OK, withdrawnBody);
            Assert.Equal("withdrawn", JsonDocument.Parse(withdrawnBody).RootElement.GetProperty("state").GetString());
            HttpResponseMessage stillThere = await admin.GetAsync("/api/project/" + projectId + "/artifact/" + artifactId);
            Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
            Assert.Equal(pdf, await stillThere.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/project/" + projectId + "/release")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/project/" + projectId + "/artifact/" + artifactId)).StatusCode);
            int outsiders = await CreateGroupAsync(admin, "rel-out-" + suffix, false);
            string outsiderName = "rel-out-" + suffix;
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/user", new { username = outsiderName, password = "outsider-password", groupIds = new[] { outsiders } })).StatusCode);
            HttpClient outsider = await LoginAsync(outsiderName, "outsider-password");
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/project/" + projectId + "/artifact/" + artifactId)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/project/" + projectId + "/release")).StatusCode);

            await using NpgsqlConnection connection = await OpenAsync();
            await using NpgsqlCommand move = new(
                "UPDATE public.git_ref SET sha = @sha WHERE repository_id = @repo AND name = 'refs/tags/v1'",
                connection);
            move.Parameters.AddWithValue("sha", new string('b', 40));
            move.Parameters.AddWithValue("repo", repositoryId);
            Assert.Equal(1, await move.ExecuteNonQueryAsync());
            HttpResponseMessage afterMove = await admin.GetAsync("/api/project/" + projectId + "/release");
            JsonElement kept = JsonDocument.Parse(await afterMove.Content.ReadAsStringAsync()).RootElement
                .EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == releaseId);
            Assert.Equal(sha, kept.GetProperty("sha").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private WebApplicationFactory<Program> FakeHost(FakeRunner runner)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IKiCadRunnerClient>();
                services.AddSingleton<IKiCadRunnerClient>(runner);
            });
        });
    }

    private async Task<int> CreateProjectAsync(HttpClient admin, string name)
    {
        int repositoryId = await CreateRepositoryAsync(admin, "https://git.local.test/group/" + name + ".git");
        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/project", new
        {
            repositoryId,
            projectFile = "hw/main/main.kicad_pro",
            name,
            status = "development"
        });
        string body = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateRepositoryAsync(HttpClient admin, string cloneUrl)
    {
        string name = "host-" + Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage host = await admin.PostAsJsonAsync("/api/git-host", new
        {
            kind = "generic",
            name,
            webUrl = "https://git.local.test",
            sshHost = "git@git.local.test",
            allowHttp = false
        });
        string hostBody = await host.Content.ReadAsStringAsync();
        Assert.True(host.StatusCode == HttpStatusCode.Created, hostBody);
        int hostId = JsonDocument.Parse(hostBody).RootElement.GetProperty("id").GetInt32();
        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/git-repository", new
        {
            hostId,
            cloneUrl,
            syncMode = "poll"
        });
        string body = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> InsertRevisionAsync(int projectId, string sha, string? json, string gitRef)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Project project = await db.Projects.FirstAsync(p => p.Id == projectId);
        GitRepository repository = await db.GitRepositories.FirstAsync(r => r.Id == project.RepositoryId);
        repository.DefaultBranch = "main";
        ProjectRevision revision = new()
        {
            ProjectId = projectId,
            CommitSha = sha,
            ParentShas = Array.Empty<string>(),
            CommittedAt = DateTime.UtcNow,
            AuthorName = "Test",
            Message = "revision",
            Refs = new[] { gitRef },
            Reachable = true,
            AnalysisStatus = json == null ? "done" : "pending",
            CreatedAt = DateTime.UtcNow
        };
        db.ProjectRevisions.Add(revision);
        await db.SaveChangesAsync();
        if (json != null)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            string hash = ProjectExport.Sha256Hex(data);
            if (!await db.ProjectBlobs.AnyAsync(b => b.Hash == hash))
            {
                db.ProjectBlobs.Add(new ProjectBlob
                {
                    Hash = hash,
                    Size = data.LongLength,
                    MediaType = "application/json",
                    Data = data,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            revision.NetlistBlobHash = hash;
            await db.SaveChangesAsync();
        }

        return revision.Id;
    }

    private async Task InsertComponentAsync(int revisionId, string path, string reference, string value, string? footprint, int? partId = null, string method = "none")
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ProjectComponents.Add(new ProjectComponent
        {
            RevisionId = revisionId,
            Variant = "",
            SymbolPath = path,
            SymbolUuids = Array.Empty<Guid>(),
            SheetNames = "/",
            Reference = reference,
            Value = value,
            Footprint = footprint,
            Fields = "{}",
            PartId = partId,
            MatchMethod = method
        });
        await db.SaveChangesAsync();
    }

    private async Task SetHeadAsync(int projectId, int revisionId)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = new("UPDATE public.project SET head_revision_id = @revision WHERE id = @id", connection);
        command.Parameters.AddWithValue("revision", revisionId);
        command.Parameters.AddWithValue("id", projectId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RunFetchAsync(int repositoryId)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        FetchJobHandler handler = scope.ServiceProvider.GetRequiredService<FetchJobHandler>();
        await handler.RunAsync(new ProjectJob { Kind = "fetch", RepositoryId = repositoryId, Payload = "{}" }, CancellationToken.None);
    }

    private async Task<(int Id, string Sha)> LatestRevisionAsync(int projectId)
    {
        List<(int Id, string Sha)> rows = await RevisionsAsync(projectId);
        Assert.NotEmpty(rows);
        return rows[^1];
    }

    private async Task<List<(int Id, string Sha)>> RevisionsAsync(int projectId)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = new(
            "SELECT id, commit_sha FROM public.project_revision WHERE project_id = @id ORDER BY committed_at, id",
            connection);
        command.Parameters.AddWithValue("id", projectId);
        List<(int, string)> rows = new();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    private async Task<string> AnalysisStatusAsync(int revisionId)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        object? value = await ScalarAsync(connection, "SELECT analysis_status FROM public.project_revision WHERE id = @r", revisionId);
        return (string)value!;
    }

    private static async Task<string> MatchMethodAsync(NpgsqlConnection connection, int revisionId, string path)
    {
        await using NpgsqlCommand command = new(
            "SELECT match_method FROM public.project_component WHERE revision_id = @r AND symbol_path = @p",
            connection);
        command.Parameters.AddWithValue("r", revisionId);
        command.Parameters.AddWithValue("p", path);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> PartIdAsync(NpgsqlConnection connection, int revisionId, string path)
    {
        await using NpgsqlCommand command = new(
            "SELECT part_id FROM public.project_component WHERE revision_id = @r AND symbol_path = @p",
            connection);
        command.Parameters.AddWithValue("r", revisionId);
        command.Parameters.AddWithValue("p", path);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> ScalarIsNullAsync(NpgsqlConnection connection, string sql, int revisionId)
    {
        object? value = await ScalarAsync(connection, sql, revisionId);
        return value == null || value is DBNull;
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, int revisionId)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("r", revisionId);
        return await command.ExecuteScalarAsync();
    }

    private async Task<JsonElement> BomAsync(HttpClient admin, int projectId, string sha, string group)
    {
        HttpResponseMessage response = await admin.GetAsync("/api/project/" + projectId + "/revision/" + sha + "/bom?group=" + group);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static JsonElement Line(JsonElement bom, string reference)
    {
        foreach (JsonElement line in bom.EnumerateArray())
        {
            if (line.GetProperty("reference").GetString() == reference)
            {
                return line;
            }
        }

        throw new InvalidOperationException("BOM line " + reference + " was not found.");
    }

    private static string StatusOf(JsonElement bom, string reference) => Line(bom, reference).GetProperty("status").GetString()!;

    private static List<string> OutdatedOf(JsonElement bom, string reference)
    {
        return Line(bom, reference).GetProperty("outdatedFields").EnumerateArray().Select(item => item.GetString() ?? "").ToList();
    }

    private static object Component(
        string symbolPath,
        string reference,
        string value,
        string? libId,
        string? footprint,
        string? kicomlibUuid,
        string? mpn,
        string? manufacturer,
        bool dnp)
    {
        Dictionary<string, string> fields = new();
        if (!string.IsNullOrWhiteSpace(kicomlibUuid))
        {
            fields["KiComLibUUID"] = kicomlibUuid;
        }

        if (!string.IsNullOrWhiteSpace(mpn))
        {
            fields["MPN"] = mpn;
        }

        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            fields["Manufacturer"] = manufacturer;
        }

        return new
        {
            symbolPath,
            symbolUuids = new[] { Guid.NewGuid().ToString() },
            sheetNames = "/",
            reference,
            value,
            footprint,
            libId,
            fields,
            dnp,
            excludeFromBom = false,
            excludeFromBoard = false
        };
    }

    private async Task<HttpClient> AdminClientAsync() => await LoginAsync("admin", "kicomlib");

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        string body = await login.Content.ReadAsStringAsync();
        Assert.True(login.IsSuccessStatusCode, body);
        TestSessions.UseBearer(client, login);
        return client;
    }

    private async Task<int> CreateGroupAsync(HttpClient admin, string name, bool canWrite)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/group", new
        {
            name,
            description = name,
            canRead = true,
            canWrite
        });
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateManufacturerAsync(HttpClient admin, string name)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/manufacturer", new { name });
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> CreatePartAsync(HttpClient admin, string name, string value)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/part", new { name, value });
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    private async Task CreatePartNumberAsync(HttpClient admin, int partId, int manufacturerId, string mpn)
    {
        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/partnumber", new
        {
            partId,
            manufacturerId,
            manufacturerPartNumber = mpn,
            quantity = 1
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> PartUuidAsync(int partId)
    {
        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = new("SELECT uuid FROM public.part WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", partId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(_factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using StreamWriter writer = new(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static bool GitOnPath()
    {
        try
        {
            ProcessStartInfo start = new("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using Process? process = Process.Start(start);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteProject(string work, string marker)
    {
        string directory = Path.Combine(work, "hw", "main");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "main.kicad_pro"), "(kicad_pro (version 20250114) (marker \"" + marker + "\"))\n");
    }

    private static void Commit(string work, string message)
    {
        Git("add -A", work);
        Git("-c user.email=test@example.com -c user.name=Test commit -m \"" + message + "\"", work);
    }

    private static void Git(string arguments, string? workingDirectory = null)
    {
        ProcessStartInfo start = new("git", arguments)
        {
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using Process? process = Process.Start(start);
        Assert.NotNull(process);
        string stdout = process!.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);
        Assert.True(process.ExitCode == 0, arguments + "\n" + stdout + "\n" + stderr);
    }

    private static string ToFileUrl(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        if (!full.StartsWith('/'))
        {
            full = "/" + full;
        }

        return "file://" + full;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception)
        {
            // Temporary directories are removed on a best-effort basis.
        }
    }

    private sealed class FakeRunner : IKiCadRunnerClient
    {
        public int AnalyzeCalls { get; private set; }

        public string? FailCode { get; set; }

        public string AnalyzeJson { get; set; } = "{\"components\":[]}";

        public byte[] RenderZip { get; set; } = Array.Empty<byte>();

        public byte[] VisualZip { get; set; } = Array.Empty<byte>();

        public byte[] OutputsZip { get; set; } = Array.Empty<byte>();

        public int OutputsCalls { get; private set; }

        public Task<string> AnalyzeAsync(byte[] projectTar, string projectFile, string? variant, CancellationToken cancellationToken)
        {
            AnalyzeCalls++;
            if (FailCode != null)
            {
                string message = FailCode == "unsupported_version"
                    ? "File version is newer than this runner."
                    : "Missing sheet files.";
                throw new RunnerCallException(FailCode, 422, message, retryable: false);
            }

            return Task.FromResult(AnalyzeJson);
        }

        public Task<byte[]> RenderAsync(byte[] projectTar, string projectFile, CancellationToken cancellationToken)
        {
            return Task.FromResult(RenderZip);
        }

        public Task<byte[]> VisualDiffAsync(byte[] baseTar, byte[] targetTar, string projectFile, CancellationToken cancellationToken)
        {
            return Task.FromResult(VisualZip);
        }

        public Task<byte[]> OutputsAsync(
            byte[] projectTar,
            string projectFile,
            IReadOnlyList<string> outputs,
            string? variant,
            IReadOnlyList<OutputBomLine>? bom,
            byte[]? modelsZip,
            CancellationToken cancellationToken)
        {
            OutputsCalls++;
            return Task.FromResult(OutputsZip);
        }
    }
}
