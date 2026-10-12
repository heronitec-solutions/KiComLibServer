import { Component, ChangeDetectionStrategy } from '@angular/core';

import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';

import { AuthService } from '../../services/auth.service';

@Component({
    selector: 'account-box',
    imports: [
    MatIconModule,
    MatMenuModule,
    MatDividerModule
],
    templateUrl: './account-box.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './account-box.component.scss'
})
export class AccountBoxComponent {
  constructor(
    public auth: AuthService,
    private router: Router
  ) { }

  showReadOnlyBadge(): boolean {
    return !this.auth.canWrite() && !this.auth.canWriteWarehouse();
  }

  catalogAccess(): string {
    const profile = this.auth.profile();
    if (profile.canWrite) {
      return 'Read and write';
    }
    return profile.canRead ? 'Read only' : 'No catalog access';
  }

  warehouseAccess(): string {
    if (this.auth.canWriteWarehouse()) {
      return 'Read and write';
    }
    return this.auth.canReadWarehouse() ? 'Read only' : 'No warehouse access';
  }

  openAccount(): void {
    void this.router.navigate(['/account']);
  }

  logout(): void {
    this.auth.logout().subscribe(() => {
      void this.router.navigate(['/login']);
    });
  }
}
