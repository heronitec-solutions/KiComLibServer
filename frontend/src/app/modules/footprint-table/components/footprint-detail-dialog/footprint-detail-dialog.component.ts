import { Component, Inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

import { FootprintService } from '../../services/footprint.service';
import { Footprint, FootprintModel } from '../../types/footprint';
import { parseSvgPreview } from '../../../part-table/services/svg-preview';

export interface FootprintDetailDialogData {
  id: number;
}

@Component({
    selector: 'footprint-detail-dialog',
    templateUrl: './footprint-detail-dialog.component.html',
    styleUrl: './footprint-detail-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class FootprintDetailDialogComponent implements OnInit {
  footprint?: Footprint;
  isLoading = true;
  loadError = '';

  preview: SafeHtml = '';
  previewViewBox = '0 0 1 1';

  readonly modelColumns = ['position', 'file', 'offset', 'scale', 'rotate', 'hide'];

  constructor(
    public dialogRef: MatDialogRef<FootprintDetailDialogComponent>,
    private footprintService: FootprintService,
    private sanitizer: DomSanitizer,
    @Inject(MAT_DIALOG_DATA) public data: FootprintDetailDialogData
  ) { }

  ngOnInit(): void {
    this.footprintService.getFootprint(this.data.id).subscribe({
      next: footprint => {
        this.footprint = footprint;
        this.isLoading = false;
        this.loadPreview(footprint.id);
      },
      error: () => {
        this.loadError = 'Could not load footprint.';
        this.isLoading = false;
      }
    });
  }

  get models(): FootprintModel[] {
    return this.footprint?.models ?? [];
  }

  close(): void {
    this.dialogRef.close();
  }

  formatVec(values: number[] | undefined | null): string {
    if (!values?.length) {
      return '—';
    }
    return values.map(v => Number(v).toFixed(3)).join(', ');
  }

  modelPath(model: FootprintModel): string {
    const dir = (model.directory || '').replace(/\\/g, '/');
    const file = model.filename || '';
    if (!dir) {
      return file || '—';
    }
    return dir.endsWith('/') ? `${dir}${file}` : `${dir}/${file}`;
  }

  private loadPreview(id: number): void {
    this.footprintService.getFootprintPreview(id).subscribe({
      next: raw => {
        const parsed = parseSvgPreview(raw);
        this.previewViewBox = parsed.viewBox;
        this.preview = this.sanitizer.bypassSecurityTrustHtml(parsed.content);
      },
      error: () => {
        this.preview = '';
      }
    });
  }
}
