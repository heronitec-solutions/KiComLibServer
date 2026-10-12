import { Component, Inject, OnDestroy, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Sort } from '@angular/material/sort';

import { apiErrorMessage, AuthService } from '../../../../services/auth.service';
import { PartService } from '../../../part-table/services/part.service';
import { Part } from '../../../part-table/types/part';
import { WarehouseService } from '../../services/warehouse.service';
import { BATCH_SUGGESTIONS, WarehouseItem, WarehouseWrite } from '../../types/warehouse';

export interface WarehouseEditDialogData {
  item: WarehouseItem | null;
  takenPartIds: number[];
}

interface BatchDraft {
  name: string;
  quantity: number;
  location: string;
}

@Component({
    selector: 'warehouse-edit-dialog',
    templateUrl: './warehouse-edit-dialog.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class WarehouseEditDialogComponent implements OnInit, OnDestroy {
  readonly suggestions = BATCH_SUGGESTIONS;
  readonly maxImageBytes = 5 * 1024 * 1024;

  manual = false;
  catalogAllowed = false;
  partId: number | null = null;
  selectedPartName = '';
  name = '';
  value = '';
  comment = '';
  batches: BatchDraft[] = [];
  partQuery = '';
  parts: Part[] = [];
  partMatches: Part[] = [];
  partsLoading = false;
  partsError = '';
  error = '';
  saving = false;
  imageHash: string | null = null;
  imagePreviewUrl: string | null = null;
  imageUploading = false;
  imageTouched = false;
  private localImageUrl: string | null = null;

  constructor(
    public dialogRef: MatDialogRef<WarehouseEditDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) public data: WarehouseEditDialogData,
    private warehouse: WarehouseService,
    private partsApi: PartService,
    private auth: AuthService
  ) {
    this.catalogAllowed = this.auth.canRead();
    const item = data.item;
    if (item) {
      this.partId = item.partId;
      this.manual = item.partId == null;
      this.selectedPartName = item.partId ? item.name : '';
      this.name = item.name;
      this.value = item.value ?? '';
      this.comment = item.comment ?? '';
      if (item.partId == null) {
        this.imageHash = item.imageHash ?? null;
        this.imagePreviewUrl = item.imageHash
          ? this.warehouse.imageContentUrl(item.imageHash)
          : (item.hasImage ? this.warehouse.imageUrl(item.id) : null);
      }
      this.batches = item.batches.map(batch => ({
        name: batch.name,
        quantity: batch.quantity,
        location: batch.location ?? ''
      }));
    } else {
      this.manual = !this.catalogAllowed;
      this.batches = [{ name: 'Empty', quantity: 0, location: '' }];
    }

    if (!this.batches.length) {
      this.batches = [{ name: 'Empty', quantity: 0, location: '' }];
    }
  }

  ngOnInit(): void {
    if (this.catalogAllowed) {
      this.loadParts();
    }
  }

  ngOnDestroy(): void {
    this.revokeLocalImage();
  }

  get productImageUrl(): string | null {
    if (this.manual || this.partId == null) {
      return null;
    }

    const item = this.data.item;
    if (item?.id && item.partId === this.partId && item.hasImage) {
      return this.warehouse.imageUrl(item.id);
    }

    const part = this.parts.find(candidate => candidate.id === this.partId);
    return part?.imageHash ? this.partsApi.partImageUrl(part.id) : null;
  }

  close(): void {
    this.dialogRef.close(false);
  }

  setManual(manual: boolean): void {
    if (!manual && !this.catalogAllowed) {
      return;
    }

    this.manual = manual;
    this.error = '';
    if (manual) {
      this.partId = null;
      this.selectedPartName = '';
    }
  }

  onPartQuery(query: string): void {
    this.partQuery = query;
    this.refreshMatches();
  }

  selectPart(part: Part): void {
    if (this.isTaken(part.id)) {
      return;
    }

    this.partId = part.id;
    this.selectedPartName = part.name;
    this.value = part.value ?? '';
    this.partQuery = '';
    this.error = '';
  }

  clearPart(): void {
    this.partId = null;
    this.selectedPartName = '';
    this.refreshMatches();
  }

  isTaken(partId: number): boolean {
    return this.data.takenPartIds.includes(partId);
  }

  onQuantityChange(batch: BatchDraft, raw: number | string): void {
    const quantity = Math.max(0, Math.floor(Number(raw) || 0));
    const wasEmpty = batch.quantity === 0;
    batch.quantity = quantity;
    if (quantity === 0) {
      batch.name = 'Empty';
    } else if (wasEmpty || !batch.name.trim() || batch.name.trim().toLowerCase() === 'empty') {
      batch.name = 'Ordered';
    }
  }

  useSuggestion(batch: BatchDraft, name: string): void {
    if (batch.quantity === 0) {
      batch.quantity = 1;
    }
    batch.name = name;
  }

  onImageFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.imageUploading) {
      return;
    }

    if (file.size > this.maxImageBytes) {
      this.error = 'The product image is larger than 5 MB';
      return;
    }

    this.imageUploading = true;
    this.error = '';
    this.showLocalImage(file);
    this.warehouse.uploadImage(file).subscribe({
      next: result => {
        this.imageUploading = false;
        this.imageHash = result.hash;
        this.imageTouched = true;
        this.revokeLocalImage();
        this.imagePreviewUrl = this.warehouse.imageContentUrl(result.hash);
      },
      error: error => {
        this.imageUploading = false;
        this.revokeLocalImage();
        this.imagePreviewUrl = this.previewForHash();
        this.error = apiErrorMessage(error, 'Could not upload the product image');
      }
    });
  }

  clearImage(): void {
    this.imageHash = null;
    this.imageTouched = true;
    this.revokeLocalImage();
    this.imagePreviewUrl = null;
    this.error = '';
  }

  addBatch(): void {
    this.batches.push({ name: 'Ordered', quantity: 1, location: '' });
  }

  removeBatch(index: number): void {
    this.batches.splice(index, 1);
    if (!this.batches.length) {
      this.batches.push({ name: 'Empty', quantity: 0, location: '' });
    }
  }

  confirm(): void {
    if (this.imageUploading) {
      return;
    }

    const body = this.build();
    if (!body) {
      return;
    }

    this.saving = true;
    this.error = '';
    const request = this.data.item
      ? this.warehouse.update(this.data.item.id, body)
      : this.warehouse.create(body);
    request.subscribe({
      next: () => this.dialogRef.close(true),
      error: error => {
        this.saving = false;
        this.error = apiErrorMessage(error, 'Could not save the warehouse item');
      }
    });
  }

  private loadParts(): void {
    const sort: Sort = { active: 'name', direction: 'asc' };
    this.partsLoading = true;
    this.partsError = '';
    this.partsApi.getParts(sort).subscribe({
      next: parts => {
        this.parts = parts.slice().sort((left, right) => left.name.localeCompare(right.name));
        this.partsLoading = false;
        this.refreshMatches();
      },
      error: () => {
        this.partsLoading = false;
        this.partsError = 'Could not load catalog parts';
      }
    });
  }

  private refreshMatches(): void {
    const term = this.partQuery.trim().toLowerCase();
    this.partMatches = this.parts
      .filter(part => !term
        || part.name.toLowerCase().includes(term)
        || (part.value ?? '').toLowerCase().includes(term)
        || (part.comment ?? '').toLowerCase().includes(term))
      .slice(0, 12);
  }

  private build(): WarehouseWrite | null {
    if (!this.manual && !this.partId) {
      this.error = 'Choose a catalog part, or switch to a manual entry';
      return null;
    }

    if (this.manual && !this.name.trim()) {
      this.error = 'Name is required for a manual entry';
      return null;
    }

    for (const batch of this.batches) {
      const quantity = Math.max(0, Math.floor(Number(batch.quantity) || 0));
      if (quantity > 0 && !batch.name.trim()) {
        this.error = 'A batch with a quantity needs a name, for example Ordered';
        return null;
      }
      if (quantity > 0 && batch.name.trim().toLowerCase() === 'empty') {
        this.error = 'The batch Empty is reserved for a quantity of zero';
        return null;
      }
    }

    const body: WarehouseWrite = {
      partId: this.manual ? null : this.partId,
      name: this.manual ? this.name.trim() : '',
      value: this.manual ? this.value.trim() : '',
      comment: this.comment.trim(),
      batches: this.batches.map(batch => {
        const quantity = Math.max(0, Math.floor(Number(batch.quantity) || 0));
        return {
          name: quantity === 0 ? 'Empty' : batch.name.trim(),
          quantity,
          location: batch.location.trim()
        };
      })
    };

    if (this.manual && (this.imageTouched || this.imageHash)) {
      body.imageHash = this.imageHash ?? '';
    }

    return body;
  }

  private previewForHash(): string | null {
    return this.imageHash ? this.warehouse.imageContentUrl(this.imageHash) : null;
  }

  private showLocalImage(file: File): void {
    this.revokeLocalImage();
    this.localImageUrl = URL.createObjectURL(file);
    this.imagePreviewUrl = this.localImageUrl;
  }

  private revokeLocalImage(): void {
    if (this.localImageUrl) {
      URL.revokeObjectURL(this.localImageUrl);
      this.localImageUrl = null;
    }
  }
}
