using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using KiComLibBackend.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KiComLibBackend.Tests;

public class DirectoryFilterTests
{
    [Fact]
    public void Escape_EncodesFilterMetacharacters()
    {
        Assert.Equal("a\\2ab\\28c\\29\\5cd", DirectoryFilters.Escape("a*b(c)\\d"));
        Assert.Equal("", DirectoryFilters.Escape(""));
    }

    [Fact]
    public void UserSearchFilter_EscapesTheQueryAndKeepsTheLoginShape()
    {
        DirectorySettings settings = Sample();
        string filter = DirectoryFilters.UserSearchFilter(settings, "a*b");
        Assert.Contains("(sAMAccountName=*)", filter);
        Assert.Contains("(sAMAccountName=*a\\2ab*)", filter);
        Assert.DoesNotContain("{username}", filter);
    }

    [Fact]
    public void LoginFilter_SubstitutesTheEscapedUsername()
    {
        string filter = DirectoryFilters.LoginFilter(Sample(), "ada)");
        Assert.Equal("(&(objectClass=user)(sAMAccountName=ada\\29))", filter);
    }

    [Fact]
    public void Model_MapsDirectoryAccountsAndMembershipOrigin()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;
        using ApplicationDbContext db = new(options);
        Assert.NotNull(db.Model.FindEntityType(typeof(DirectoryConnection)));
        Assert.NotNull(db.Model.FindEntityType(typeof(UserGroupLink))!.FindProperty("Origin"));
        Assert.NotNull(db.Model.FindEntityType(typeof(AppUser))!.FindProperty("Source"));
        Assert.NotNull(db.Model.FindEntityType(typeof(AppGroup))!.FindProperty("DirectoryDn"));
    }

    [Fact]
    public void IsDisabledAccount_HonoursTheActiveDirectoryFlag()
    {
        Assert.True(DirectoryFilters.IsDisabledAccount("514"));
        Assert.False(DirectoryFilters.IsDisabledAccount("512"));
        Assert.False(DirectoryFilters.IsDisabledAccount(null));
    }

    private static DirectorySettings Sample() => new()
    {
        UserFilter = "(&(objectClass=user)(sAMAccountName={username}))",
        UsernameAttribute = "sAMAccountName",
        DisplayNameAttribute = "displayName",
        GroupFilter = "(objectClass=group)",
        GroupNameAttribute = "cn"
    };
}

[Collection("kicomlib-db")]
public class DirectoryAccessTests
{
    private readonly CustomWebApplicationFactory _factory;

