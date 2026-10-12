import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { apiErrorMessage } from '../../../../services/auth.service';
import { AppSettings, SettingsService } from '../../../../services/settings.service';

@Component({
    selector: 'settings-page',
    templateUrl: './settings-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class SettingsPageComponent implements OnInit {
  sessionLifetimeDays = 30;
  gitPollIntervalMinutes = 5;
  gitWebhookPollIntervalMinutes = 60;
  projectMaxCommitsPerSync = 20;
  projectInitialTags = 50;
  kicomlibLibraryNicknames = 'KiComLib';
  runnerReachable = false;
  runnerKicad = '';
  version = '';
  httpLibraryVersion = '';
  apiVersion: number | null = null;
  schemaVersion: number | null = null;
  isLoading = true;
  isSaving = false;
  error = '';
  message = '';

  constructor(private settingsApi: SettingsService) { }

  ngOnInit(): void {
    this.settingsApi.get().subscribe({
      next: settings => {
        this.apply(settings);
        this.isLoading = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load settings.');
        this.isLoading = false;
      }
    });
  }

  save(): void {
    const days = Number(this.sessionLifetimeDays);
    const gitPoll = Number(this.gitPollIntervalMinutes);
    const webhookPoll = Number(this.gitWebhookPollIntervalMinutes);
    const maxCommits = Number(this.projectMaxCommitsPerSync);
    const initialTags = Number(this.projectInitialTags);
    const nicknames = this.kicomlibLibraryNicknames.trim();
    if (!Number.isInteger(days) || days < 1 || days > 365) {
      this.error = 'Session lifetime must be a whole number of days from 1 to 365.';
      this.message = '';
      return;
    }
    if (!Number.isInteger(gitPoll) || gitPoll < 1 || !Number.isInteger(webhookPoll) || webhookPoll < 1) {
      this.error = 'Poll intervals must be whole numbers of minutes, at least 1.';
      this.message = '';
      return;
    }
    if (!Number.isInteger(maxCommits) || maxCommits < 1 || !Number.isInteger(initialTags) || initialTags < 0) {
      this.error = 'Commit and tag limits must be whole numbers. The commit limit is at least 1.';
      this.message = '';
      return;
    }
    if (!nicknames) {
      this.error = 'Enter at least one KiComLib library nickname.';
      this.message = '';
      return;
    }

    this.isSaving = true;
    this.error = '';
    this.message = '';
    this.settingsApi.save({
      sessionLifetimeDays: days,
      gitPollIntervalMinutes: gitPoll,
      gitWebhookPollIntervalMinutes: webhookPoll,
      projectMaxCommitsPerSync: maxCommits,
      projectInitialTags: initialTags,
      kicomlibLibraryNicknames: nicknames
    }).subscribe({
      next: settings => {
        this.apply(settings);
        this.message = 'Settings saved. New sign-ins use this lifetime. Sessions that are already open keep their current expiry.';
        this.isSaving = false;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not save settings.');
        this.isSaving = false;
      }
    });
  }

  private apply(settings: AppSettings): void {
    this.sessionLifetimeDays = settings.sessionLifetimeDays;
    this.gitPollIntervalMinutes = settings.gitPollIntervalMinutes;
    this.gitWebhookPollIntervalMinutes = settings.gitWebhookPollIntervalMinutes;
    this.projectMaxCommitsPerSync = settings.projectMaxCommitsPerSync;
    this.projectInitialTags = settings.projectInitialTags;
    this.kicomlibLibraryNicknames = settings.kicomlibLibraryNicknames;
    this.version = settings.version;
    this.httpLibraryVersion = settings.httpLibraryVersion;
    this.apiVersion = settings.apiVersion;
    this.schemaVersion = settings.schemaVersion;
    this.runnerReachable = settings.runner.reachable;
    this.runnerKicad = settings.runner.kicad || '';
  }
}
