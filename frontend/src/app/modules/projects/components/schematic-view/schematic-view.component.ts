import { Component, Input, OnChanges, OnDestroy, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subject } from 'rxjs';

import { apiErrorMessage } from '../../../../shared/api-error';
import { ProjectService } from '../../services/project.service';
import { SvgObjectUrls } from '../../services/svg-object-urls';
import { RevisionDetail } from '../../types/project';

@Component({
    selector: 'schematic-view',
    templateUrl: './schematic-view.component.html',
    styleUrl: './schematic-view.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class SchematicViewComponent implements OnChanges, OnDestroy {
  @Input() projectId = 0;
  @Input() sha = '';

  detail: RevisionDetail | null = null;
  page = '1';
  error = '';
  loading = false;
  previewSrc: string | null = null;
  images: SvgObjectUrls;

  private readonly destroyed$ = new Subject<void>();
  private currentUrl = '';

  constructor(
    private projects: ProjectService,
    http: HttpClient
  ) {
    this.images = new SvgObjectUrls(http, this.destroyed$, () => {
      this.previewSrc = this.images.src(this.currentUrl);
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['projectId'] || changes['sha']) && this.projectId && this.sha) {
      this.loadRevision();
    }
  }

  ngOnDestroy(): void {
    this.images.revoke();
    this.destroyed$.next();
    this.destroyed$.complete();
  }

  selectPage(page: string): void {
    this.page = page;
    this.loadImage();
  }

  private loadRevision(): void {
    this.loading = true;
    this.error = '';
    this.images.clear();
    this.previewSrc = null;
    this.projects.revision(this.projectId, this.sha).subscribe({
      next: detail => {
        this.detail = detail;
        this.loading = false;
        const first = detail.pages[0]?.page || '1';
        this.page = detail.pages.some(item => item.page === this.page) ? this.page : first;
        this.loadImage();
      },
      error: error => {
        this.detail = null;
        this.loading = false;
        this.error = apiErrorMessage(error, 'Could not load the schematic revision.');
      }
    });
  }

  private loadImage(): void {
    if (!this.projectId || !this.sha || !this.page) {
      return;
    }
    this.currentUrl = this.projects.pageUrl(this.projectId, this.sha, this.page);
    this.previewSrc = this.images.src(this.currentUrl);
    this.images.fetch(this.currentUrl, this.currentUrl);
  }
}
