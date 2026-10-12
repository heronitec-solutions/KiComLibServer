import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { apiErrorMessage } from '../../../../services/auth.service';
import {
  AD_PRESET,
  DirectoryGroupHit,
  DirectoryService,
  DirectorySettings,
  DirectoryUserHit,
  DirectoryWrite,
  LDAP_PRESET
} from '../../../../services/directory.service';

@Component({
    selector: 'directory-page',
    templateUrl: './directory-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DirectoryPageComponent implements OnInit {
  enabled = false;
  serverType = 'ldap';
  host = '';
  port = 389;
  encryption = 'starttls';
  allowUntrustedCertificate = false;
  bindDn = '';
  bindPassword = '';
  bindPasswordSet = false;
  userBaseDn = '';
  userFilter = LDAP_PRESET.userFilter;
  usernameAttribute = LDAP_PRESET.usernameAttribute;
  displayNameAttribute = LDAP_PRESET.displayNameAttribute;
  userIdAttribute = LDAP_PRESET.userIdAttribute;
  groupBaseDn = '';
  groupFilter = LDAP_PRESET.groupFilter;
  groupNameAttribute = LDAP_PRESET.groupNameAttribute;
  groupIdAttribute = LDAP_PRESET.groupIdAttribute;
  membershipAttribute = LDAP_PRESET.membershipAttribute;
  provisionOnLogin = true;
  configured = false;

  userQuery = '';
  groupQuery = '';
  userHits: DirectoryUserHit[] = [];
  groupHits: DirectoryGroupHit[] = [];
  selectedUsers = new Set<string>();
  selectedGroups = new Set<string>();
  importCanRead = true;
  importCanWrite = false;
  importCanReadWarehouse = false;
  importCanWriteWarehouse = false;
  importCanAdmin = false;

  isLoading = true;
  isSaving = false;
  isTesting = false;
  isSearchingUsers = false;
  isSearchingGroups = false;
  isImportingUsers = false;
  isImportingGroups = false;
  error = '';
  message = '';

  constructor(private directory: DirectoryService) { }

  ngOnInit(): void {
    this.directory.get().subscribe({
      next: settings => {
        this.apply(settings);
        this.isLoading = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load the directory connection.');
        this.isLoading = false;
      }
    });
  }

  applyLdap(): void {
    this.applyPreset(LDAP_PRESET);
    this.message = 'LDAP filters were filled in. Search bases were left as they are.';
    this.error = '';
  }

  applyActiveDirectory(): void {
    this.applyPreset(AD_PRESET);
    this.message = 'Active Directory filters were filled in. Search bases were left as they are. LDAPS usually uses port 636 and SSL.';
    this.error = '';
  }

  save(): void {
    this.isSaving = true;
    this.error = '';
    this.message = '';
    this.directory.save(this.body()).subscribe({
      next: settings => {
        this.apply(settings);
        this.bindPassword = '';
        this.message = 'Directory connection saved.';
        this.isSaving = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not save the directory connection.');
        this.isSaving = false;
      }
    });
  }

  test(): void {
    this.isTesting = true;
    this.error = '';
    this.message = '';
    this.directory.test(this.body()).subscribe({
      next: result => {
        this.message = result.message || 'Connected to the directory.';
        this.isTesting = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not connect to the directory.');
        this.isTesting = false;
      }
    });
  }

  searchUsers(): void {
    const query = this.userQuery.trim();
    if (!query) {
      return;
    }

    this.isSearchingUsers = true;
    this.error = '';
    this.message = '';
    this.directory.searchUsers(query).subscribe({
      next: hits => {
        this.userHits = hits;
        this.selectedUsers = new Set<string>();
        this.isSearchingUsers = false;
        if (!hits.length) {
          this.message = 'No directory users matched.';
        }
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not search the directory.');
        this.isSearchingUsers = false;
      }
    });
  }

  searchGroups(): void {
    const query = this.groupQuery.trim();
    if (!query) {
      return;
    }

    this.isSearchingGroups = true;
    this.error = '';
    this.message = '';
    this.directory.searchGroups(query).subscribe({
      next: hits => {
        this.groupHits = hits;
        this.selectedGroups = new Set<string>();
        this.isSearchingGroups = false;
        if (!hits.length) {
          this.message = 'No directory groups matched.';
        }
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not search the directory.');
        this.isSearchingGroups = false;
      }
    });
  }

  toggleUser(dn: string, checked: boolean): void {
    this.toggle(this.selectedUsers, dn, checked);
  }

  toggleGroup(dn: string, checked: boolean): void {
    this.toggle(this.selectedGroups, dn, checked);
  }

  importUsers(): void {
    const names = Array.from(this.selectedUsers);
    if (!names.length) {
      return;
    }

    this.isImportingUsers = true;
    this.error = '';
    this.message = '';
    this.directory.importUsers(names).subscribe({
      next: result => {
        this.isImportingUsers = false;
        this.message = this.importMessage('user', result.imported.length, result.errors);
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not import directory users.');
        this.isImportingUsers = false;
      }
    });
  }

  importGroups(): void {
    const names = Array.from(this.selectedGroups);
    if (!names.length) {
      return;
    }

    this.isImportingGroups = true;
    this.error = '';
    this.message = '';
    this.directory.importGroups(
      names,
      this.importCanRead || this.importCanWrite,
      this.importCanWrite,
      this.importCanAdmin,
      this.importCanReadWarehouse || this.importCanWriteWarehouse,
      this.importCanWriteWarehouse
    ).subscribe({
      next: result => {
        this.isImportingGroups = false;
        this.message = this.importMessage('group', result.imported.length, result.errors);
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not import directory groups.');
        this.isImportingGroups = false;
      }
    });
  }

  onImportWrite(checked: boolean): void {
    this.importCanWrite = checked;
    if (checked) {
      this.importCanRead = true;
    }
  }

  onImportWarehouseWrite(checked: boolean): void {
    this.importCanWriteWarehouse = checked;
    if (checked) {
      this.importCanReadWarehouse = true;
    }
  }

  private apply(settings: DirectorySettings): void {
    this.enabled = settings.enabled;
    this.serverType = settings.serverType || 'ldap';
    this.host = settings.host || '';
    this.port = settings.port || 389;
    this.encryption = settings.encryption || 'none';
    this.allowUntrustedCertificate = settings.allowUntrustedCertificate;
    this.bindDn = settings.bindDn || '';
    this.bindPasswordSet = settings.bindPasswordSet;
    this.userBaseDn = settings.userBaseDn || '';
    this.userFilter = settings.userFilter;
    this.usernameAttribute = settings.usernameAttribute;
    this.displayNameAttribute = settings.displayNameAttribute;
    this.userIdAttribute = settings.userIdAttribute;
    this.groupBaseDn = settings.groupBaseDn || '';
    this.groupFilter = settings.groupFilter;
    this.groupNameAttribute = settings.groupNameAttribute;
    this.groupIdAttribute = settings.groupIdAttribute;
    this.membershipAttribute = settings.membershipAttribute;
    this.provisionOnLogin = settings.provisionOnLogin;
    this.configured = settings.configured;
  }

  private applyPreset(preset: typeof LDAP_PRESET): void {
    this.serverType = preset.serverType;
    this.port = preset.port;
    this.encryption = preset.encryption;
    this.userFilter = preset.userFilter;
    this.usernameAttribute = preset.usernameAttribute;
    this.displayNameAttribute = preset.displayNameAttribute;
    this.userIdAttribute = preset.userIdAttribute;
    this.groupFilter = preset.groupFilter;
    this.groupNameAttribute = preset.groupNameAttribute;
    this.groupIdAttribute = preset.groupIdAttribute;
    this.membershipAttribute = preset.membershipAttribute;
  }

  private body(): DirectoryWrite {
    const body: DirectoryWrite = {
      enabled: this.enabled,
      serverType: this.serverType,
      host: this.host.trim(),
      port: Number(this.port),
      encryption: this.encryption,
      allowUntrustedCertificate: this.allowUntrustedCertificate,
      bindDn: this.bindDn.trim(),
      userBaseDn: this.userBaseDn.trim(),
      userFilter: this.userFilter.trim(),
      usernameAttribute: this.usernameAttribute.trim(),
      displayNameAttribute: this.displayNameAttribute.trim(),
      userIdAttribute: this.userIdAttribute.trim(),
      groupBaseDn: this.groupBaseDn.trim(),
      groupFilter: this.groupFilter.trim(),
      groupNameAttribute: this.groupNameAttribute.trim(),
      groupIdAttribute: this.groupIdAttribute.trim(),
      membershipAttribute: this.membershipAttribute.trim(),
      provisionOnLogin: this.provisionOnLogin
    };
    if (this.bindPassword) {
      body.bindPassword = this.bindPassword;
    }
    return body;
  }

  private toggle(selected: Set<string>, dn: string, checked: boolean): void {
    if (checked) {
      selected.add(dn);
    } else {
      selected.delete(dn);
    }
  }

  private importMessage(
    kind: 'user' | 'group',
    imported: number,
    errors: { distinguishedName: string; error: string }[]
  ): string {
    const label = kind === 'user' ? 'user' : 'group';
    const parts = [`Imported ${imported} ${label}${imported === 1 ? '' : 's'}.`];
    if (kind === 'user') {
      parts.push('Directory group membership is applied for groups that are already imported.');
    } else {
      parts.push('Existing groups keep their rights. New groups use the rights selected here.');
    }
    if (errors.length) {
      parts.push(errors.map(error => `${error.distinguishedName}: ${error.error}`).join(' '));
    }
    return parts.join(' ');
  }
}
