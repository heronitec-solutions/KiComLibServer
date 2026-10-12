import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { ApiProviderDefinition, ApiProviderId, apiProvider } from '../../api-providers';
import { apiErrorMessage } from '../../../../services/auth.service';
import {
  AppSettings,
  canonicalFarnellStore,
  DistributorSettingsWrite,
  distributorWriteFrom,
  FARNELL_STORES,
  SettingsService
} from '../../../../services/settings.service';

export interface ApiSettingsDialogData {
  providerId: ApiProviderId;
  settings: AppSettings;
  adding: boolean;
}

export interface ApiSettingsDialogResult {
  settings: AppSettings;
  removed: boolean;
}

@Component({
    selector: 'api-settings-dialog',
    templateUrl: './api-settings-dialog.component.html',
    styleUrl: './api-settings-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ApiSettingsDialogComponent {
  readonly farnellStores = FARNELL_STORES;
  readonly provider: ApiProviderDefinition;

  mouserApiKey = '';
  mouserApiKeySet = false;
  mouserClearApiKey = false;

  digikeyClientId = '';
  digikeyClientSecret = '';
  digikeyClientSecretSet = false;
  digikeyClearClientSecret = false;
  digikeyLocale = 'en';

  farnellApiKey = '';
  farnellApiKeySet = false;
  farnellClearApiKey = false;
  farnellStore = 'de.farnell.com';

  arrowLogin = '';
  arrowApiKey = '';
  arrowApiKeySet = false;
  arrowClearApiKey = false;

  tmeToken = '';
  tmeTokenSet = false;
  tmeClearToken = false;
  tmeAppSecret = '';
  tmeAppSecretSet = false;
  tmeClearAppSecret = false;
  tmeCountry = 'DE';
  tmeLanguage = 'EN';

  companyId = '';
  apiKey = '';
  apiKeySet = false;
  clearApiKey = false;

  isSaving = false;
  isTesting = false;
  error = '';
  testMessage = '';

  constructor(
    private settingsApi: SettingsService,
    private dialogRef: MatDialogRef<ApiSettingsDialogComponent, ApiSettingsDialogResult>,
    @Inject(MAT_DIALOG_DATA) public data: ApiSettingsDialogData
  ) {
    const provider = apiProvider(data.providerId);
    if (!provider) {
      throw new Error(`Unknown API provider: ${data.providerId}`);
    }
    this.provider = provider;
    this.apply(data.settings);
  }

  close(): void {
    if (!this.isSaving) {
      this.dialogRef.close();
    }
  }

  save(): void {
    const problem = this.validate(true);
    if (problem) {
      this.reject(problem);
      return;
    }
    this.persist(true);
  }

  remove(): void {
    this.persist(false);
  }

  test(): void {
    if (this.provider.kind !== 'distributor') {
      return;
    }
    const missing = this.credentialsForTest();
    if (missing) {
      this.reject(missing);
      return;
    }

    this.isTesting = true;
    this.error = '';
    this.testMessage = '';
    this.settingsApi.testDistributor(this.provider.id, this.buildDistributorWrite(true)).subscribe({
      next: result => {
        this.testMessage = result.message || 'Connected.';
        this.isTesting = false;
      },
      error: error => {
        this.reject(apiErrorMessage(error, 'Could not reach this API.'));
        this.isTesting = false;
      }
    });
  }

  requestMouserClear(): void {
    this.mouserClearApiKey = !this.mouserClearApiKey;
    if (this.mouserClearApiKey) {
      this.mouserApiKey = '';
    }
    this.error = '';
  }

  requestDigiKeyClear(): void {
    this.digikeyClearClientSecret = !this.digikeyClearClientSecret;
    if (this.digikeyClearClientSecret) {
      this.digikeyClientSecret = '';
    }
    this.error = '';
  }

  requestFarnellClear(): void {
    this.farnellClearApiKey = !this.farnellClearApiKey;
    if (this.farnellClearApiKey) {
      this.farnellApiKey = '';
    }
    this.error = '';
  }

  requestArrowClear(): void {
    this.arrowClearApiKey = !this.arrowClearApiKey;
    if (this.arrowClearApiKey) {
      this.arrowApiKey = '';
    }
    this.error = '';
  }

  requestTmeTokenClear(): void {
    this.tmeClearToken = !this.tmeClearToken;
    if (this.tmeClearToken) {
      this.tmeToken = '';
    }
    this.error = '';
  }

  requestTmeSecretClear(): void {
    this.tmeClearAppSecret = !this.tmeClearAppSecret;
    if (this.tmeClearAppSecret) {
      this.tmeAppSecret = '';
    }
    this.error = '';
  }

  requestTrustedPartsClear(): void {
    this.clearApiKey = !this.clearApiKey;
    if (this.clearApiKey) {
      this.apiKey = '';
    }
    this.error = '';
  }

  private persist(enabled: boolean): void {
    if (enabled) {
      const problem = this.validate(true);
      if (problem) {
        this.reject(problem);
        return;
      }
    }

    this.isSaving = true;
    this.error = '';
    this.testMessage = '';
    const request = this.provider.id === 'trustedparts'
      ? this.settingsApi.saveTrustedParts({
        enabled,
        companyId: this.companyId.trim(),
        apiKeySet: this.apiKeySet,
        apiKey: this.clearApiKey ? '' : this.apiKey.trim(),
        clearApiKey: this.clearApiKey
      })
      : this.settingsApi.saveDistributors(this.buildDistributorWrite(enabled));

    request.subscribe({
      next: settings => {
        this.isSaving = false;
        this.dialogRef.close({ settings, removed: !enabled });
      },
      error: error => {
        this.isSaving = false;
        this.reject(apiErrorMessage(error, enabled
          ? 'Could not save API settings.'
          : 'Could not remove this API.'));
      }
    });
  }

  private validate(enabled: boolean): string | null {
    if (!enabled) {
      return null;
    }
    switch (this.provider.id) {
      case 'trustedparts':
        return this.validateTrustedParts();
      case 'mouser':
        if (this.mouserClearApiKey) {
          return 'Remove Mouser before deleting the saved API key.';
        }
        return this.secretReady(this.mouserApiKey, this.mouserApiKeySet, this.mouserClearApiKey)
          ? null
          : 'Enter a Mouser API key.';
      case 'digikey':
        if (this.digikeyClearClientSecret) {
          return 'Remove DigiKey before deleting the saved client secret.';
        }
        return this.digikeyClientId.trim() && this.secretReady(this.digikeyClientSecret, this.digikeyClientSecretSet, this.digikeyClearClientSecret)
          ? null
          : 'Enter a DigiKey client ID and client secret.';
      case 'farnell':
        if (this.farnellClearApiKey) {
          return 'Remove Farnell before deleting the saved API key.';
        }
        return this.secretReady(this.farnellApiKey, this.farnellApiKeySet, this.farnellClearApiKey)
          ? null
          : 'Enter a Farnell API key.';
      case 'arrow':
        if (this.arrowClearApiKey) {
          return 'Remove Arrow before deleting the saved API key.';
        }
        return this.arrowLogin.trim() && this.secretReady(this.arrowApiKey, this.arrowApiKeySet, this.arrowClearApiKey)
          ? null
          : 'Enter an Arrow login and API key.';
      case 'tme':
        if (this.tmeClearToken) {
          return 'Remove TME before deleting the saved token.';
        }
        if (this.tmeClearAppSecret) {
          return 'Remove TME before deleting the saved app secret.';
        }
        return this.secretReady(this.tmeToken, this.tmeTokenSet, this.tmeClearToken)
          && this.secretReady(this.tmeAppSecret, this.tmeAppSecretSet, this.tmeClearAppSecret)
          ? null
          : 'Enter a TME token and app secret.';
      default:
        return 'Unknown API provider.';
    }
  }

  private validateTrustedParts(): string | null {
    const companyId = this.companyId.trim();
    const apiKey = this.clearApiKey ? '' : this.apiKey.trim();
    if (companyId.length > 100) {
      return 'Company ID must be at most 100 characters.';
    }
    if (apiKey.length > 500) {
      return 'API key must be at most 500 characters.';
    }
    if (this.clearApiKey) {
      return 'Remove TrustedParts before deleting the saved API key.';
    }
    if (!companyId || !this.secretReady(apiKey, this.apiKeySet, this.clearApiKey)) {
      return 'Enter a company ID and API key.';
    }
    return null;
  }

  private credentialsForTest(): string | null {
    switch (this.provider.id) {
      case 'mouser':
        return this.secretReady(this.mouserApiKey, this.mouserApiKeySet, this.mouserClearApiKey)
          ? null
          : 'Enter a Mouser API key before testing.';
      case 'digikey':
        return this.digikeyClientId.trim() && this.secretReady(this.digikeyClientSecret, this.digikeyClientSecretSet, this.digikeyClearClientSecret)
          ? null
          : 'Enter a DigiKey client ID and client secret before testing.';
      case 'farnell':
        return this.secretReady(this.farnellApiKey, this.farnellApiKeySet, this.farnellClearApiKey)
          ? null
          : 'Enter a Farnell API key before testing.';
      case 'arrow':
        return this.arrowLogin.trim() && this.secretReady(this.arrowApiKey, this.arrowApiKeySet, this.arrowClearApiKey)
          ? null
          : 'Enter an Arrow login and API key before testing.';
      case 'tme':
        return this.secretReady(this.tmeToken, this.tmeTokenSet, this.tmeClearToken)
          && this.secretReady(this.tmeAppSecret, this.tmeAppSecretSet, this.tmeClearAppSecret)
          ? null
          : 'Enter a TME token and app secret before testing.';
      default:
        return 'Unknown API provider.';
    }
  }

  private buildDistributorWrite(enabled: boolean): DistributorSettingsWrite {
    const write = distributorWriteFrom(this.data.settings.distributors);
    const id = this.provider.id;
    if (id === 'mouser') {
      const apiKey = this.optionalSecret(this.mouserApiKey, this.mouserClearApiKey);
      write.mouser = {
        enabled,
        ...(apiKey ? { apiKey } : {}),
        ...(this.mouserClearApiKey ? { clearApiKey: true } : {})
      };
    } else if (id === 'digikey') {
      const clientSecret = this.optionalSecret(this.digikeyClientSecret, this.digikeyClearClientSecret);
      write.digikey = {
        enabled,
        clientId: this.digikeyClientId.trim(),
        locale: this.filled(this.digikeyLocale, 'en'),
        ...(clientSecret ? { clientSecret } : {}),
        ...(this.digikeyClearClientSecret ? { clearClientSecret: true } : {})
      };
    } else if (id === 'farnell') {
      const apiKey = this.optionalSecret(this.farnellApiKey, this.farnellClearApiKey);
      write.farnell = {
        enabled,
        store: canonicalFarnellStore(this.farnellStore),
        ...(apiKey ? { apiKey } : {}),
        ...(this.farnellClearApiKey ? { clearApiKey: true } : {})
      };
    } else if (id === 'arrow') {
      const apiKey = this.optionalSecret(this.arrowApiKey, this.arrowClearApiKey);
      write.arrow = {
        enabled,
        login: this.arrowLogin.trim(),
        ...(apiKey ? { apiKey } : {}),
        ...(this.arrowClearApiKey ? { clearApiKey: true } : {})
      };
    } else if (id === 'tme') {
      const token = this.optionalSecret(this.tmeToken, this.tmeClearToken);
      const appSecret = this.optionalSecret(this.tmeAppSecret, this.tmeClearAppSecret);
      write.tme = {
        enabled,
        country: this.filled(this.tmeCountry, 'DE'),
        language: this.filled(this.tmeLanguage, 'EN'),
        ...(token ? { token } : {}),
        ...(this.tmeClearToken ? { clearToken: true } : {}),
        ...(appSecret ? { appSecret } : {}),
        ...(this.tmeClearAppSecret ? { clearAppSecret: true } : {})
      };
    }
    if (!enabled && write.lookupProvider === id) {
      write.lookupProvider = '';
    }
    return write;
  }

  private apply(settings: AppSettings): void {
    const distributors = settings.distributors;
    this.mouserApiKeySet = distributors.mouser.apiKeySet;
    this.digikeyClientId = distributors.digikey.clientId;
    this.digikeyClientSecretSet = distributors.digikey.clientSecretSet;
    this.digikeyLocale = distributors.digikey.locale || 'en';
    this.farnellApiKeySet = distributors.farnell.apiKeySet;
    this.farnellStore = canonicalFarnellStore(distributors.farnell.store);
    this.arrowLogin = distributors.arrow.login;
    this.arrowApiKeySet = distributors.arrow.apiKeySet;
    this.tmeTokenSet = distributors.tme.tokenSet;
    this.tmeAppSecretSet = distributors.tme.appSecretSet;
    this.tmeCountry = distributors.tme.country || 'DE';
    this.tmeLanguage = distributors.tme.language || 'EN';
    this.companyId = settings.trustedParts.companyId;
    this.apiKeySet = settings.trustedParts.apiKeySet;
  }

  private secretReady(value: string, alreadySet: boolean, clear: boolean): boolean {
    if (clear) {
      return false;
    }
    return value.trim().length > 0 || alreadySet;
  }

  private optionalSecret(value: string, clear: boolean): string | undefined {
    if (clear) {
      return undefined;
    }
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : undefined;
  }

  private filled(value: string, fallback: string): string {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : fallback;
  }

  private reject(message: string): void {
    this.error = message;
    this.testMessage = '';
  }
}
