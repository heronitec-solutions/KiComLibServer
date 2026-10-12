import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { AppHeaderComponent } from '../app-header/app-header.component';
import { AuthService, apiErrorMessage } from '../../services/auth.service';
import { SettingsService } from '../../services/settings.service';

@Component({
    selector: 'info-page',
    imports: [AppHeaderComponent],
    templateUrl: './info-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager
})
export class InfoPageComponent implements OnInit {
  version = '';
  httpLibraryVersion = '';
  apiVersion: number | null = null;
  schemaVersion: number | null = null;
  kicadVersion = '';
  versionLoading = false;
  versionError = '';

  constructor(
    public auth: AuthService,
    private settingsApi: SettingsService
  ) { }

  ngOnInit(): void {
    if (!this.auth.profile().isAdmin) {
      return;
    }

    this.versionLoading = true;
    this.settingsApi.get().subscribe({
      next: settings => {
        this.version = settings.version;
        this.httpLibraryVersion = settings.httpLibraryVersion;
        this.apiVersion = settings.apiVersion;
        this.schemaVersion = settings.schemaVersion;
        this.kicadVersion = settings.runner.kicad || '';
        this.versionLoading = false;
      },
      error: error => {
        this.versionError = apiErrorMessage(error, 'Could not load version information.');
        this.versionLoading = false;
      }
    });
  }
}
