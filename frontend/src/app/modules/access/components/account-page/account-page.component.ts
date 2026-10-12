import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { apiErrorMessage, ApiTokenInfo, AuthService } from '../../../../services/auth.service';

@Component({
    selector: 'account-page',
    templateUrl: './account-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class AccountPageComponent implements OnInit {
  tokens: ApiTokenInfo[] = [];
  tokenName = '';
  tokenCanWrite = true;
  createdToken = '';
  currentPassword = '';
  newPassword = '';
  confirmPassword = '';
  message = '';
  error = '';
  isLoadingTokens = false;

  readonly columns = ['name', 'prefix', 'access', 'createdAt', 'expiresAt', 'lastUsedAt', 'actions'];

  constructor(
    public auth: AuthService
  ) { }

  ngOnInit(): void {
    if (!this.auth.profile().mustChangePassword) {
      this.loadTokens();
    }
  }

  loadTokens(): void {
    this.isLoadingTokens = true;
    this.auth.getTokens().subscribe({
      next: tokens => {
        this.tokens = tokens;
        this.isLoadingTokens = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load API tokens');
        this.isLoadingTokens = false;
      }
    });
  }

  createToken(): void {
    const name = this.tokenName.trim();
    if (!name) {
      return;
    }

    this.error = '';
    this.message = '';
    this.auth.createToken(name, this.auth.canWrite() && this.tokenCanWrite).subscribe({
      next: created => {
        this.createdToken = created.token || '';
        this.tokenName = '';
        this.loadTokens();
      },
      error: error => this.error = apiErrorMessage(error, 'Could not create the token')
    });
  }

  copyToken(): void {
    if (!this.createdToken || !navigator.clipboard) {
      return;
    }
    void navigator.clipboard.writeText(this.createdToken);
    this.message = 'Token copied.';
  }

  revoke(token: ApiTokenInfo): void {
    this.auth.revokeToken(token.id).subscribe({
      next: () => {
        if (this.createdToken.startsWith(token.prefix)) {
          this.createdToken = '';
        }
        this.loadTokens();
      },
      error: error => this.error = apiErrorMessage(error, 'Could not revoke the token')
    });
  }

  passwordsMatch(): boolean {
    return this.newPassword.length >= 12 && this.newPassword === this.confirmPassword && !this.isInstalledDefault();
  }

  isInstalledDefault(): boolean {
    return this.newPassword.trim() === 'kicomlib';
  }

  changePassword(): void {
    this.error = '';
    this.message = '';
    if (this.isInstalledDefault()) {
      this.error = 'kicomlib is the installed default password and cannot be used.';
      return;
    }
    if (!this.passwordsMatch()) {
      this.error = 'The new passwords do not match.';
      return;
    }

    this.auth.changePassword(this.currentPassword, this.newPassword).subscribe({
      next: () => {
        this.currentPassword = '';
        this.newPassword = '';
        this.confirmPassword = '';
        this.message = 'Password updated.';
      },
      error: error => this.error = apiErrorMessage(error, 'Could not change the password')
    });
  }

  groupNames(): string {
    const groups = this.auth.profile().groups || [];
    return groups.length ? groups.map(group => group.name).join(', ') : 'No group';
  }
}
