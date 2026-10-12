import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { appEnvironment } from '../app-environment';
import { asRecord, readBool, readNumber, readString } from '../shared/json-read';

export interface RunnerStatus {
  reachable: boolean;
  kicad: string | null;
}

export interface TrustedPartsSettings {
  enabled: boolean;
  companyId: string;
  apiKeySet: boolean;
}

export interface MouserDistributorSettings {
  enabled: boolean;
  apiKeySet: boolean;
}

export interface DigiKeyDistributorSettings {
  enabled: boolean;
  clientId: string;
  clientSecretSet: boolean;
  locale: string;
}

export interface FarnellDistributorSettings {
  enabled: boolean;
  apiKeySet: boolean;
  store: string;
}

export interface ArrowDistributorSettings {
  enabled: boolean;
  login: string;
  apiKeySet: boolean;
}

export interface TmeDistributorSettings {
  enabled: boolean;
  tokenSet: boolean;
  appSecretSet: boolean;
  country: string;
  language: string;
}

export interface DistributorSettings {
  lookupProvider: string;
  mouser: MouserDistributorSettings;
  digikey: DigiKeyDistributorSettings;
  farnell: FarnellDistributorSettings;
  arrow: ArrowDistributorSettings;
  tme: TmeDistributorSettings;
}

export const FARNELL_STORES = ['uk.farnell.com', 'de.farnell.com', 'www.newark.com', 'sg.element14.com'] as const;

export function canonicalFarnellStore(value: string): string {
  const trimmed = (value || '').trim().toLowerCase();
  if (trimmed === 'newark.com') {
    return 'www.newark.com';
  }
  return FARNELL_STORES.find(store => store === trimmed) ?? 'de.farnell.com';
}

/** Write payload that keeps stored secrets and the current enabled flags. */
export function distributorWriteFrom(settings: DistributorSettings): DistributorSettingsWrite {
  return {
    lookupProvider: (settings.lookupProvider || '').trim().toLowerCase(),
    mouser: { enabled: settings.mouser.enabled },
    digikey: {
      enabled: settings.digikey.enabled,
      clientId: settings.digikey.clientId.trim(),
      locale: settings.digikey.locale.trim() || 'en'
    },
    farnell: {
      enabled: settings.farnell.enabled,
      store: canonicalFarnellStore(settings.farnell.store)
    },
    arrow: {
      enabled: settings.arrow.enabled,
      login: settings.arrow.login.trim()
    },
    tme: {
      enabled: settings.tme.enabled,
      country: settings.tme.country.trim() || 'DE',
      language: settings.tme.language.trim() || 'EN'
    }
  };
}

export interface DistributorTestResult {
  ok: boolean;
  provider: string;
  message: string;
}

export interface DistributorSettingsWrite {
  lookupProvider: string;
  mouser: { enabled: boolean; apiKey?: string; clearApiKey?: boolean };
  digikey: { enabled: boolean; clientId: string; clientSecret?: string; clearClientSecret?: boolean; locale: string };
  farnell: { enabled: boolean; apiKey?: string; clearApiKey?: boolean; store: string };
  arrow: { enabled: boolean; login: string; apiKey?: string; clearApiKey?: boolean };
  tme: {
    enabled: boolean;
    token?: string;
    clearToken?: boolean;
    appSecret?: string;
    clearAppSecret?: boolean;
    country: string;
    language: string;
  };
}

export interface AppSettingsWrite {
  sessionLifetimeDays: number;
  gitPollIntervalMinutes: number;
  gitWebhookPollIntervalMinutes: number;
  projectMaxCommitsPerSync: number;
  projectInitialTags: number;
  kicomlibLibraryNicknames: string;
}

export interface AppSettings {
  sessionLifetimeDays: number;
  version: string;
  httpLibraryVersion: string;
  apiVersion: number;
  schemaVersion: number;
  gitPollIntervalMinutes: number;
  gitWebhookPollIntervalMinutes: number;
  projectMaxCommitsPerSync: number;
  projectInitialTags: number;
  kicomlibLibraryNicknames: string;
  trustedParts: TrustedPartsSettings;
  distributors: DistributorSettings;
  runner: RunnerStatus;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private readonly apiUrl = appEnvironment.apiUrl;

