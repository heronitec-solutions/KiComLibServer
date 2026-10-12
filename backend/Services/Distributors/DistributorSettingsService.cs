using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class MouserSettingsView
    {
        public bool Enabled { get; init; }

        public bool ApiKeySet { get; init; }
    }

    public sealed class DigiKeySettingsView
    {
        public bool Enabled { get; init; }

        public string ClientId { get; init; } = "";

        public bool ClientSecretSet { get; init; }

        public string Locale { get; init; } = "en";
    }

    public sealed class FarnellSettingsView
    {
        public bool Enabled { get; init; }

        public bool ApiKeySet { get; init; }

        public string Store { get; init; } = "de.farnell.com";
    }

    public sealed class ArrowSettingsView
    {
        public bool Enabled { get; init; }

        public string Login { get; init; } = "";

        public bool ApiKeySet { get; init; }
    }

    public sealed class TmeSettingsView
    {
        public bool Enabled { get; init; }

        public bool TokenSet { get; init; }

        public bool AppSecretSet { get; init; }

        public string Country { get; init; } = "DE";

        public string Language { get; init; } = "EN";
    }

    public sealed class DistributorSettingsView
    {
        public string LookupProvider { get; init; } = "";

        public MouserSettingsView Mouser { get; init; } = new();

        // CamelCase would emit "digiKey"; the API uses the provider id "digikey".
        [JsonPropertyName("digikey")]
        public DigiKeySettingsView DigiKey { get; init; } = new();

        public FarnellSettingsView Farnell { get; init; } = new();

        public ArrowSettingsView Arrow { get; init; } = new();

        public TmeSettingsView Tme { get; init; } = new();
    }

    public sealed class MouserSettingsWrite
    {
        public bool Enabled { get; set; }

        public string? ApiKey { get; set; }

        public bool ClearApiKey { get; set; }
    }

    public sealed class DigiKeySettingsWrite
    {
        public bool Enabled { get; set; }

        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public bool ClearClientSecret { get; set; }

        public string? Locale { get; set; }
    }

    public sealed class FarnellSettingsWrite
    {
        public bool Enabled { get; set; }

        public string? ApiKey { get; set; }

        public bool ClearApiKey { get; set; }

        public string? Store { get; set; }
    }

    public sealed class ArrowSettingsWrite
    {
        public bool Enabled { get; set; }

        public string? Login { get; set; }

        public string? ApiKey { get; set; }

        public bool ClearApiKey { get; set; }
    }

    public sealed class TmeSettingsWrite
    {
        public bool Enabled { get; set; }

        public string? Token { get; set; }

        public bool ClearToken { get; set; }

        public string? AppSecret { get; set; }

        public bool ClearAppSecret { get; set; }

        public string? Country { get; set; }

        public string? Language { get; set; }
    }

    public sealed class DistributorSettingsWriteRequest
    {
        public string? LookupProvider { get; set; }

        public MouserSettingsWrite? Mouser { get; set; }

        [JsonPropertyName("digikey")]
        public DigiKeySettingsWrite? DigiKey { get; set; }

        public FarnellSettingsWrite? Farnell { get; set; }

        public ArrowSettingsWrite? Arrow { get; set; }

        public TmeSettingsWrite? Tme { get; set; }
    }

    public sealed class MouserAccess
    {
        public required string ApiKey { get; init; }
    }

    public sealed class DigiKeyAccess
    {
        public required string ClientId { get; init; }

        public required string ClientSecret { get; init; }

        public required string Locale { get; init; }
    }

    public sealed class FarnellAccess
    {
        public required string ApiKey { get; init; }

        public required string Store { get; init; }
    }

    public sealed class ArrowAccess
    {
        public required string Login { get; init; }

        public required string ApiKey { get; init; }
    }

    public sealed class TmeAccess
    {
        public required string Token { get; init; }

        public required string AppSecret { get; init; }

        public required string Country { get; init; }

        public required string Language { get; init; }
    }

    public sealed class DistributorTestAccess
    {
        public string? Error { get; init; }

        public string Provider { get; init; } = "";

        public string Name { get; init; } = "";

        public MouserAccess? Mouser { get; init; }

        public DigiKeyAccess? DigiKey { get; init; }

        public FarnellAccess? Farnell { get; init; }

        public ArrowAccess? Arrow { get; init; }

        public TmeAccess? Tme { get; init; }

        public static DistributorTestAccess Fail(string error) => new() { Error = error };
    }

    public sealed class DistributorAvailability
    {
        public string LookupProvider { get; init; } = "";

        public required IReadOnlyDictionary<string, bool> Enabled { get; init; }

        public MouserAccess? Mouser { get; init; }

        public DigiKeyAccess? DigiKey { get; init; }

        public FarnellAccess? Farnell { get; init; }

        public ArrowAccess? Arrow { get; init; }

        public TmeAccess? Tme { get; init; }

        public bool IsUsable(string providerId)
        {
            return Enabled.TryGetValue(providerId, out bool enabled) && enabled;
        }
    }

    public sealed class DistributorSettingsService
    {
        public const string DefaultDigiKeyLocale = "en";
        public const string DefaultFarnellStore = "de.farnell.com";
        public const string DefaultTmeCountry = "DE";
        public const string DefaultTmeLanguage = "EN";
        public const int SecretMaxLength = 500;
        public const int TextMaxLength = 200;
        public const int LocaleMaxLength = 16;
        public const int RegionMaxLength = 8;

        public const string FarnellStoreError =
            "Farnell store must be uk.farnell.com, de.farnell.com, www.newark.com, or sg.element14.com";

        public static readonly string[] FarnellStores =
        {
            "uk.farnell.com",
            "de.farnell.com",
            "www.newark.com",
            "sg.element14.com"
        };

        public static string? CanonicalFarnellStore(string? store)
        {
            string trimmed = (store ?? "").Trim();
            if (trimmed.Equals("newark.com", StringComparison.OrdinalIgnoreCase))
            {
                return "www.newark.com";
            }

            return FarnellStores.FirstOrDefault(item => item.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private readonly AppSettingsService _settings;
        private readonly SecretProtector _secrets;
        private readonly ILogger<DistributorSettingsService> _logger;

        public DistributorSettingsService(
            AppSettingsService settings,
            SecretProtector secrets,
            ILogger<DistributorSettingsService> logger)
        {
            _settings = settings;
            _secrets = secrets;
            _logger = logger;
        }

        public async Task<DistributorSettingsView> ViewAsync(CancellationToken cancellationToken)
        {
            return new DistributorSettingsView
            {
                LookupProvider = await LookupProviderAsync(cancellationToken),
                Mouser = new MouserSettingsView
                {
                    Enabled = await _settings.GetBoolAsync(AppSettingsService.MouserEnabledKey, false, cancellationToken),
                    ApiKeySet = await SecretStoredAsync(AppSettingsService.MouserApiKeyKey, cancellationToken)
                },
                DigiKey = new DigiKeySettingsView
                {
                    Enabled = await _settings.GetBoolAsync(AppSettingsService.DigiKeyEnabledKey, false, cancellationToken),
                    ClientId = (await _settings.GetStringAsync(AppSettingsService.DigiKeyClientIdKey, "", cancellationToken)).Trim(),
                    ClientSecretSet = await SecretStoredAsync(AppSettingsService.DigiKeyClientSecretKey, cancellationToken),
                    Locale = await TextAsync(AppSettingsService.DigiKeyLocaleKey, DefaultDigiKeyLocale, cancellationToken)
                },
                Farnell = new FarnellSettingsView
                {
                    Enabled = await _settings.GetBoolAsync(AppSettingsService.FarnellEnabledKey, false, cancellationToken),
                    ApiKeySet = await SecretStoredAsync(AppSettingsService.FarnellApiKeyKey, cancellationToken),
                    Store = CanonicalFarnellStore(await TextAsync(AppSettingsService.FarnellStoreKey, DefaultFarnellStore, cancellationToken))
                        ?? DefaultFarnellStore
                },
                Arrow = new ArrowSettingsView
                {
                    Enabled = await _settings.GetBoolAsync(AppSettingsService.ArrowEnabledKey, false, cancellationToken),
                    Login = (await _settings.GetStringAsync(AppSettingsService.ArrowLoginKey, "", cancellationToken)).Trim(),
                    ApiKeySet = await SecretStoredAsync(AppSettingsService.ArrowApiKeyKey, cancellationToken)
                },
                Tme = new TmeSettingsView
                {
                    Enabled = await _settings.GetBoolAsync(AppSettingsService.TmeEnabledKey, false, cancellationToken),
                    TokenSet = await SecretStoredAsync(AppSettingsService.TmeTokenKey, cancellationToken),
                    AppSecretSet = await SecretStoredAsync(AppSettingsService.TmeAppSecretKey, cancellationToken),
                    Country = await TextAsync(AppSettingsService.TmeCountryKey, DefaultTmeCountry, cancellationToken),
                    Language = await TextAsync(AppSettingsService.TmeLanguageKey, DefaultTmeLanguage, cancellationToken)
                }
            };
        }

        public async Task<DistributorAvailability> AvailabilityAsync(CancellationToken cancellationToken)
        {
            bool mouserFlag = await _settings.GetBoolAsync(AppSettingsService.MouserEnabledKey, false, cancellationToken);
            bool digikeyFlag = await _settings.GetBoolAsync(AppSettingsService.DigiKeyEnabledKey, false, cancellationToken);
            bool farnellFlag = await _settings.GetBoolAsync(AppSettingsService.FarnellEnabledKey, false, cancellationToken);
            bool arrowFlag = await _settings.GetBoolAsync(AppSettingsService.ArrowEnabledKey, false, cancellationToken);
            bool tmeFlag = await _settings.GetBoolAsync(AppSettingsService.TmeEnabledKey, false, cancellationToken);

            string? mouserKey = mouserFlag ? await ReadSecretAsync(AppSettingsService.MouserApiKeyKey, cancellationToken) : null;
            string clientId = (await _settings.GetStringAsync(AppSettingsService.DigiKeyClientIdKey, "", cancellationToken)).Trim();
            string? clientSecret = digikeyFlag ? await ReadSecretAsync(AppSettingsService.DigiKeyClientSecretKey, cancellationToken) : null;
            string locale = await TextAsync(AppSettingsService.DigiKeyLocaleKey, DefaultDigiKeyLocale, cancellationToken);
            string? farnellKey = farnellFlag ? await ReadSecretAsync(AppSettingsService.FarnellApiKeyKey, cancellationToken) : null;
            string store = CanonicalFarnellStore(await TextAsync(AppSettingsService.FarnellStoreKey, DefaultFarnellStore, cancellationToken))
                ?? DefaultFarnellStore;
            string login = (await _settings.GetStringAsync(AppSettingsService.ArrowLoginKey, "", cancellationToken)).Trim();
            string? arrowKey = arrowFlag ? await ReadSecretAsync(AppSettingsService.ArrowApiKeyKey, cancellationToken) : null;
            string? token = tmeFlag ? await ReadSecretAsync(AppSettingsService.TmeTokenKey, cancellationToken) : null;
            string? appSecret = tmeFlag ? await ReadSecretAsync(AppSettingsService.TmeAppSecretKey, cancellationToken) : null;
            string country = await TextAsync(AppSettingsService.TmeCountryKey, DefaultTmeCountry, cancellationToken);
            string language = await TextAsync(AppSettingsService.TmeLanguageKey, DefaultTmeLanguage, cancellationToken);

            MouserAccess? mouser = mouserFlag && mouserKey != null ? new MouserAccess { ApiKey = mouserKey } : null;
            DigiKeyAccess? digikey = digikeyFlag && clientId.Length > 0 && clientSecret != null
                ? new DigiKeyAccess { ClientId = clientId, ClientSecret = clientSecret, Locale = locale }
                : null;
            FarnellAccess? farnell = farnellFlag && farnellKey != null
                ? new FarnellAccess { ApiKey = farnellKey, Store = store }
                : null;
            ArrowAccess? arrow = arrowFlag && login.Length > 0 && arrowKey != null
                ? new ArrowAccess { Login = login, ApiKey = arrowKey }
                : null;
            TmeAccess? tme = tmeFlag && token != null && appSecret != null
                ? new TmeAccess { Token = token, AppSecret = appSecret, Country = country, Language = language }
                : null;

            return new DistributorAvailability
            {
                LookupProvider = await LookupProviderAsync(cancellationToken),
                Enabled = new Dictionary<string, bool>
                {
                    [DistributorProviders.Mouser] = mouser != null,
                    [DistributorProviders.DigiKey] = digikey != null,
                    [DistributorProviders.Farnell] = farnell != null,
                    [DistributorProviders.Arrow] = arrow != null,
                    [DistributorProviders.Tme] = tme != null
                },
                Mouser = mouser,
                DigiKey = digikey,
                Farnell = farnell,
                Arrow = arrow,
                Tme = tme
            };
        }

        /// <summary>
        /// Credentials for a connection test. A typed secret wins; otherwise the stored secret is used, even when the provider is off.
        /// </summary>
        public async Task<DistributorTestAccess> AccessForTestAsync(
            string? providerId,
            DistributorSettingsWriteRequest? request,
            CancellationToken cancellationToken)
        {
            string? id = DistributorProviders.Canonical(providerId);
            DistributorProviderInfo? info = id == null ? null : DistributorProviders.All.FirstOrDefault(item => item.Id == id);
            if (info == null)
            {
                return DistributorTestAccess.Fail("Unknown distributor provider");
            }

            request ??= new DistributorSettingsWriteRequest();
            return id switch
            {
                DistributorProviders.Mouser => await MouserForTestAsync(info, request.Mouser, cancellationToken),
                DistributorProviders.DigiKey => await DigiKeyForTestAsync(info, request.DigiKey, cancellationToken),
                DistributorProviders.Farnell => await FarnellForTestAsync(info, request.Farnell, cancellationToken),
                DistributorProviders.Arrow => await ArrowForTestAsync(info, request.Arrow, cancellationToken),
                DistributorProviders.Tme => await TmeForTestAsync(info, request.Tme, cancellationToken),
                _ => DistributorTestAccess.Fail("Unknown distributor provider")
            };
        }

        public async Task<string?> SaveAsync(DistributorSettingsWriteRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return "Distributor settings are required";
            }

            if (request.Mouser == null || request.DigiKey == null || request.Farnell == null || request.Arrow == null || request.Tme == null)
            {
                return "Distributor settings are required";
            }

            string lookup = (request.LookupProvider ?? "").Trim().ToLowerInvariant();
            if (lookup.Length > 0 && DistributorProviders.Canonical(lookup) == null)
            {
                return "Lookup provider must be empty or an enabled distributor";
            }

            string mouserKey = (request.Mouser.ApiKey ?? "").Trim();
            string clientId = (request.DigiKey.ClientId ?? "").Trim();
            string clientSecret = (request.DigiKey.ClientSecret ?? "").Trim();
            string locale = (request.DigiKey.Locale ?? "").Trim();
            string farnellKey = (request.Farnell.ApiKey ?? "").Trim();
            string store = (request.Farnell.Store ?? "").Trim();
            string login = (request.Arrow.Login ?? "").Trim();
            string arrowKey = (request.Arrow.ApiKey ?? "").Trim();
            string token = (request.Tme.Token ?? "").Trim();
            string appSecret = (request.Tme.AppSecret ?? "").Trim();
            string country = (request.Tme.Country ?? "").Trim();
            string language = (request.Tme.Language ?? "").Trim();

            if (locale.Length == 0)
            {
                locale = DefaultDigiKeyLocale;
            }

            if (store.Length == 0)
            {
                store = DefaultFarnellStore;
            }

            if (country.Length == 0)
            {
                country = DefaultTmeCountry;
            }

            if (language.Length == 0)
            {
                language = DefaultTmeLanguage;
            }

            string? fieldError = First(
                Limit(mouserKey, SecretMaxLength, "Mouser API key"),
                Limit(clientId, TextMaxLength, "DigiKey client ID"),
                Limit(clientSecret, SecretMaxLength, "DigiKey client secret"),
                Limit(locale, LocaleMaxLength, "DigiKey locale"),
                Limit(farnellKey, SecretMaxLength, "Farnell API key"),
                Limit(login, TextMaxLength, "Arrow login"),
                Limit(arrowKey, SecretMaxLength, "Arrow API key"),
                Limit(token, SecretMaxLength, "TME token"),
                Limit(appSecret, SecretMaxLength, "TME app secret"),
                Limit(country, RegionMaxLength, "TME country"),
                Limit(language, RegionMaxLength, "TME language"));
            if (fieldError != null)
            {
                return fieldError;
            }

            if (HasControl(mouserKey) || HasControl(clientId) || HasControl(clientSecret) || HasControl(locale)
                || HasControl(farnellKey) || HasControl(login) || HasControl(arrowKey)
                || HasControl(token) || HasControl(appSecret) || HasControl(country) || HasControl(language))
            {
                return "Distributor settings cannot contain control characters";
            }

            string? canonicalStore = CanonicalFarnellStore(store);
            if (canonicalStore == null)
            {
                return FarnellStoreError;
            }

            bool mouserStored = await SecretStoredAsync(AppSettingsService.MouserApiKeyKey, cancellationToken);
            bool digikeySecretStored = await SecretStoredAsync(AppSettingsService.DigiKeyClientSecretKey, cancellationToken);
            bool farnellStored = await SecretStoredAsync(AppSettingsService.FarnellApiKeyKey, cancellationToken);
            bool arrowStored = await SecretStoredAsync(AppSettingsService.ArrowApiKeyKey, cancellationToken);
            bool tokenStored = await SecretStoredAsync(AppSettingsService.TmeTokenKey, cancellationToken);
            bool appSecretStored = await SecretStoredAsync(AppSettingsService.TmeAppSecretKey, cancellationToken);

            bool mouserReady = mouserKey.Length > 0 || (mouserStored && !request.Mouser.ClearApiKey);
            bool digikeyReady = clientId.Length > 0 && (clientSecret.Length > 0 || (digikeySecretStored && !request.DigiKey.ClearClientSecret));
            bool farnellReady = farnellKey.Length > 0 || (farnellStored && !request.Farnell.ClearApiKey);
            bool arrowReady = login.Length > 0 && (arrowKey.Length > 0 || (arrowStored && !request.Arrow.ClearApiKey));
            bool tmeReady = (token.Length > 0 || (tokenStored && !request.Tme.ClearToken))
                && (appSecret.Length > 0 || (appSecretStored && !request.Tme.ClearAppSecret));

            if (request.Mouser.Enabled && !mouserReady)
            {
                return "Enter a Mouser API key before turning Mouser on";
            }

            if (request.DigiKey.Enabled && !digikeyReady)
            {
                return "Enter a DigiKey client ID and client secret before turning DigiKey on";
            }

            if (request.Farnell.Enabled && !farnellReady)
            {
                return "Enter a Farnell API key before turning Farnell on";
            }

            if (request.Arrow.Enabled && !arrowReady)
            {
                return "Enter an Arrow login and API key before turning Arrow on";
            }

            if (request.Tme.Enabled && !tmeReady)
            {
                return "Enter a TME token and app secret before turning TME on";
            }

            Dictionary<string, bool> enabled = new()
            {
                [DistributorProviders.Mouser] = request.Mouser.Enabled,
                [DistributorProviders.DigiKey] = request.DigiKey.Enabled,
                [DistributorProviders.Farnell] = request.Farnell.Enabled,
                [DistributorProviders.Arrow] = request.Arrow.Enabled,
                [DistributorProviders.Tme] = request.Tme.Enabled
            };
            if (lookup.Length > 0 && (!enabled.TryGetValue(lookup, out bool lookupEnabled) || !lookupEnabled))
            {
                return "Lookup provider must be empty or an enabled distributor";
            }

            await _settings.SetAsync(AppSettingsService.DistributorLookupProviderKey, lookup, cancellationToken);
            await _settings.SetAsync(AppSettingsService.MouserEnabledKey, Flag(request.Mouser.Enabled), cancellationToken);
            await SaveSecretAsync(AppSettingsService.MouserApiKeyKey, mouserKey, request.Mouser.ClearApiKey, cancellationToken);
            await _settings.SetAsync(AppSettingsService.DigiKeyEnabledKey, Flag(request.DigiKey.Enabled), cancellationToken);
            await _settings.SetAsync(AppSettingsService.DigiKeyClientIdKey, clientId, cancellationToken);
            await SaveSecretAsync(AppSettingsService.DigiKeyClientSecretKey, clientSecret, request.DigiKey.ClearClientSecret, cancellationToken);
            await _settings.SetAsync(AppSettingsService.DigiKeyLocaleKey, locale, cancellationToken);
            await _settings.SetAsync(AppSettingsService.FarnellEnabledKey, Flag(request.Farnell.Enabled), cancellationToken);
            await SaveSecretAsync(AppSettingsService.FarnellApiKeyKey, farnellKey, request.Farnell.ClearApiKey, cancellationToken);
            await _settings.SetAsync(AppSettingsService.FarnellStoreKey, canonicalStore, cancellationToken);
            await _settings.SetAsync(AppSettingsService.ArrowEnabledKey, Flag(request.Arrow.Enabled), cancellationToken);
            await _settings.SetAsync(AppSettingsService.ArrowLoginKey, login, cancellationToken);
            await SaveSecretAsync(AppSettingsService.ArrowApiKeyKey, arrowKey, request.Arrow.ClearApiKey, cancellationToken);
            await _settings.SetAsync(AppSettingsService.TmeEnabledKey, Flag(request.Tme.Enabled), cancellationToken);
            await SaveSecretAsync(AppSettingsService.TmeTokenKey, token, request.Tme.ClearToken, cancellationToken);
            await SaveSecretAsync(AppSettingsService.TmeAppSecretKey, appSecret, request.Tme.ClearAppSecret, cancellationToken);
            await _settings.SetAsync(AppSettingsService.TmeCountryKey, country, cancellationToken);
            await _settings.SetAsync(AppSettingsService.TmeLanguageKey, language, cancellationToken);
            return null;
        }

        private async Task<DistributorTestAccess> MouserForTestAsync(
            DistributorProviderInfo info,
            MouserSettingsWrite? write,
            CancellationToken cancellationToken)
        {
            string? key = await IncomingOrStoredAsync(write?.ApiKey, write?.ClearApiKey == true, AppSettingsService.MouserApiKeyKey, cancellationToken);
            string? invalid = SecretProblem(key, "Mouser API key", "Enter a Mouser API key before testing");
            if (invalid != null)
            {
                return DistributorTestAccess.Fail(invalid);
            }

            return new DistributorTestAccess { Provider = info.Id, Name = info.Name, Mouser = new MouserAccess { ApiKey = key! } };
        }

        private async Task<DistributorTestAccess> DigiKeyForTestAsync(
            DistributorProviderInfo info,
            DigiKeySettingsWrite? write,
            CancellationToken cancellationToken)
        {
            string clientId = await IncomingOrStoredTextAsync(write?.ClientId, AppSettingsService.DigiKeyClientIdKey, "", cancellationToken);
            string? secret = await IncomingOrStoredAsync(write?.ClientSecret, write?.ClearClientSecret == true, AppSettingsService.DigiKeyClientSecretKey, cancellationToken);
            string locale = await IncomingOrStoredTextAsync(write?.Locale, AppSettingsService.DigiKeyLocaleKey, DefaultDigiKeyLocale, cancellationToken);
            string? invalid = First(
                PlainProblem(clientId, TextMaxLength, "DigiKey client ID", "Enter a DigiKey client ID and client secret before testing"),
                SecretProblem(secret, "DigiKey client secret", "Enter a DigiKey client ID and client secret before testing"),
                PlainProblem(locale, LocaleMaxLength, "DigiKey locale", "Enter a DigiKey locale before testing"));
            if (invalid != null)
            {
                return DistributorTestAccess.Fail(invalid);
            }

            return new DistributorTestAccess
            {
                Provider = info.Id,
                Name = info.Name,
                DigiKey = new DigiKeyAccess { ClientId = clientId, ClientSecret = secret!, Locale = locale }
            };
        }

        private async Task<DistributorTestAccess> FarnellForTestAsync(
            DistributorProviderInfo info,
            FarnellSettingsWrite? write,
            CancellationToken cancellationToken)
        {
            string? key = await IncomingOrStoredAsync(write?.ApiKey, write?.ClearApiKey == true, AppSettingsService.FarnellApiKeyKey, cancellationToken);
            string store = await IncomingOrStoredTextAsync(write?.Store, AppSettingsService.FarnellStoreKey, DefaultFarnellStore, cancellationToken);
            string? canonicalStore = CanonicalFarnellStore(store);
            string? invalid = SecretProblem(key, "Farnell API key", "Enter a Farnell API key before testing");
            if (invalid != null)
            {
                return DistributorTestAccess.Fail(invalid);
            }

            if (canonicalStore == null)
            {
                return DistributorTestAccess.Fail(FarnellStoreError);
            }

            return new DistributorTestAccess
            {
                Provider = info.Id,
                Name = info.Name,
                Farnell = new FarnellAccess { ApiKey = key!, Store = canonicalStore }
            };
        }

        private async Task<DistributorTestAccess> ArrowForTestAsync(
            DistributorProviderInfo info,
            ArrowSettingsWrite? write,
            CancellationToken cancellationToken)
        {
            string login = await IncomingOrStoredTextAsync(write?.Login, AppSettingsService.ArrowLoginKey, "", cancellationToken);
            string? key = await IncomingOrStoredAsync(write?.ApiKey, write?.ClearApiKey == true, AppSettingsService.ArrowApiKeyKey, cancellationToken);
            string? invalid = First(
                PlainProblem(login, TextMaxLength, "Arrow login", "Enter an Arrow login and API key before testing"),
                SecretProblem(key, "Arrow API key", "Enter an Arrow login and API key before testing"));
            if (invalid != null)
            {
                return DistributorTestAccess.Fail(invalid);
            }

            return new DistributorTestAccess
            {
                Provider = info.Id,
                Name = info.Name,
                Arrow = new ArrowAccess { Login = login, ApiKey = key! }
            };
        }

        private async Task<DistributorTestAccess> TmeForTestAsync(
            DistributorProviderInfo info,
            TmeSettingsWrite? write,
            CancellationToken cancellationToken)
        {
            string? token = await IncomingOrStoredAsync(write?.Token, write?.ClearToken == true, AppSettingsService.TmeTokenKey, cancellationToken);
            string? appSecret = await IncomingOrStoredAsync(write?.AppSecret, write?.ClearAppSecret == true, AppSettingsService.TmeAppSecretKey, cancellationToken);
            string country = await IncomingOrStoredTextAsync(write?.Country, AppSettingsService.TmeCountryKey, DefaultTmeCountry, cancellationToken);
            string language = await IncomingOrStoredTextAsync(write?.Language, AppSettingsService.TmeLanguageKey, DefaultTmeLanguage, cancellationToken);
            string? invalid = First(
                SecretProblem(token, "TME token", "Enter a TME token and app secret before testing"),
                SecretProblem(appSecret, "TME app secret", "Enter a TME token and app secret before testing"),
                PlainProblem(country, RegionMaxLength, "TME country", "Enter a TME country before testing"),
                PlainProblem(language, RegionMaxLength, "TME language", "Enter a TME language before testing"));
            if (invalid != null)
            {
                return DistributorTestAccess.Fail(invalid);
            }

            return new DistributorTestAccess
            {
                Provider = info.Id,
                Name = info.Name,
                Tme = new TmeAccess { Token = token!, AppSecret = appSecret!, Country = country, Language = language }
            };
        }

        private async Task<string?> IncomingOrStoredAsync(string? incoming, bool clear, string key, CancellationToken cancellationToken)
        {
            string typed = (incoming ?? "").Trim();
            if (typed.Length > 0)
            {
                return typed;
            }

            if (clear)
            {
                return null;
            }

            return await ReadSecretAsync(key, cancellationToken);
        }

        private async Task<string> IncomingOrStoredTextAsync(string? incoming, string key, string fallback, CancellationToken cancellationToken)
        {
            string typed = (incoming ?? "").Trim();
            if (typed.Length > 0)
            {
                return typed;
            }

            return await TextAsync(key, fallback, cancellationToken);
        }

        private static string? SecretProblem(string? value, string label, string missing)
        {
            if (string.IsNullOrEmpty(value))
            {
                return missing;
            }

            return PlainProblem(value, SecretMaxLength, label, missing);
        }

        private static string? PlainProblem(string value, int max, string label, string missing)
        {
            if (value.Length == 0)
            {
                return missing;
            }

            if (value.Length > max)
            {
                return $"{label} must be at most {max} characters";
            }

            if (HasControl(value))
            {
                return "Distributor settings cannot contain control characters";
            }

            return null;
        }

        private async Task<string> LookupProviderAsync(CancellationToken cancellationToken)
        {
            string value = (await _settings.GetStringAsync(AppSettingsService.DistributorLookupProviderKey, "", cancellationToken)).Trim().ToLowerInvariant();
            return DistributorProviders.Canonical(value) ?? "";
        }

        private async Task<string> TextAsync(string key, string fallback, CancellationToken cancellationToken)
        {
            string value = (await _settings.GetStringAsync(key, fallback, cancellationToken)).Trim();
            return value.Length == 0 ? fallback : value;
        }

        private async Task<bool> SecretStoredAsync(string key, CancellationToken cancellationToken)
        {
            return !string.IsNullOrWhiteSpace(await _settings.FindAsync(key, cancellationToken));
        }

        private async Task SaveSecretAsync(string key, string incoming, bool clear, CancellationToken cancellationToken)
        {
            if (incoming.Length > 0)
            {
                await _settings.SetAsync(key, _secrets.Protect(incoming), cancellationToken);
            }
            else if (clear)
            {
                await _settings.DeleteAsync(key, cancellationToken);
            }
        }

        private async Task<string?> ReadSecretAsync(string key, CancellationToken cancellationToken)
        {
            string? protectedValue = await _settings.FindAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }

            try
            {
                string value = _secrets.Unprotect(protectedValue).Trim();
                return value.Length == 0 ? null : value;
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "Stored distributor secret could not be decrypted");
                return null;
            }
        }

        private static string Flag(bool enabled) => enabled ? "true" : "false";

        private static string? Limit(string value, int max, string label)
        {
            if (value.Length > max)
            {
                return $"{label} must be at most {max} characters";
            }

            return null;
        }

        private static string? First(params string?[] errors)
        {
            foreach (string? error in errors)
            {
                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }

        private static bool HasControl(string value) => value.Any(char.IsControl);
    }
}
