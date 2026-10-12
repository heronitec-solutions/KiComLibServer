import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { DistributorProduct } from '../../services/distributor-search.service';
import {
  ImportFieldId,
  ImportFieldOption,
  KeywordImportChoice,
  importFieldOptions,
  toggleImportField
} from '../../services/import-fields';
import { lifecycleLabel } from '../../types/part';

export type DistributorPickKind = 'keyword' | 'alternatives' | 'orders';

export interface DistributorPickDialogData {
  kind: DistributorPickKind;
  results: DistributorProduct[];
  warnings: string[];
}

@Component({
    selector: 'distributor-pick-dialog',
    templateUrl: './distributor-pick-dialog.component.html',
    styleUrl: './distributor-pick-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class DistributorPickDialogComponent {
  readonly lifecycleLabel = lifecycleLabel;
  readonly columns: string[];
  readonly title: string;
  readonly confirmLabel: string;
  readonly emptyMessage: string;
  readonly single: boolean;

  selected: DistributorProduct | null = null;
  importOptions: ImportFieldOption[] = [];
  private checkedFields = new Set<ImportFieldId>();
  private readonly checked = new Set<DistributorProduct>();

  constructor(
    public dialogRef: MatDialogRef<DistributorPickDialogComponent, KeywordImportChoice | DistributorProduct[] | undefined>,
    @Inject(MAT_DIALOG_DATA) public data: DistributorPickDialogData
  ) {
    this.single = data.kind === 'keyword';
    this.columns = DistributorPickDialogComponent.columnsFor(data.kind);
    this.title = DistributorPickDialogComponent.titleFor(data.kind);
    this.confirmLabel = this.single ? 'Apply' : 'Add';
    this.emptyMessage = DistributorPickDialogComponent.emptyFor(data.kind);
    if (this.single && data.results.length === 1) {
      this.choose(data.results[0]);
    }
  }

  close(): void {
    this.dialogRef.close(undefined);
  }

  choose(row: DistributorProduct): void {
    if (this.selected === row) {
      return;
    }

    this.selected = row;
    this.importOptions = importFieldOptions(row);
    this.checkedFields = new Set(this.importOptions.filter(option => option.available).map(option => option.id));
  }

  isChecked(row: DistributorProduct): boolean {
    return this.checked.has(row);
  }

  isFieldChecked(id: ImportFieldId): boolean {
    return this.checkedFields.has(id);
  }

  onFieldChange(option: ImportFieldOption, event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!option.available) {
      input.checked = false;
      return;
    }

    this.checkedFields = toggleImportField(this.checkedFields, option.id, input.checked);
  }

  toggle(row: DistributorProduct, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    if (this.checked.has(row)) {
      this.checked.delete(row);
    } else {
      this.checked.add(row);
    }
  }

  onRowClick(row: DistributorProduct): void {
    if (this.single) {
      this.choose(row);
      return;
    }

    this.toggle(row);
  }

  get canConfirm(): boolean {
    if (!this.single) {
      return this.checked.size > 0;
    }

    return this.selected != null && this.importOptions.some(option =>
      option.available && this.checkedFields.has(option.id));
  }

  confirm(): void {
    if (!this.canConfirm) {
      return;
    }

    if (this.single && this.selected) {
      this.dialogRef.close({
        product: this.selected,
        fields: this.importOptions
          .filter(option => option.available && this.checkedFields.has(option.id))
          .map(option => option.id)
      });
      return;
    }

    this.dialogRef.close(this.data.results.filter(row => this.checked.has(row)));
  }

  text(value: string | null | undefined): string {
    const trimmed = (value ?? '').trim();
    return trimmed || '—';
  }

  qty(value: number | null | undefined): string {
    return value == null ? '—' : String(value);
  }

  private static columnsFor(kind: DistributorPickKind): string[] {
    if (kind === 'keyword') {
      return ['select', 'manufacturer', 'partNumber', 'description', 'status', 'orderNumber'];
    }

    if (kind === 'alternatives') {
      return ['select', 'manufacturer', 'partNumber', 'description', 'status', 'source'];
    }

    return ['select', 'partNumber', 'distributor', 'orderNumber', 'quantityMin', 'quantitySteps', 'source'];
  }

  private static titleFor(kind: DistributorPickKind): string {
    if (kind === 'keyword') {
      return 'Look up part';
    }

    if (kind === 'alternatives') {
      return 'Alternative part numbers';
    }

    return 'Order numbers';
  }

  private static emptyFor(kind: DistributorPickKind): string {
    if (kind === 'keyword') {
      return 'No matching parts.';
    }

    if (kind === 'alternatives') {
      return 'No alternative part numbers.';
    }

    return 'No order numbers.';
  }
}
