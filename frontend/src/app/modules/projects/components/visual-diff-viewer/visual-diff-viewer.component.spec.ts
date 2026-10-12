import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommonModule } from '@angular/common';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { convertToParamMap, ActivatedRoute } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';

import { AppHeaderComponent } from '../../../../components/app-header/app-header.component';
import { VisualDiffViewerComponent } from './visual-diff-viewer.component';
import { VisualDiffChange, VisualDiffPage } from '../../types/project';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';

describe('VisualDiffViewerComponent', () => {
  let fixture: ComponentFixture<VisualDiffViewerComponent>;
  let component: VisualDiffViewerComponent;
  let http: HttpTestingController;

  const query = convertToParamMap({
    base: 'aaa',
    target: 'bbb',
    page: 'sch:1',
    focus: 'C2'
  });
  const params = convertToParamMap({ id: '7' });
  const routeSnapshot = { paramMap: params, queryParamMap: query, data: {} };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
    declarations: [VisualDiffViewerComponent],
    imports: [CommonModule,
        FormsModule,
        RouterTestingModule,
        MatIconModule,
        AppHeaderComponent],
    providers: [
        {
            provide: ActivatedRoute,
            useValue: {
                snapshot: routeSnapshot,
                firstChild: null
            }
        },
        provideHttpClient(withXhr(), withInterceptorsFromDi()),
        provideHttpClientTesting()
    ]
}).compileComponents();

    fixture = TestBed.createComponent(VisualDiffViewerComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.match(() => true).forEach(req => {
      if (!req.cancelled) {
        req.flush(req.request.method === 'POST' ? { status: 'done', pages: [] } : []);
      }
    });
    http.verify();
  });

  it('applies deep link query params on init', () => {
    fixture.detectChanges();

    expect(component.baseSha).toBe('aaa');
    expect(component.targetSha).toBe('bbb');
    expect(component.pageId).toBe('sch:1');
    expect(component.focusKey).toBe('C2');

    flushOpen(http);
  });

  it('hides layers when their status or category is turned off', () => {
    component.page = samplePage();

    expect(component.visibleLayers().map(layer => layer.file)).toEqual([
      'sch/p1/changed-parts.svg',
      'sch/p1/added-wiring.svg'
    ]);

    component.toggleStatus('added');
    expect(component.visibleLayers().map(layer => layer.file)).toEqual([
      'sch/p1/changed-parts.svg'
    ]);

    component.toggleStatus('added');
    component.toggleCategory('graphics');
    expect(component.visibleLayers().map(layer => layer.file)).toContain('sch/p1/added-graphics.svg');
  });

  it('shows a spinner while the runner is still working', () => {
    component.loading = true;
    component.runnerStatus = 'queued';
    fixture.detectChanges();

    const status = fixture.nativeElement.querySelector('.diff-busy') as HTMLElement;
    expect(status?.textContent).toContain('Waiting for the runner');
    expect(fixture.nativeElement.querySelector('.diff-spinner')).not.toBeNull();

    component.runnerStatus = 'running';
    fixture.detectChanges();
    expect(status.textContent).toContain('The runner is rendering the visual diff');

    flushOpen(http);
  });

  it('zooms to a change and cycles multi-unit positions', () => {
    component.page = samplePage();
    const change: VisualDiffChange = {
      status: 'changed',
      kind: 'symbol',
      key: 'sym-1',
      reference: 'U1',
      value: 'LM7805',
      at: [[10, 20], [30, 40]],
      catalogStatus: 'ok',
      detail: {}
    };

    component.selectChange(change);
    expect(component.focusKey).toBe('sym-1');
    expect(component.zoomTarget).toEqual({ x: 10, y: 20, scale: 4 });
    expect(component.scale).toBe(4);

    component.selectChange(change);
    expect(component.unitIndex).toBe(1);
    expect(component.zoomTarget).toEqual({ x: 30, y: 40, scale: 4 });
  });
});

function flushOpen(http: HttpTestingController): void {
  http.match(() => true).forEach(req => {
    if (req.cancelled) {
      return;
    }
    if (req.request.method === 'POST') {
      req.flush({ id: 'diff-1', status: 'done', pages: [] });
      return;
    }
    if (req.request.url.includes('/revision')) {
      req.flush([]);
      return;
    }
    if (req.request.url.includes('/release')) {
      req.flush([]);
      return;
    }
    req.flush({ id: 7, name: 'Demo', permissions: { canWrite: false, canAdmin: false } });
  });
}

function samplePage(): VisualDiffPage {
  return {
    id: 'sch:1',
    kind: 'schematic',
    page: '1',
    side: '',
    sheetNames: '/',
    file: 'board.kicad_sch',
    viewBox: [0, 0, 297, 210],
    crop: null,
    base: 'sch/p1/base.svg',
    target: null,
    only: '',
    aligned: true,
    layers: [
      { status: 'changed', category: 'parts', file: 'sch/p1/changed-parts.svg', aligned: true },
      { status: 'added', category: 'wiring', file: 'sch/p1/added-wiring.svg', aligned: true },
      { status: 'added', category: 'graphics', file: 'sch/p1/added-graphics.svg', aligned: true }
    ],
    changes: []
  };
}
