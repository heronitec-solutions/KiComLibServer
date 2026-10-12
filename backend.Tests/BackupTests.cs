using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KiComLibBackend.Tests;

[Collection("kicomlib-db")]
public class BackupTests
{
    private readonly CustomWebApplicationFactory _factory;

    public BackupTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Admin_CanCreateImportDownloadRestoreAndDeleteBackup()
    {
        if (!_factory.DatabaseAvailable)
        {
            return;
        }

        HttpClient anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/backup")).StatusCode);

        const string passphrase = "test-backup-pass";
        string keysDirectory = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_DIR")!;
        string markerPath = Path.Combine(keysDirectory, "marker-key.txt");
        const string marker = "kicomlib-marker-value-v1";
        await File.WriteAllTextAsync(markerPath, marker);

        HttpClient admin = await LoginAsync();
        HttpResponseMessage missingPassphrase = await admin.PostAsJsonAsync("/api/backup", new { name = "Test backup" });
        Assert.Equal(HttpStatusCode.BadRequest, missingPassphrase.StatusCode);

        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/backup", new { name = "Test backup", passphrase });
        string createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        using JsonDocument createdDoc = JsonDocument.Parse(createdBody);
        string id = createdDoc.RootElement.GetProperty("id").GetString()!;
        Assert.EndsWith(".kibkp", createdDoc.RootElement.GetProperty("fileName").GetString());
        Assert.True(createdDoc.RootElement.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.True(createdDoc.RootElement.GetProperty("encrypted").GetBoolean());

        HttpResponseMessage list = await admin.GetAsync("/api/backup");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using JsonDocument listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(listDoc.RootElement.EnumerateArray(), item => item.GetProperty("id").GetString() == id && item.GetProperty("encrypted").GetBoolean());

        HttpResponseMessage file = await admin.GetAsync($"/api/backup/{id}/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        byte[] bytes = await file.Content.ReadAsByteArrayAsync();
        Assert.Equal("KICOMLIBBK", Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.False(ContainsSequence(bytes, Encoding.ASCII.GetBytes(marker)));

        using MultipartFormDataContent upload = new();
        upload.Add(new ByteArrayContent(bytes), "file", "imported.kibkp");
        upload.Add(new StringContent("Imported copy"), "name");
        HttpResponseMessage imported = await admin.PostAsync("/api/backup/upload", upload);
        string importedBody = await imported.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        JsonElement importedPart = JsonDocument.Parse(importedBody).RootElement;
        string importedId = importedPart.GetProperty("id").GetString()!;
        Assert.Equal("Imported-copy.kibkp", importedPart.GetProperty("fileName").GetString());
        Assert.True(importedPart.GetProperty("encrypted").GetBoolean());

        await File.WriteAllTextAsync(markerPath, "kicomlib-marker-value-v2");
        HttpResponseMessage rejected = await admin.PostAsJsonAsync($"/api/backup/{id}/restore", new { passphrase = "wrong-passphrase-value" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("kicomlib-marker-value-v2", await File.ReadAllTextAsync(markerPath));

        HttpResponseMessage restored = await admin.PostAsJsonAsync($"/api/backup/{id}/restore", new { passphrase });
        string restoredBody = await restored.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using JsonDocument restoredDoc = JsonDocument.Parse(restoredBody);
        Assert.False(restoredDoc.RootElement.GetProperty("restarting").GetBoolean());
        Assert.True(restoredDoc.RootElement.GetProperty("keysRestored").GetBoolean());
        Assert.Equal(marker, await File.ReadAllTextAsync(markerPath));

        admin = await LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/backup/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/backup/{importedId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/backup/{id}")).StatusCode);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (int index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<HttpClient> LoginAsync()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "kicomlib" });
        string body = await login.Content.ReadAsStringAsync();
        Assert.True(login.IsSuccessStatusCode, body);
        TestSessions.UseBearer(client, login);
        return client;
    }
}