  constructor(private http: HttpClient) { }

  get(): Observable<AppSettings> {
    return this.http.get<unknown>(`${this.apiUrl}/settings`).pipe(
      map(body => normalizeSettings(body))
    );
  }

  save(settings: AppSettingsWrite): Observable<AppSettings> {
    return this.http.put<unknown>(`${this.apiUrl}/settings`, {
      sessionLifetimeDays: settings.sessionLifetimeDays,
      gitPollIntervalMinutes: settings.gitPollIntervalMinutes,
      gitWebhookPollIntervalMinutes: settings.gitWebhookPollIntervalMinutes,
      projectMaxCommitsPerSync: settings.projectMaxCommitsPerSync,
      projectInitialTags: settings.projectInitialTags,
      kicomlibLibraryNicknames: settings.kicomlibLibraryNicknames
    }).pipe(map(body => normalizeSettings(body)));
  }

  saveTrustedParts(settings: TrustedPartsSettings & { apiKey?: string; clearApiKey?: boolean }): Observable<AppSettings> {
    return this.http.put<unknown>(`${this.apiUrl}/settings/trustedparts`, {
      enabled: settings.enabled,
      companyId: settings.companyId,
      apiKey: settings.apiKey?.trim() ? settings.apiKey.trim() : null,
      clearApiKey: settings.clearApiKey === true
    }).pipe(map(body => normalizeSettings(body)));
  }

  saveDistributors(settings: DistributorSettingsWrite): Observable<AppSettings> {
    return this.http.put<unknown>(`${this.apiUrl}/settings/distributors`, distributorPayload(settings)).pipe(
      map(body => normalizeSettings(body))
    );
  }

  testDistributor(provider: string, settings: DistributorSettingsWrite): Observable<DistributorTestResult> {
    const id = provider.trim().toLowerCase();
    return this.http.post<unknown>(`${this.apiUrl}/settings/distributors/test/${encodeURIComponent(id)}`, distributorPayload(settings)).pipe(
      map(body => {
        const row = asRecord(body) ?? {};
        return {
          ok: readBool(row, 'ok'),
          provider: readString(row, 'provider') || id,
          message: readString(row, 'message') || 'Connected.'
        };
      })
    );
  }
}

function distributorPayload(settings: DistributorSettingsWrite): unknown {
  return {
    lookupProvider: settings.lookupProvider.trim(),
    mouser: {
      enabled: settings.mouser.enabled,
      apiKey: settings.mouser.apiKey?.trim() ? settings.mouser.apiKey.trim() : null,
      clearApiKey: settings.mouser.clearApiKey === true
    },
    digikey: {
      enabled: settings.digikey.enabled,
      clientId: settings.digikey.clientId.trim(),
      clientSecret: settings.digikey.clientSecret?.trim() ? settings.digikey.clientSecret.trim() : null,
      clearClientSecret: settings.digikey.clearClientSecret === true,
      locale: settings.digikey.locale.trim()
    },
    farnell: {
      enabled: settings.farnell.enabled,
      apiKey: settings.farnell.apiKey?.trim() ? settings.farnell.apiKey.trim() : null,
      clearApiKey: settings.farnell.clearApiKey === true,
      store: settings.farnell.store.trim()
    },
    arrow: {
      enabled: settings.arrow.enabled,
      login: settings.arrow.login.trim(),
      apiKey: settings.arrow.apiKey?.trim() ? settings.arrow.apiKey.trim() : null,
      clearApiKey: settings.arrow.clearApiKey === true
    },
    tme: {
      enabled: settings.tme.enabled,
      token: settings.tme.token?.trim() ? settings.tme.token.trim() : null,
      clearToken: settings.tme.clearToken === true,
      appSecret: settings.tme.appSecret?.trim() ? settings.tme.appSecret.trim() : null,
      clearAppSecret: settings.tme.clearAppSecret === true,
      country: settings.tme.country.trim(),
      language: settings.tme.language.trim()
    }
  };
}

