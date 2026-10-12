using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class AppSettingsService
    {
        public const string SessionLifetimeKey = "session_lifetime_days";
        public const int DefaultSessionLifetimeDays = 30;
        public const int MinSessionLifetimeDays = 1;
        public const int MaxSessionLifetimeDays = 365;

        private readonly ApplicationDbContext _db;

        public AppSettingsService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<int> SessionLifetimeDaysAsync(CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.Key == SessionLifetimeKey, cancellationToken);
            if (row != null && int.TryParse(row.Value, out int days) && days >= MinSessionLifetimeDays && days <= MaxSessionLifetimeDays)
            {
                return days;
            }

            return DefaultSessionLifetimeDays;
        }

        public async Task SetSessionLifetimeDaysAsync(int days, CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings
                .FirstOrDefaultAsync(s => s.Key == SessionLifetimeKey, cancellationToken);
            if (row == null)
            {
                _db.AppSettings.Add(new AppSetting
                {
                    Key = SessionLifetimeKey,
                    Value = days.ToString(),
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Value = days.ToString();
                row.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public const string PollIntervalKey = "git_poll_interval_minutes";
        public const string WebhookPollIntervalKey = "git_webhook_poll_interval_minutes";
        public const string MaxCommitsKey = "project_max_commits_per_sync";
        public const string InitialTagsKey = "project_initial_tags";
        public const string LibraryNicknamesKey = "kicomlib_library_nicknames";
        public const string TrustedPartsEnabledKey = "trustedparts_enabled";
        public const string TrustedPartsCompanyIdKey = "trustedparts_company_id";
        public const string TrustedPartsApiKeyKey = "trustedparts_api_key";

        public const string DistributorLookupProviderKey = "distributor_lookup_provider";
        public const string MouserEnabledKey = "mouser_enabled";
        public const string MouserApiKeyKey = "mouser_api_key";
        public const string DigiKeyEnabledKey = "digikey_enabled";
        public const string DigiKeyClientIdKey = "digikey_client_id";
        public const string DigiKeyClientSecretKey = "digikey_client_secret";
        public const string DigiKeyLocaleKey = "digikey_locale";
        public const string FarnellEnabledKey = "farnell_enabled";
        public const string FarnellApiKeyKey = "farnell_api_key";
        public const string FarnellStoreKey = "farnell_store";
        public const string ArrowEnabledKey = "arrow_enabled";
        public const string ArrowLoginKey = "arrow_login";
        public const string ArrowApiKeyKey = "arrow_api_key";
        public const string TmeEnabledKey = "tme_enabled";
        public const string TmeTokenKey = "tme_token";
        public const string TmeAppSecretKey = "tme_app_secret";
        public const string TmeCountryKey = "tme_country";
        public const string TmeLanguageKey = "tme_language";

        public async Task<int> GetIntAsync(string key, int fallback, int min, int max, CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (row != null && int.TryParse(row.Value, out int value) && value >= min && value <= max)
            {
                return value;
            }

            return fallback;
        }

        public async Task<string> GetStringAsync(string key, string fallback, CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (row != null && !string.IsNullOrWhiteSpace(row.Value))
            {
                return row.Value;
            }

            return fallback;
        }

        public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken)
        {
            string? value = await FindAsync(key, cancellationToken);
            if (value != null && bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public async Task<string?> FindAsync(string key, CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            return row?.Value;
        }

        public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (row == null)
            {
                _db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            AppSetting? row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (row == null)
            {
                return;
            }

            _db.AppSettings.Remove(row);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
