import { Component, Input, OnChanges, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { HttpErrorResponse } from '@angular/common/http';

import { apiErrorMessage } from '../../../../shared/api-error';
import { bomStatusClass, bomStatusLabel, orderStatusClass, orderStatusLabel } from '../../labels';
import { ProjectService } from '../../services/project.service';
import { BomAvailabilityLine, BomGroup, BomLine, BomStatus } from '../../types/project';
import { downloadText, tableCsv, tableHtml } from './bom-export';

interface BomTableRow extends BomLine {
  manufacturerDisplay: string;
  mpnDisplay: string;
  totalQuantity: number | null;
  distributor: string;
  orderNumber: string;
  orderStatus: string;
  limited: boolean;
}

@Component({
    selector: 'bom-table',
    templateUrl: './bom-table.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class BomTableComponent implements OnChanges {
  @Input() projectId = 0;
  @Input() sha = '';

  private readonly baseColumns = ['reference', 'value', 'footprint', 'mpn', 'quantity', 'status', 'part'];
  private readonly availabilityColumns = [
    'reference', 'value', 'manufacturer', 'mpn', 'quantity', 'totalQuantity',
    'status', 'part', 'distributor', 'orderNumber', 'orderStatus'
  ];

  readonly statusLabel = bomStatusLabel;
  readonly statusClass = bomStatusClass;
  readonly orderStatusLabel = orderStatusLabel;
  readonly orderStatusClass = orderStatusClass;

  columns = [...this.baseColumns];
  lines: BomLine[] = [];
  warnings: string[] = [];
  group: BomGroup = 'part';
  problemsOnly = false;
  pieces = 1;
  loading = false;
  checking = false;
  error = '';
  private availability: BomAvailabilityLine[] | null = null;
  private requestGeneration = 0;

  constructor(
    private projects: ProjectService,
    private dialog: MatDialog
  ) { }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['projectId'] || changes['sha']) && this.projectId && this.sha) {
      this.load();
    }
  }

  onGroup(group: BomGroup): void {
    this.group = group;
    this.load();
  }

  get visibleLines(): BomTableRow[] {
    const rows = this.lines.map((line, index) => this.toRow(line, index));
    if (!this.problemsOnly) {
      return rows;
    }
    return rows.filter(line => this.isProblem(line));
  }

  statuses(line: BomLine): BomStatus[] {
    const listed = Array.isArray(line.status) ? line.status : [line.status];
    const unique = listed.filter((status, index) => listed.indexOf(status) === index);
    if (line.dnp && !unique.includes('dnp')) {
      unique.push('dnp');
    }
    return unique;
  }

  openPart(line: BomLine): void {
    if (!line.partId) {
      return;
    }
    const partId = line.partId;
    void import('../../../part-table/part-table.module').then(module => {
      this.dialog.open(module.PartDetailDialogComponent, {
        width: '70%',
        maxWidth: '72rem',
        data: {
          id: partId,
          manufacturerList: [],
          distributorList: [],
          datasheetList: [],
          symbolList: [],
          footprintList: [],
          libraryList: []
        }
      });
    });
  }

  checkAvailability(): void {
    if (!this.projectId || !this.sha || this.checking) {
      return;
    }
    const pieces = this.boardCount();
    this.pieces = pieces;
    const generation = ++this.requestGeneration;
    this.checking = true;
    this.error = '';
    this.projects.bomAvailability(this.projectId, this.sha, this.group, pieces).subscribe({
      next: result => {
        if (generation !== this.requestGeneration) {
          return;
        }
        this.availability = result.lines;
        this.warnings = result.warnings;
        this.columns = [...this.availabilityColumns];
        this.checking = false;
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.requestGeneration) {
          return;
        }
        this.checking = false;
        this.error = apiErrorMessage(error, 'Could not check availability.');
      }
    });
  }

  exportTable(format: 'csv' | 'html'): void {
    const rows = this.visibleLines;
    const headers = this.exportHeaders();
    const matrix = rows.map(row => this.exportValues(row));
    if (format === 'csv') {
      downloadText('bom.csv', 'text/csv;charset=utf-8', tableCsv(headers, matrix));
      return;
    }
    downloadText('bom.html', 'text/html;charset=utf-8', tableHtml('Bill of materials', headers, matrix));
  }

  load(): void {
    this.requestGeneration++;
    this.availability = null;
    this.warnings = [];
    this.columns = [...this.baseColumns];
    this.checking = false;
    if (!this.projectId || !this.sha) {
      return;
    }
    this.loading = true;
    this.error = '';
    const generation = this.requestGeneration;
    this.projects.bom(this.projectId, this.sha, this.group).subscribe({
      next: lines => {
        if (generation !== this.requestGeneration) {
          return;
        }
        this.lines = lines;
        this.loading = false;
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.requestGeneration) {
          return;
        }
        this.lines = [];
        this.loading = false;
        this.error = apiErrorMessage(error, 'Could not load the bill of materials.');
      }
    });
  }

  private toRow(line: BomLine, index: number): BomTableRow {
    const hit = this.availability?.[index];
    return {
      ...line,
      manufacturerDisplay: hit?.manufacturer ?? line.manufacturer,
      mpnDisplay: hit?.mpn ?? line.mpn,
      totalQuantity: hit ? hit.totalQuantity : null,
      distributor: hit?.distributor ?? '',
      orderNumber: hit?.orderNumber ?? '',
      orderStatus: hit?.orderStatus ?? '',
      limited: hit?.limited ?? false
    };
  }

  private boardCount(): number {
    const value = Math.floor(Number(this.pieces));
    return Number.isFinite(value) && value >= 1 ? value : 1;
  }

  private exportHeaders(): string[] {
    return this.columns.map(column => {
      switch (column) {
        case 'reference':
          return 'Reference';
        case 'value':
          return 'Value';
        case 'footprint':
          return 'Footprint';
        case 'manufacturer':
          return 'Manufacturer';
        case 'mpn':
          return 'MPN';
        case 'quantity':
          return 'Qty';
        case 'totalQuantity':
          return 'Total Qty';
        case 'status':
          return 'Status';
        case 'part':
          return 'Catalog part';
        case 'distributor':
          return 'Distributor';
        case 'orderNumber':
          return 'Order-Number';
        case 'orderStatus':
          return 'Order Status';
        default:
          return column;
      }
    });
  }

  private exportValues(row: BomTableRow): string[] {
    return this.columns.map(column => {
      switch (column) {
        case 'reference':
          return row.reference;
        case 'value':
          return row.value;
        case 'footprint':
          return row.footprint;
        case 'manufacturer':
          return row.manufacturerDisplay;
        case 'mpn':
          return row.mpnDisplay;
        case 'quantity':
          return String(row.quantity);
        case 'totalQuantity':
          return row.totalQuantity == null ? '' : String(row.totalQuantity);
        case 'status':
          return this.statuses(row).map(status => bomStatusLabel(status)).join(', ');
        case 'part':
          return row.partName || (row.partId ? `Part #${row.partId}` : '');
        case 'distributor':
          return row.distributor;
        case 'orderNumber':
          return row.orderNumber;
        case 'orderStatus':
          return this.orderStatusText(row);
        default:
          return '';
      }
    });
  }

  private orderStatusText(row: BomTableRow): string {
    const parts = [];
    if (row.orderStatus) {
      parts.push(orderStatusLabel(row.orderStatus));
    }
    if (row.limited) {
      parts.push('Limited');
    }
    return parts.join(', ');
  }

  private isProblem(line: BomLine): boolean {
    return this.statuses(line).some(status => status !== 'ok');
  }
}
