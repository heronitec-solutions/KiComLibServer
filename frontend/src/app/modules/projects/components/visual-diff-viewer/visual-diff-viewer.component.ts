import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnDestroy, OnInit, AfterViewInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Observable, Subject, of, throwError, timer } from 'rxjs';
import { filter, switchMap, take, takeUntil, tap } from 'rxjs/operators';

import { apiErrorMessage } from '../../../../shared/api-error';
import { bomStatusLabel, changeGroupLabel, revisionOptionLabel, shortSha } from '../../labels';
import { diffIdFromEnvelope, manifestFromEnvelope, ProjectService } from '../../services/project.service';
import { SvgObjectUrls } from '../../services/svg-object-urls';
import {
  ProjectRelease,
  ProjectRevision,
  SheetNode,
  VisualDiffChange,
  VisualDiffLayer,
  VisualDiffManifest,
  VisualDiffPage,
  VisualLayerCategory,
  VisualLayerStatus,
  ZoomTarget
} from '../../types/project';

@Component({
    selector: 'visual-diff-viewer',
    templateUrl: './visual-diff-viewer.component.html',
    styleUrl: './visual-diff-viewer.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class VisualDiffViewerComponent implements OnInit, AfterViewInit, OnDestroy {
  readonly layerStatuses: VisualLayerStatus[] = ['added', 'removed', 'changed', 'moved'];
  readonly layerCategories: VisualLayerCategory[] = ['parts', 'wiring', 'graphics', 'routing', 'zones'];
  readonly changeGroups = ['added', 'replaced', 'changed', 'removed', 'moved', 'renamed'];
  readonly groupLabel = changeGroupLabel;
  readonly catalogLabel = bomStatusLabel;
  readonly shortSha = shortSha;
  readonly revisionLabel = revisionOptionLabel;

  projectId = 0;
  projectName = '';
  headSha = '';
  revisions: ProjectRevision[] = [];
  releases: ProjectRelease[] = [];
  releasesUnavailable = false;

  baseSha = '';
  targetSha = '';
  pageId = '';
  focusKey = '';
  unitIndex = 0;

  manifest: VisualDiffManifest | null = null;
  page: VisualDiffPage | null = null;
  diffId = '';
  loading = false;
  runnerStatus = '';
  error = '';

  statusOn: Record<VisualLayerStatus, boolean> = {
    added: true,
    removed: true,
    changed: true,
    moved: true
  };
  categoryOn: Record<VisualLayerCategory, boolean> = {
    parts: true,
    wiring: true,
    graphics: false,
    routing: false,
    zones: false
  };

  scale = 1;
  panX = 0;
  panY = 0;
  zoomTarget: ZoomTarget | null = null;
  imageStamp = 0;

  images: SvgObjectUrls;

  @ViewChild('stage') stage?: ElementRef<HTMLElement>;

  private readonly destroyed$ = new Subject<void>();
  private dragging = false;
  private dragOrigin = { x: 0, y: 0 };
  private removeWheel?: () => void;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private projects: ProjectService,
    http: HttpClient
  ) {
    this.images = new SvgObjectUrls(http, this.destroyed$, () => {
      this.imageStamp += 1;
    });
  }

  ngOnInit(): void {
    this.projectId = Number(this.route.snapshot.paramMap.get('id'));
    this.applyDeepLink(this.route.snapshot.queryParamMap);
    this.loadSelectors();
    if (this.baseSha && this.targetSha) {
      this.startDiff(false);
    }
  }

  ngAfterViewInit(): void {
    const stage = this.stage?.nativeElement;
    if (!stage) {
      return;
    }
    const onWheel = (event: WheelEvent) => this.onWheel(event);
    stage.addEventListener('wheel', onWheel, { passive: false });
    this.removeWheel = () => stage.removeEventListener('wheel', onWheel);
  }

  ngOnDestroy(): void {
    this.removeWheel?.();
    this.images.revoke();
    this.destroyed$.next();
    this.destroyed$.complete();
  }

  applyDeepLink(query: ParamMap): void {
    this.baseSha = query.get('base') ?? '';
    this.targetSha = query.get('target') ?? '';
    this.pageId = query.get('page') ?? '';
    this.focusKey = query.get('focus') ?? '';
  }

  toggleStatus(status: VisualLayerStatus): void {
    this.statusOn = { ...this.statusOn, [status]: !this.statusOn[status] };
    this.refreshImages();
  }

  toggleCategory(category: VisualLayerCategory): void {
    this.categoryOn = { ...this.categoryOn, [category]: !this.categoryOn[category] };
    this.refreshImages();
  }

  visibleLayers(): VisualDiffLayer[] {
    if (!this.page || this.isSideBySide(this.page)) {
      return [];
    }
    return this.page.layers.filter(layer =>
      this.isStatusOn(layer.status) && this.isCategoryOn(layer.category)
    );
  }

  isSideBySide(page: VisualDiffPage): boolean {
    if (page.aligned === false || page.only === 'base' || page.only === 'target' || !page.base) {
      return true;
    }
    return page.layers.some(layer => layer.aligned === false);
  }

  sheetNodes(): SheetNode[] {
    return buildSheetTree((this.manifest?.pages ?? []).filter(page => page.kind !== 'board'));
  }

  boardPages(): VisualDiffPage[] {
    return (this.manifest?.pages ?? []).filter(page => page.kind === 'board');
  }

  changesFor(status: string): VisualDiffChange[] {
    return (this.page?.changes ?? []).filter(change => change.status === status);
  }

  imageSrc(path: string | null): string | null {
    return this.images.src(path);
  }

  markPosition(): { left: number; top: number } | null {
    if (!this.zoomTarget || !this.page) {
      return null;
    }
    const minX = this.page.viewBox[0] ?? 0;
    const minY = this.page.viewBox[1] ?? 0;
    const width = this.page.viewBox[2] || 1;
    const height = this.page.viewBox[3] || 1;
    return {
      left: ((this.zoomTarget.x - minX) / width) * 100,
      top: ((this.zoomTarget.y - minY) / height) * 100
    };
  }

  canvasTransform(): string {
    return `translate(${this.panX}px, ${this.panY}px) scale(${this.scale})`;
  }

  selectPageById(pageId: string | undefined): void {
    if (!pageId) {
      return;
    }
    const page = this.manifest?.pages.find(item => item.id === pageId);
    if (page) {
      this.selectPage(page);
    }
  }

  selectPage(page: VisualDiffPage, writeUrl = true): void {
    const kindChanged = !this.page || this.page.kind !== page.kind;
    this.page = page;
    this.pageId = page.id;
    if (kindChanged) {
      this.applyCategoryDefaults(page.kind);
    }
    if (page.kind === 'board' && page.crop && page.crop.length >= 4) {
      const x = page.crop[0] + page.crop[2] / 2;
      const y = page.crop[1] + page.crop[3] / 2;
      this.zoomTarget = { x, y, scale: this.scale };
      this.panTo(x, y);
    }
    this.refreshImages();
    if (writeUrl) {
      this.writeQuery();
    }
  }

  selectChange(change: VisualDiffChange, writeUrl = true): void {
    const points = change.at ?? [];
    const identity = change.key || change.reference;
    const same = !!identity && this.focusKey === identity;
    if (writeUrl && same && points.length > 1) {
      this.unitIndex = (this.unitIndex + 1) % points.length;
    } else {
      this.unitIndex = 0;
    }
    this.focusKey = identity;
    const point = points[this.unitIndex];
    if (point && point.length >= 2) {
      this.zoomTarget = { x: point[0], y: point[1], scale: 4 };
      this.scale = 4;
      this.panTo(point[0], point[1]);
    }
    if (writeUrl) {
      this.writeQuery();
    }
  }

  detailText(change: VisualDiffChange): string {
    return Object.keys(change.detail).map(field => {
      const pair = change.detail[field] ?? [];
      return `${field}: ${pair[0] ?? ''} → ${pair[1] ?? ''}`;
    }).join('; ');
  }

  useLatestReleaseToHead(): void {
    const release = [...this.releases]
      .filter(item => item.state !== 'withdrawn' && item.sha)
      .sort((a, b) => Date.parse(b.releasedAt) - Date.parse(a.releasedAt))[0];
    if (!release || !this.headSha) {
      this.error = 'A release and a head revision are both required for this shortcut.';
      return;
    }
    this.baseSha = release.sha;
    this.targetSha = this.headSha;
    this.startDiff();
  }

  usePreviousRevision(): void {
    const ordered = [...this.revisions].sort((a, b) => Date.parse(b.committedAt) - Date.parse(a.committedAt));
    const index = ordered.findIndex(item => item.commitSha === this.targetSha);
    const previous = index >= 0 ? ordered[index + 1] : undefined;
    if (!previous) {
      this.error = 'There is no previous revision for the selected target.';
      return;
    }
    this.baseSha = previous.commitSha;
    if (!this.targetSha) {
      this.targetSha = ordered[index]?.commitSha ?? '';
    }
    this.startDiff();
  }

  busyMessage(): string {
    if (this.runnerStatus === 'running') {
      return 'The runner is rendering the visual diff…';
    }
    return 'Waiting for the runner…';
  }

  startDiff(updateUrl = true): void {
    if (!this.projectId || !this.baseSha || !this.targetSha) {
      this.error = 'Choose a base revision and a target revision.';
      return;
    }
    if (updateUrl) {
      this.writeQuery();
    }
    this.loading = true;
    this.runnerStatus = 'queued';
    this.error = '';
    this.images.clear();
    this.projects.requestVisualDiff(this.projectId, this.baseSha, this.targetSha).pipe(
      switchMap(response => this.manifestFromResponse(response.status, response.body)),
      takeUntil(this.destroyed$)
    ).subscribe({
      next: manifest => this.showManifest(manifest),
      error: error => {
        this.loading = false;
        this.runnerStatus = '';
        this.error = error instanceof HttpErrorResponse
          ? apiErrorMessage(error, 'Could not start the visual diff.')
          : (error instanceof Error ? error.message : 'Could not start the visual diff.');
      }
    });
  }

  zoomBy(factor: number): void {
    this.scale = clampScale(this.scale * factor);
  }

  resetView(): void {
    this.scale = 1;
    this.panX = 0;
    this.panY = 0;
  }

  onWheel(event: WheelEvent): void {
    event.preventDefault();
    this.zoomBy(event.deltaY < 0 ? 1.1 : 0.9);
  }

  onPointerDown(event: PointerEvent): void {
    this.dragging = true;
    this.dragOrigin = { x: event.clientX - this.panX, y: event.clientY - this.panY };
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }

  onPointerMove(event: PointerEvent): void {
    if (!this.dragging) {
      return;
    }
    this.panX = event.clientX - this.dragOrigin.x;
    this.panY = event.clientY - this.dragOrigin.y;
  }

  onPointerUp(): void {
    this.dragging = false;
  }

  private loadSelectors(): void {
    if (!this.projectId) {
      return;
    }
    this.projects.get(this.projectId).pipe(takeUntil(this.destroyed$)).subscribe({
      next: project => {
        this.projectName = project.name;
        this.headSha = project.headSha;
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load the project.');
      }
    });
    this.projects.revisions(this.projectId, { limit: 200, offset: 0 }).pipe(takeUntil(this.destroyed$)).subscribe({
      next: page => {
        this.revisions = page.items;
      },
      error: () => {
        this.revisions = [];
      }
    });
    this.projects.releases(this.projectId).pipe(takeUntil(this.destroyed$)).subscribe({
      next: rows => {
        this.releases = rows;
        this.releasesUnavailable = false;
      },
      error: (error: HttpErrorResponse) => {
        this.releases = [];
        this.releasesUnavailable = error.status === 404;
      }
    });
  }

  private manifestFromResponse(status: number, body: Parameters<typeof manifestFromEnvelope>[0]): Observable<VisualDiffManifest> {
    const id = diffIdFromEnvelope(body);
    if (id) {
      this.diffId = id;
    }
    if (body?.status) {
      this.runnerStatus = body.status;
    }
    if (body?.status === 'failed') {
      return throwError(() => new Error(body.error || 'Visual diff failed.'));
    }
    const manifest = manifestFromEnvelope(body);
    const pending = status === 202 || (!!body?.status && body.status !== 'done' && !manifest);
    if (pending) {
      if (!this.diffId) {
        return throwError(() => new Error('Visual diff did not return an id.'));
      }
      return this.pollDiff(this.diffId);
    }
    if (!manifest) {
      return throwError(() => new Error('Visual diff did not return a manifest.'));
    }
    return of(manifest);
  }

  private pollDiff(diffId: string): Observable<VisualDiffManifest> {
    return timer(0, 2000).pipe(
      switchMap(() => this.projects.visualDiff(this.projectId, diffId)),
      tap(body => {
        if (body.status) {
          this.runnerStatus = body.status;
        }
      }),
      switchMap(body => {
        if (body.status === 'failed') {
          return throwError(() => new Error(body.error || 'Visual diff failed.'));
        }
        const manifest = manifestFromEnvelope(body);
        if (manifest && (body.status === 'done' || !body.status)) {
          return of(manifest);
        }
        return of(null);
      }),
      filter((manifest): manifest is VisualDiffManifest => !!manifest),
      take(1)
    );
  }

  private showManifest(manifest: VisualDiffManifest): void {
    this.manifest = manifest;
    this.loading = false;
    this.runnerStatus = '';
    const match = manifest.pages.find(page => page.id === this.pageId) ?? manifest.pages[0];
    if (match) {
      this.selectPage(match, false);
    }
    this.focusFromRoute();
  }

  private focusFromRoute(): void {
    if (!this.focusKey || !this.page) {
      return;
    }
    const match = this.page.changes.find(change =>
      change.key === this.focusKey || change.reference === this.focusKey
    );
    if (match) {
      this.selectChange(match, false);
    }
  }

  private refreshImages(): void {
    if (!this.page || !this.diffId || !this.projectId) {
      return;
    }
    const fileUrl = (path: string) => this.projects.visualDiffFileUrl(this.projectId, this.diffId, path);
    if (this.page.base) {
      this.images.fetch(this.page.base, fileUrl(this.page.base));
    }
    if (this.page.target) {
      this.images.fetch(this.page.target, fileUrl(this.page.target));
    }
    if (this.isSideBySide(this.page)) {
      return;
    }
    for (const layer of this.visibleLayers()) {
      this.images.fetch(layer.file, fileUrl(layer.file));
    }
  }

  private applyCategoryDefaults(kind: string): void {
    if (kind === 'board') {
      this.categoryOn = { parts: true, wiring: false, graphics: false, routing: false, zones: false };
      return;
    }
    this.categoryOn = { parts: true, wiring: true, graphics: false, routing: false, zones: false };
  }

  private panTo(x: number, y: number): void {
    const box = this.page?.viewBox ?? [0, 0, 1, 1];
    const width = box[2] || 1;
    const height = box[3] || 1;
    const nx = (x - (box[0] ?? 0)) / width;
    const ny = (y - (box[1] ?? 0)) / height;
    const view = this.stage?.nativeElement;
    const viewWidth = view?.clientWidth || 800;
    const viewHeight = view?.clientHeight || 480;
    this.panX = viewWidth / 2 - nx * viewWidth * this.scale;
    this.panY = viewHeight / 2 - ny * viewHeight * this.scale;
  }

  private writeQuery(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        base: this.baseSha || null,
        target: this.targetSha || null,
        page: this.pageId || null,
        focus: this.focusKey || null
      },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private isStatusOn(status: string): boolean {
    return this.layerStatuses.some(item => item === status) && this.statusOn[status as VisualLayerStatus];
  }

  private isCategoryOn(category: string): boolean {
    return this.layerCategories.some(item => item === category) && this.categoryOn[category as VisualLayerCategory];
  }
}

function clampScale(value: number): number {
  return Math.min(12, Math.max(0.25, value));
}

function buildSheetTree(pages: VisualDiffPage[]): SheetNode[] {
  const roots: SheetNode[] = [];
  for (const page of pages) {
    const parts = (page.sheetNames || page.page || page.id).split('/').filter(part => part.length > 0);
    const labels = parts.length ? parts : [page.id];
    let level = roots;
    let node: SheetNode | undefined;
    for (const label of labels) {
      node = level.find(item => item.name === label);
      if (!node) {
        node = { name: label, changeCount: 0, children: [] };
        level.push(node);
      }
      level = node.children;
    }
    if (node) {
      node.pageId = page.id;
      node.changeCount += page.changes.length;
    }
  }
  rollup(roots);
  return roots;
}

function rollup(nodes: SheetNode[]): number {
  let total = 0;
  for (const node of nodes) {
    const childTotal = rollup(node.children);
    node.changeCount += childTotal;
    total += node.changeCount;
  }
  return total;
}