function normalizeDistributors(value: unknown): DistributorSettings {
  const row = asRecord(value) ?? {};
  const mouser = asRecord(row['mouser']) ?? {};
  const digikey = asRecord(row['digikey']) ?? asRecord(row['digiKey']) ?? {};
  const farnell = asRecord(row['farnell']) ?? {};
  const arrow = asRecord(row['arrow']) ?? {};
  const tme = asRecord(row['tme']) ?? {};
  return {
    lookupProvider: readString(row, 'lookupProvider', 'lookup_provider'),
    mouser: {
      enabled: readBool(mouser, 'enabled'),
      apiKeySet: readBool(mouser, 'apiKeySet', 'api_key_set')
    },
    digikey: {
      enabled: readBool(digikey, 'enabled'),
      clientId: readString(digikey, 'clientId', 'client_id'),
      clientSecretSet: readBool(digikey, 'clientSecretSet', 'client_secret_set'),
      locale: readString(digikey, 'locale') || 'en'
    },
    farnell: {
      enabled: readBool(farnell, 'enabled'),
      apiKeySet: readBool(farnell, 'apiKeySet', 'api_key_set'),
      store: readString(farnell, 'store') || 'de.farnell.com'
    },
    arrow: {
      enabled: readBool(arrow, 'enabled'),
      login: readString(arrow, 'login'),
      apiKeySet: readBool(arrow, 'apiKeySet', 'api_key_set')
    },
    tme: {
      enabled: readBool(tme, 'enabled'),
      tokenSet: readBool(tme, 'tokenSet', 'token_set'),
      appSecretSet: readBool(tme, 'appSecretSet', 'app_secret_set'),
      country: readString(tme, 'country') || 'DE',
      language: readString(tme, 'language') || 'EN'
    }
  };
}

function normalizeTrustedParts(value: unknown): TrustedPartsSettings {
  const row = asRecord(value) ?? {};
  return {
    enabled: readBool(row, 'enabled'),
    companyId: readString(row, 'companyId', 'company_id'),
    apiKeySet: readBool(row, 'apiKeySet', 'api_key_set')
  };
}

function numberOr(row: Record<string, unknown>, fallback: number, ...keys: string[]): number {
  for (const key of keys) {
    if (row[key] == null || row[key] === '') {
      continue;
    }
    return readNumber(row, key);
  }
  return fallback;
}

function normalizeSettings(body: unknown): AppSettings {
  const row = asRecord(body) ?? {};
  const runner = asRecord(row['runner']);
  const kicad = runner ? (readString(runner, 'kicad', 'kicadVersion', 'version') || null) : null;
  return {
    sessionLifetimeDays: numberOr(row, 30, 'sessionLifetimeDays'),
    version: readString(row, 'version'),
    httpLibraryVersion: readString(row, 'httpLibraryVersion'),
    apiVersion: readNumber(row, 'apiVersion'),
    schemaVersion: readNumber(row, 'schemaVersion'),
    gitPollIntervalMinutes: numberOr(row, 5, 'gitPollIntervalMinutes', 'git_poll_interval_minutes'),
    gitWebhookPollIntervalMinutes: numberOr(row, 60, 'gitWebhookPollIntervalMinutes', 'git_webhook_poll_interval_minutes'),
    projectMaxCommitsPerSync: numberOr(row, 20, 'projectMaxCommitsPerSync', 'project_max_commits_per_sync'),
    projectInitialTags: numberOr(row, 50, 'projectInitialTags', 'project_initial_tags'),
    kicomlibLibraryNicknames: readString(row, 'kicomlibLibraryNicknames', 'kicomlib_library_nicknames') || 'KiComLib',
    trustedParts: normalizeTrustedParts(row['trustedParts']),
    distributors: normalizeDistributors(row['distributors']),
    runner: {
      reachable: runner ? readBool(runner, 'reachable') : false,
      kicad
    }
  };
}
