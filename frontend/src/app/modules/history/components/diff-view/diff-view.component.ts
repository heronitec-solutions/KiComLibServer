import {
  Component,
  Input,
  OnChanges,
  SimpleChanges,
  ChangeDetectionStrategy
} from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { html as diff2Html } from 'diff2html';
import { ColorSchemeType } from 'diff2html/lib/types';

import {
  BinaryDiffResult,
  DiffResult,
  isBinaryDiff,
  isUnifiedDiff,
  UnifiedDiffResult
} from '../../types/commit';

@Component({
    selector: 'diff-view',
    templateUrl: './diff-view.component.html',
    styleUrl: './diff-view.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DiffViewComponent implements OnChanges {
  @Input() diff: DiffResult | null = null;

  unifiedHtml: SafeHtml = '';
  binaryDiff: BinaryDiffResult | null = null;
  emptyMessage = 'No diff available.';

  constructor(private sanitizer: DomSanitizer) { }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['diff']) {
      this.render(this.diff);
    }
  }

  get isUnified(): boolean {
    return isUnifiedDiff(this.diff);
  }

  get isBinary(): boolean {
    return isBinaryDiff(this.diff);
  }

  private render(diff: DiffResult | null): void {
    this.unifiedHtml = '';
    this.binaryDiff = null;

    if (!diff) {
      return;
    }

    if (isUnifiedDiff(diff)) {
      this.unifiedHtml = this.sanitizer.bypassSecurityTrustHtml(this.renderUnified(diff));
      return;
    }

    if (isBinaryDiff(diff)) {
      this.binaryDiff = diff;
    }
  }

  private renderUnified(diff: UnifiedDiffResult): string {
    const text = diff.diff ?? '';
    if (!text.trim()) {
      return `<pre class="diff-empty">${this.emptyMessage}</pre>`;
    }

    return diff2Html(text, {
      drawFileList: false,
      matching: 'lines',
      outputFormat: 'line-by-line',
      colorScheme: ColorSchemeType.DARK
    });
  }
}
