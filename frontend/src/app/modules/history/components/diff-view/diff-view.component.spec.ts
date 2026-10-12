import { DomSanitizer } from '@angular/platform-browser';

import { DiffViewComponent } from './diff-view.component';
import { UnifiedDiffResult } from '../../types/commit';

describe('DiffViewComponent', () => {
  let component: DiffViewComponent;
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    document.body.appendChild(host);

    const sanitizer = {
      bypassSecurityTrustHtml: (value: string) => value
    } as unknown as DomSanitizer;

    component = new DiffViewComponent(sanitizer);
  });

  afterEach(() => {
    host.remove();
  });

  it('renders added and removed lines from a unified diff', () => {
    const diff: UnifiedDiffResult = {
      format: 'unified',
      diff: [
        '--- a/sample.txt',
        '+++ b/sample.txt',
        '@@ -1,3 +1,3 @@',
        ' context',
        '-removed line',
        '+added line'
      ].join('\n')
    };

    component.diff = diff;
    component.ngOnChanges({
      diff: {
        currentValue: diff,
        previousValue: null,
        firstChange: true,
        isFirstChange: () => true
      }
    });

    const html = String(component.unifiedHtml);
    host.innerHTML = html;
    const text = host.textContent ?? '';

    expect(component.isUnified).toBeTrue();
    expect(text).toContain('added line');
    expect(text).toContain('removed line');
    expect(host.querySelector('.d2h-dark-color-scheme')).withContext('dark diff theme').not.toBeNull();
    expect(host.querySelector('.d2h-ins')).withContext('added line marker').not.toBeNull();
    expect(host.querySelector('.d2h-del')).withContext('removed line marker').not.toBeNull();
  });

  it('keeps an unchanged file message inside the dark diff theme', () => {
    const diff: UnifiedDiffResult = {
      format: 'unified',
      diff: [
        'diff --git a/lib.kicad_sym b/lib.kicad_sym',
        'index 1111111..2222222 100644',
        '--- a/lib.kicad_sym',
        '+++ b/lib.kicad_sym'
      ].join('\n')
    };

    component.diff = diff;
    component.ngOnChanges({
      diff: {
        currentValue: diff,
        previousValue: null,
        firstChange: true,
        isFirstChange: () => true
      }
    });

    const html = String(component.unifiedHtml);
    host.innerHTML = html;

    expect(host.textContent).toContain('File without changes');
    expect(host.querySelector('.d2h-info')).withContext('unchanged-file row').not.toBeNull();
    expect(host.querySelector('.d2h-dark-color-scheme')).withContext('dark diff theme').not.toBeNull();
  });
});
