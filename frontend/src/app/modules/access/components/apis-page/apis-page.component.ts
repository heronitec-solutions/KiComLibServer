import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';

import { API_PROVIDERS, ApiProviderDefinition, ApiProviderId } from '../../api-providers';
import { apiErrorMessage } from '../../../../services/auth.service';
import { AppSettings, distributorWriteFrom, SettingsService } from '../../../../services/settings.service';
import { ApiSettingsDialogComponent, ApiSettingsDialogResult } from '../api-settings-dialog/api-settings-dialog.component';

interface ApiRow {
  id: ApiProviderId;
  label: string;
  kindLabel: string;
  detail: string;
  credentials: string;
  ready: boolean;
  isLookup: boolean;
}

@Component({
    selector: 'apis-page',
    templateUrl: './apis-page.component.html',
    styleUrl: './apis-page.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ApisPageComponent implements OnInit {
  readonly columns = ['name', 'kind', 'detail', 'credentials'];

  settings: AppSettings | null = null;
  lookupProvider = '';
  providerToAdd = '';
  isLoading = true;
  isSavingLookup = false;
  error = '';
  message = '';
  lookupError = '';

  constructor(
    private settingsApi: SettingsService,
    private dialog: MatDialog
  ) { }

  ngOnInit(): void {
    this.settingsApi.get().subscribe({
      next: settings => {
        this.apply(settings);
        this.isLoading = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load API settings.');
        this.isLoading = false;
      }
    });
  }

  get rows(): ApiRow[] {
    if (!this.settings) {
      return [];
    }
    return API_PROVIDERS.filter(provider => this.isEnabled(provider.id)).map(provider => this.toRow(provider));
  }

  get availableProviders(): ApiProviderDefinition[] {
    return API_PROVIDERS.filter(provider => !this.isEnabled(provider.id));
  }

  get lookupChoices(): { id: string; label: string }[] {
    const choices: { id: string; label: string }[] = API_PROVIDERS
      .filter(provider => provider.kind === 'distributor' && this.isEnabled(provider.id))
      .map(provider => ({ id: provider.id, label: provider.label }));
    if (this.lookupProvider && !choices.some(choice => choice.id === this.lookupProvider)) {
      const known = API_PROVIDERS.find(provider => provider.id === this.lookupProvider);
      choices.push({ id: this.lookupProvider, label: known?.label ?? this.lookupProvider });
    }
    return choices;
  }

  add(): void {
    const id = this.providerToAdd as ApiProviderId;
    if (!id || !this.availableProviders.some(provider => provider.id === id)) {
      return;
    }
    this.open(id, true);
  }

  edit(row: ApiRow): void {
    this.open(row.id, false);
  }

  saveLookup(value: string): void {
    if (!this.settings || value === this.lookupProvider) {
      return;
    }
    const previous = this.lookupProvider;
    this.lookupProvider = value;
    this.lookupError = '';
    this.isSavingLookup = true;
    const write = distributorWriteFrom(this.settings.distributors);
    write.lookupProvider = value.trim().toLowerCase();
    this.settingsApi.saveDistributors(write).subscribe({
      next: settings => {
        this.apply(settings);
        this.isSavingLookup = false;
      },
      error: error => {
        this.lookupProvider = previous;
        this.lookupError = apiErrorMessage(error, 'Could not save the lookup API.');
        this.isSavingLookup = false;
      }
    });
  }

  private open(id: ApiProviderId, adding: boolean): void {
    if (!this.settings) {
      return;
    }
    this.message = '';
    this.dialog.open(ApiSettingsDialogComponent, {
      width: '720px',
      data: { providerId: id, settings: this.settings, adding }
    }).afterClosed().subscribe((result: ApiSettingsDialogResult | undefined) => {
      if (!result) {
        return;
      }
      this.apply(result.settings);
      if (adding || result.removed) {
        this.providerToAdd = '';
      }
      const label = API_PROVIDERS.find(provider => provider.id === id)?.label ?? id;
      this.message = result.removed ? `${label} removed.` : `${label} saved.`;
    });
  }

  private apply(settings: AppSettings): void {
    this.settings = settings;
    this.lookupProvider = (settings.distributors.lookupProvider || '').trim().toLowerCase();
    if (this.providerToAdd && this.isEnabled(this.providerToAdd as ApiProviderId)) {
      this.providerToAdd = '';
    }
  }

  private toRow(provider: ApiProviderDefinition): ApiRow {
    const settings = this.settings!;
    const distributors = settings.distributors;
    let detail = '—';
    let credentials = 'Missing';
    let ready = false;
    switch (provider.id) {
      case 'trustedparts':
        detail = settings.trustedParts.companyId || 'No company ID';
        ready = !!settings.trustedParts.companyId && settings.trustedParts.apiKeySet;
        credentials = settings.trustedParts.apiKeySet ? 'Key saved' : 'No key';
        break;
      case 'mouser':
        ready = distributors.mouser.apiKeySet;
        credentials = ready ? 'Key saved' : 'No key';
        break;
      case 'digikey':
        detail = [distributors.digikey.clientId, distributors.digikey.locale].filter(part => !!part).join(' · ') || '—';
        ready = !!distributors.digikey.clientId && distributors.digikey.clientSecretSet;
        credentials = distributors.digikey.clientSecretSet ? 'Secret saved' : 'No secret';
        break;
      case 'farnell':
        detail = distributors.farnell.store || '—';
        ready = distributors.farnell.apiKeySet;
        credentials = ready ? 'Key saved' : 'No key';
        break;
      case 'arrow':
        detail = distributors.arrow.login || 'No login';
        ready = !!distributors.arrow.login && distributors.arrow.apiKeySet;
        credentials = distributors.arrow.apiKeySet ? 'Key saved' : 'No key';
        break;
      case 'tme':
        detail = [distributors.tme.country, distributors.tme.language].filter(part => !!part).join(' · ') || '—';
        ready = distributors.tme.tokenSet && distributors.tme.appSecretSet;
        credentials = ready ? 'Credentials saved' : 'Incomplete';
        break;
    }
    return {
      id: provider.id,
      label: provider.label,
      kindLabel: provider.kindLabel,
      detail,
      credentials,
      ready,
      isLookup: provider.kind === 'distributor' && this.lookupProvider === provider.id
    };
  }

  private isEnabled(id: ApiProviderId): boolean {
    if (!this.settings) {
      return false;
    }
    switch (id) {
      case 'trustedparts': return this.settings.trustedParts.enabled;
      case 'mouser': return this.settings.distributors.mouser.enabled;
      case 'digikey': return this.settings.distributors.digikey.enabled;
      case 'farnell': return this.settings.distributors.farnell.enabled;
      case 'arrow': return this.settings.distributors.arrow.enabled;
      case 'tme': return this.settings.distributors.tme.enabled;
      default: return false;
    }
  }
}