    public DirectoryAccessTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DirectorySignIn_AddsAccountsBesideLocalUsers_AndRefreshesGroupMembership()
    {
        if (!_factory.DatabaseAvailable)
        {
            return;
        }

        FakeDirectoryClient directory = new();
        directory.Users.Add(new DirectoryAccount
        {
            Id = "ada-guid",
            Username = "ada",
            DisplayName = "Ada Lovelace",
            DistinguishedName = "uid=ada,ou=people,dc=example,dc=com",
            GroupDistinguishedNames = new[] { "cn=Library,ou=groups,dc=example,dc=com" }
        });
        directory.Passwords["ada"] = "directory-secret";
        directory.Groups.Add(new DirectoryGroupRecord
        {
            Id = "library-guid",
            Name = "Library writers",
            DistinguishedName = "cn=Library,ou=groups,dc=example,dc=com",
            Description = "Catalog editors"
        });
        directory.Users.Add(new DirectoryAccount
        {
            Id = "admin-guid",
            Username = "admin",
            DisplayName = "Not the local admin",
            DistinguishedName = "uid=admin,ou=people,dc=example,dc=com"
        });
        directory.Passwords["admin"] = "from-the-directory";

        using WebApplicationFactory<Program> host = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDirectoryClient>();
                services.AddSingleton<IDirectoryClient>(directory);
            });
        });
        HttpClient admin = host.CreateClient();
        Assert.False(string.IsNullOrEmpty(await SignInAsync(admin, "admin", "kicomlib")));

        object settings = Connection(enabled: true, provisionOnLogin: true);
        try
        {
            HttpResponseMessage anonymous = await host.CreateClient().GetAsync("/api/directory");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            HttpResponseMessage saved = await admin.PutAsJsonAsync("/api/directory", settings);
            string savedBody = await saved.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using JsonDocument savedDoc = JsonDocument.Parse(savedBody);
            Assert.True(savedDoc.RootElement.GetProperty("bindPasswordSet").GetBoolean());
            Assert.True(savedDoc.RootElement.GetProperty("configured").GetBoolean());
            Assert.False(savedBody.Contains("svc-secret", StringComparison.Ordinal));

            HttpResponseMessage importedGroup = await admin.PostAsJsonAsync("/api/directory/groups/import", new
            {
                distinguishedNames = new[] { "cn=Library,ou=groups,dc=example,dc=com" },
                canRead = true,
                canWrite = true
            });
            Assert.Equal(HttpStatusCode.OK, importedGroup.StatusCode);

            HttpClient ada = host.CreateClient();
            HttpResponseMessage adaLogin = await ada.PostAsJsonAsync("/api/auth/login", new { username = "ada", password = "directory-secret" });
            string adaBody = await adaLogin.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, adaLogin.StatusCode);
            using JsonDocument adaDoc = JsonDocument.Parse(adaBody);
            Assert.Equal("ada", adaDoc.RootElement.GetProperty("user").GetProperty("username").GetString());
            Assert.Equal("directory", adaDoc.RootElement.GetProperty("user").GetProperty("source").GetString());
            Assert.True(adaDoc.RootElement.GetProperty("user").GetProperty("canWrite").GetBoolean());
            TestSessions.UseBearer(ada, adaLogin);
            Assert.Equal(HttpStatusCode.BadRequest, (await ada.PostAsJsonAsync("/api/auth/password", new { currentPassword = "directory-secret", newPassword = "local-pass" })).StatusCode);

            HttpResponseMessage users = await admin.GetAsync("/api/user");
            using JsonDocument usersDoc = JsonDocument.Parse(await users.Content.ReadAsStringAsync());
            JsonElement adaRow = usersDoc.RootElement.EnumerateArray().Single(row => row.GetProperty("username").GetString() == "ada");
            Assert.Equal("directory", adaRow.GetProperty("source").GetString());
            Assert.Equal("Ada Lovelace", adaRow.GetProperty("displayName").GetString());
            int adaId = adaRow.GetProperty("id").GetInt32();
            Assert.Contains(adaRow.GetProperty("groups").EnumerateArray(), group => group.GetProperty("name").GetString() == "Library writers" && group.GetProperty("origin").GetString() == "directory");

            using JsonDocument groupsDoc = JsonDocument.Parse(await (await admin.GetAsync("/api/group")).Content.ReadAsStringAsync());
            int extraId = 0;
            HttpResponseMessage extra = await admin.PostAsJsonAsync("/api/group", new { name = "Bench " + Guid.NewGuid().ToString("N")[..6], description = "local", canRead = true });
            using JsonDocument extraDoc = JsonDocument.Parse(await extra.Content.ReadAsStringAsync());
            extraId = extraDoc.RootElement.GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/user/" + adaId, new
            {
                username = "ada",
                isActive = true,
                groupIds = new[] { extraId }
            })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/user/" + adaId, new
            {
                username = "ada",
                password = "nope-nope",
                isActive = true,
                groupIds = new[] { extraId }
            })).StatusCode);

            directory.Users[0] = new DirectoryAccount
            {
                Id = "ada-guid",
                Username = "ada",
                DisplayName = "Ada Lovelace",
                DistinguishedName = "uid=ada,ou=people,dc=example,dc=com",
                GroupDistinguishedNames = Array.Empty<string>()
            };
            HttpResponseMessage again = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "ADA", password = "directory-secret" });
            string againBody = await again.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            using JsonDocument againDoc = JsonDocument.Parse(againBody);
            JsonElement againGroups = againDoc.RootElement.GetProperty("user").GetProperty("groups");
            Assert.Contains(againGroups.EnumerateArray(), group => group.GetProperty("name").GetString() == extraDoc.RootElement.GetProperty("name").GetString());
            Assert.DoesNotContain(againGroups.EnumerateArray(), group => group.GetProperty("name").GetString() == "Library writers");

            HttpResponseMessage hijack = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "from-the-directory" });
            Assert.Equal(HttpStatusCode.Unauthorized, hijack.StatusCode);
            Assert.False(string.IsNullOrEmpty(await SignInAsync(host.CreateClient(), "admin", "kicomlib")));

            HttpResponseMessage turnedOff = await admin.PutAsJsonAsync("/api/directory", Connection(enabled: false, provisionOnLogin: false));
            Assert.Equal(HttpStatusCode.OK, turnedOff.StatusCode);
            HttpResponseMessage blocked = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "ada", password = "directory-secret" });
            Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/directory", Connection(enabled: false, provisionOnLogin: false));
        }
    }

    private static async Task<string?> SignInAsync(HttpClient client, string username, string password)
    {
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!login.IsSuccessStatusCode)
        {
            return null;
        }

        TestSessions.UseBearer(client, login);
        return username;
    }

    private static object Connection(bool enabled, bool provisionOnLogin) => new
    {
        enabled,
        provisionOnLogin,
        serverType = "ad",
        host = "ldap.example",
        port = 389,
        encryption = "none",
        allowUntrustedCertificate = false,
        bindDn = "cn=svc,dc=example,dc=com",
        bindPassword = "svc-secret",
        userBaseDn = "ou=people,dc=example,dc=com",
        userFilter = "(&(objectClass=user)(sAMAccountName={username}))",
        usernameAttribute = "sAMAccountName",
        displayNameAttribute = "displayName",
        userIdAttribute = "objectGUID",
        groupBaseDn = "ou=groups,dc=example,dc=com",
        groupFilter = "(objectClass=group)",
        groupNameAttribute = "cn",
        groupIdAttribute = "objectGUID",
        membershipAttribute = "memberOf"
    };

    private sealed class FakeDirectoryClient : IDirectoryClient
    {
        public List<DirectoryAccount> Users { get; } = new();

        public List<DirectoryGroupRecord> Groups { get; } = new();

        public Dictionary<string, string> Passwords { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task TestAsync(DirectorySettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DirectoryAccount?> AuthenticateAsync(DirectorySettings settings, string username, string password, CancellationToken cancellationToken)
        {
            DirectoryAccount? account = Users.FirstOrDefault(user => user.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (account == null || !Passwords.TryGetValue(account.Username, out string? expected) || expected != password)
            {
                return Task.FromResult<DirectoryAccount?>(null);
            }

            return Task.FromResult<DirectoryAccount?>(account);
        }

        public Task<IReadOnlyList<DirectoryAccount>> SearchUsersAsync(DirectorySettings settings, string query, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DirectoryAccount>>(Users);
        }

        public Task<IReadOnlyList<DirectoryGroupRecord>> SearchGroupsAsync(DirectorySettings settings, string query, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DirectoryGroupRecord>>(Groups);
        }

        public Task<DirectoryAccount?> ReadUserAsync(DirectorySettings settings, string distinguishedName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Users.FirstOrDefault(user => user.DistinguishedName.Equals(distinguishedName, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<DirectoryGroupRecord?> ReadGroupAsync(DirectorySettings settings, string distinguishedName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Groups.FirstOrDefault(group => group.DistinguishedName.Equals(distinguishedName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
