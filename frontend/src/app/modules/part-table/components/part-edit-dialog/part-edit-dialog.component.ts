import { Component, Inject, OnDestroy, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Sort } from '@angular/material/sort';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Observable, forkJoin, from, of, throwError } from 'rxjs';
import { catchError, concatMap, map, switchMap, tap } from 'rxjs/operators';

import { DatasheetService } from '../../../datasheet-table/services/datasheet.service';
import { PartService } from '../../services/part.service';
import { PartNumberService } from '../../services/part-number.service';
import { OrderNumberService } from '../../services/order-number.service';
import {
  DistributorMultiResult,
  DistributorPartQuery,
  DistributorProduct,
  DistributorProviderError,
  DistributorProviderInfo,
  DistributorSearchService,
  distributorSearchError
} from '../../services/distributor-search.service';
import { ManufacturerService } from '../../../manufacturer-table/services/manufacturer.service';
import { DistributorService } from '../../../distributor-table/services/distributor.service';
import { FootprintService } from '../../../footprint-table/services/footprint.service';
import { SymbolService } from '../../../symbol-table/services/symbol.service';
import { parseSvgPreview } from '../../services/svg-preview';
import {
  ImportFieldId,
  isKeywordImportChoice,
  KeywordImportChoice
} from '../../services/import-fields';
import { describeSaveFailure } from './save-error';
import { DatasheetDownloadDialogComponent } from '../datasheet-download-dialog/datasheet-download-dialog.component';
import { ErrorNoticeDialogComponent } from '../error-notice-dialog/error-notice-dialog.component';
import {
  DistributorPickDialogComponent,
  DistributorPickDialogData,
  DistributorPickKind
} from '../distributor-pick-dialog/distributor-pick-dialog.component';

import { Manufacturer } from '../../../manufacturer-table/types/manufacturer';
import { Distributor } from '../../../distributor-table/types/distributor';
import { Datasheet } from '../../../datasheet-table/types/datasheet';
import { Footprint } from '../../../footprint-table/types/footprint';
import { Symbol } from '../../../symbol-table/types/symbol';
import { Library } from '../../../library-table/types/library';
import { LIFECYCLE_OPTIONS, Part, PartLink } from '../../types/part';
import { PartNumber } from '../../types/part-number';
import { OrderNumber } from '../../types/order-number';

type AlternativeCall =
  | { ok: true; response: DistributorMultiResult }
  | { ok: false; error: unknown };

/** Editable manufacturer part number. `clientKey` stays stable before the row is saved. */
interface PartNumberRow {
  id: number;
  clientKey: number;
  manufacturerId: number | null;
  manufacturerPartNumber: string;
  quantity: number;
  packageType: string;
}

/** Editable distributor order number, linked to a part-number row by `partNumberKey`. */
interface OrderNumberRow {
  id: number;
  partNumberKey: number | null;
  distributorId: number | null;
  orderNumberValue: string;
  quantityMin: number;
  quantitySteps: number;
}

/** Fields copied from an existing part when a new part is created as its alternative. */
export interface PartEditPreset {
  comment?: string;
  libraryId?: number;
  symbolId?: number;
  footprintId?: number;
}

export interface PartEditDialogData {
  id: number;
  manufacturerList: Manufacturer[];
  distributorList: Distributor[];
  datasheetList: Datasheet[];
  symbolList: Symbol[];
  footprintList: Footprint[];
  libraryList: Library[];
  preset?: PartEditPreset;
  /** After a new part is saved, append its id to this part's alternative list. */
  alternativeOfId?: number;
  /** Hit chosen from the part view. Applied after this part's numbers have loaded. */
  distributorProduct?: DistributorProduct;
}

@Component({
    selector: 'part-edit-dialog',
    templateUrl: './part-edit-dialog.component.html',
    styleUrl: './part-edit-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class PartEditDialogComponent implements OnDestroy {
  private readonly sort: Sort = { active: 'id', direction: 'asc' };
  private nextClientKey = -1;

  part: Part = {
    id: -1,
    uuid: undefined,
    name: '',
    comment: '',
    datasheetId: undefined,
    datasheetIds: [],
    datasheet: undefined,
    libraryId: undefined,
    symbolId: undefined,
    footprintId: undefined,
    lifecycleStatus: 'active',
    keywords: undefined,
    value: '',
    properties: undefined,
    alternatives: undefined,
    links: [],
    imageHash: null,
    updatedAt: undefined,
    createdAt: undefined
  };

  partNumbers: PartNumberRow[] = [];
  orderNumbers: OrderNumberRow[] = [];

  readonly lifecycleOptions = LIFECYCLE_OPTIONS;
  readonly partNumberColumns = ['manufacturer', 'manufacturerPartNumber', 'packageType', 'quantity', 'actions'];
  readonly orderNumberColumns = ['partNumber', 'distributor', 'orderNumber', 'quantityMin', 'quantitySteps', 'actions'];
  readonly alternativeColumns = ['name', 'value', 'comment', 'actions'];
  readonly datasheetColumns = ['filename', 'directory', 'actions'];

  datasheetIds: number[] = [];
  selectedDatasheets: Datasheet[] = [];
  availableDatasheets: Datasheet[] = [];
  datasheetToAdd: number | null = null;
  links: PartLink[] = [];

  /** Every part the alternatives picker can offer. */
  catalog: Part[] = [];
  alternativeIds: number[] = [];
  selectedAlternatives: Part[] = [];
  alternativeToAdd: number | null = null;
  alternativeSearch = '';
  alternativeChoices: Part[] = [];
  alternativeTruncated = false;
  readonly alternativeResultLimit = 40;

  @ViewChild('alternativeSearchTrigger') alternativeSearchTrigger?: MatAutocompleteTrigger;

  id = -1;
  isLoading = false;
  isSaving = false;
  lookupQuery = '';
  selectedLookupProvider = '';
  lookingUp = false;
  searchingAlternatives = false;
  searchingOrders = false;
  downloadingDatasheet = false;
  importingImage = false;
  imageUploading = false;
  imagePreviewUrl: string | null = null;
  private localImageUrl: string | null = null;
  readonly maxImageBytes = 5 * 1024 * 1024;
  private lookupQueryEdited = false;
  private pendingDistributorProduct?: DistributorProduct;
  private distributorProviders: DistributorProviderInfo[] = [];
  private providersLoaded = false;
  private applyingDistributor = false;
  private errorDialogOpen = false;
  private pendingErrors: { message: string; details: string[] }[] = [];
  private applyNotice = '';

  manufacturerList: Manufacturer[] = [];
  distributorList: Distributor[] = [];
  datasheetList: Datasheet[] = [];
  symbolList: Symbol[] = [];
  footprintList: Footprint[] = [];
  libraryList: Library[] = [];

  symbolPreviewImageData: SafeHtml = '';
  footprintPreviewImageData: SafeHtml = '';
  symbolPreviewImageViewBox = '0 0 1 1';
  footprintPreviewImageViewBox = '0 0 1 1';

  private removedPartNumberIds: number[] = [];
  private removedOrderNumberIds: number[] = [];
  private alternativeLinkFailed = false;

  constructor(
    public dialogRef: MatDialogRef<PartEditDialogComponent>,
    private partService: PartService,
    private datasheetService: DatasheetService,
    private partNumberService: PartNumberService,
    private orderNumberService: OrderNumberService,
    private symbolService: SymbolService,
    private footprintService: FootprintService,
    private distributorSearch: DistributorSearchService,
    private manufacturerService: ManufacturerService,
    private distributorService: DistributorService,
    private dialog: MatDialog,
    private sanitizer: DomSanitizer,
    @Inject(MAT_DIALOG_DATA) public data: PartEditDialogData
  ) {
    this.id = data.id;
    this.manufacturerList = data.manufacturerList;
    this.distributorList = data.distributorList;
    this.datasheetList = data.datasheetList;
    this.symbolList = data.symbolList;
    this.footprintList = data.footprintList;
    this.libraryList = data.libraryList;
    this.lookupQuery = this.part.name;
    this.pendingDistributorProduct = data.distributorProduct;
    this.refreshDatasheets();
    this.loadLookupProvider();

    this.partService.getParts({ active: 'name', direction: 'asc' }).subscribe({
      next: parts => {
        this.catalog = parts;
        this.refreshSelectedAlternatives();
      },
      error: error => console.error('Error:', error)
    });

    if (this.id < 0) {
      this.applyPreset(data.preset);
    } else {
      this.isLoading = true;
      this.partService.getPart(this.id).subscribe({
        next: part => {
          this.part = part;
          if (!this.lookupQueryEdited) {
            this.lookupQuery = part.name ?? '';
          }
          this.datasheetIds = this.idsFromPart(part);
          this.refreshDatasheets();
          this.links = (part.links ?? []).map(link => ({ label: link.label ?? '', url: link.url ?? '' }));
          this.alternativeIds = (part.alternatives ?? []).filter(id => id !== part.id);
          this.refreshSelectedAlternatives();
          this.loadPreviews(part);
          this.loadProductImage(part);
          this.loadNumbers(part.id);
        },
        error: error => {
          console.error('Error:', error);
          this.isLoading = false;
          this.showError('Could not load this part.');
        }
      });
    }
  }

  ngOnDestroy(): void {
    this.revokeLocalImage();
  }

  get dialogTitle(): string {
    if (this.data.id >= 0) {
      return 'Edit Part';
    }

    return this.data.alternativeOfId != null ? 'New Alternative' : 'New Part';
  }

  close(): void {
    this.dialogRef.close(false);
  }

  /** Parts that can still be added: not this part, not already chosen, matching the search. */
  refreshAlternativeChoices(): void {
    const query = this.alternativeSearch.trim().toLowerCase();
    if (!query) {
      this.alternativeChoices = [];
      this.alternativeTruncated = false;
      return;
    }

    const matches = this.catalog.filter(candidate =>
      candidate.id !== this.part.id
      && !this.alternativeIds.includes(candidate.id)
      && [candidate.name, candidate.value, candidate.comment]
        .some(field => String(field ?? '').toLowerCase().includes(query)));
    this.alternativeTruncated = matches.length > this.alternativeResultLimit;
    this.alternativeChoices = matches.slice(0, this.alternativeResultLimit);
  }

  onAlternativeSearch(value: Part | string | null): void {
    if (value && typeof value === 'object') {
      this.chooseAlternative(value);
      return;
    }

    this.alternativeSearch = value ?? '';
    const selected = this.catalog.find(candidate => candidate.id === this.alternativeToAdd);
    const typed = this.alternativeSearch.trim().toLowerCase();
    if (selected && this.partOptionLabel(selected).toLowerCase() === typed) {
      this.alternativeChoices = [];
      this.alternativeTruncated = false;
      return;
    }

    this.alternativeToAdd = null;
    this.refreshAlternativeChoices();
    setTimeout(() => this.alternativeSearchTrigger?.openPanel());
  }

  chooseAlternative(candidate: Part): void {
    this.alternativeToAdd = candidate.id;
    this.alternativeSearch = this.partOptionLabel(candidate);
    this.alternativeChoices = [];
    this.alternativeTruncated = false;
  }

  readonly displayAlternative = (value: Part | string | null): string => {
    if (value && typeof value === 'object') {
      return this.partOptionLabel(value);
    }
    return value ?? '';
  };

  partOptionLabel(candidate: Part): string {
    const value = (candidate.value ?? '').trim();
    return value ? `${candidate.name} — ${value}` : candidate.name;
  }

  readonly datasheetLabel = (item: Datasheet): string => {
    const directory = (item.directory ?? '').replace(/\/$/, '');
    return directory ? `${directory}/${item.filename}` : (item.filename ?? '');
  };

  addDatasheet(): void {
    const id = this.datasheetToAdd;
    if (id == null || this.datasheetIds.includes(id)) {
      return;
    }

    this.datasheetIds = [...this.datasheetIds, id];
    this.datasheetToAdd = null;
    this.refreshDatasheets();
  }

  removeDatasheet(id: number): void {
    this.datasheetIds = this.datasheetIds.filter(existing => existing !== id);
    if (this.datasheetToAdd === id) {
      this.datasheetToAdd = null;
    }
    this.refreshDatasheets();
  }

  datasheetFileUrl(sheet: Datasheet, download = false): string {
    return this.datasheetService.fileUrl(sheet.id, download);
  }

  addLink(): void {
    this.links = [...this.links, { label: '', url: '' }];
  }

  removeLink(index: number): void {
    this.links = this.links.filter((_, item) => item !== index);
  }
  readonly partNumberId = (row: PartNumberRow): number => row.clientKey;
  readonly partNumberLabel = (row: PartNumberRow): string => this.partNumberOptionLabel(row);

  addAlternative(): void {
    const id = this.alternativeToAdd;
    if (id == null || id === this.part.id || this.alternativeIds.includes(id)) {
      return;
    }

    this.alternativeIds = [...this.alternativeIds, id];
    this.alternativeToAdd = null;
    this.alternativeSearch = '';
    this.refreshSelectedAlternatives();
  }

  removeAlternative(id: number): void {
    this.alternativeIds = this.alternativeIds.filter(existing => existing !== id);
    this.refreshSelectedAlternatives();
  }

  private idsFromPart(part: Part): number[] {
    const listed = (part.datasheetIds ?? []).filter(id => id > 0);
    const source = listed.length ? listed : (part.datasheetId ? [part.datasheetId] : []);
    return source.filter((id, index) => source.indexOf(id) === index);
  }

  private refreshDatasheets(): void {
    this.selectedDatasheets = this.datasheetIds.map(id =>
      this.datasheetList.find(sheet => sheet.id === id)
      ?? { id, uuid: '', filename: `Datasheet #${id}`, directory: '', fileSize: 0, comment: null, updatedAt: '', createdAt: '' });
    this.availableDatasheets = this.datasheetList.filter(sheet => !this.datasheetIds.includes(sheet.id));
  }

  private refreshSelectedAlternatives(): void {
    this.selectedAlternatives = this.alternativeIds.map(id =>
      this.catalog.find(candidate => candidate.id === id)
      ?? { id, name: `Part #${id}`, value: '', comment: '' } as Part);
    this.refreshAlternativeChoices();
  }

  confirm(): void {
    if (this.imageUploading || this.importingImage) {
      return;
    }

    const validationError = this.validate();
    if (validationError) {
      this.showError(validationError);
      return;
    }

    this.isSaving = true;
    this.alternativeLinkFailed = false;
    this.showError('');
    this.part.alternatives = this.alternativeIds.filter(id => id !== this.part.id);
    this.part.datasheetIds = [...this.datasheetIds];
    this.part.datasheetId = this.datasheetIds[0];
    this.part.links = this.links
      .map(link => ({ label: link.label.trim(), url: link.url.trim() }))
      .filter(link => link.label || link.url);

    const part$ = this.id >= 0
      ? this.partService.updateData(this.id, this.part).pipe(map(() => this.id))
      : this.partService.postData(this.part).pipe(
          tap((created: Part) => {
            this.id = created.id;
            this.part.id = created.id;
          }),
          map((created: Part) => created.id)
        );

    part$.pipe(
      switchMap(partId => this.persistNumbers(partId).pipe(map(() => partId))),
      switchMap(partId => this.attachToOriginal(partId))
    ).subscribe({
      next: partId => this.dialogRef.close(partId),
      error: error => {
        console.error('Error:', error);
        const failure = describeSaveFailure(error);
        const summary = this.alternativeLinkFailed
          ? 'The part was saved, but it could not be added to the original part. Save again to retry.'
          : failure.summary;
        this.showError(summary, this.alternativeLinkFailed ? [] : failure.details);
        this.isSaving = false;
      }
    });
  }

  addPartNumber(): void {
    this.partNumbers = [
      ...this.partNumbers,
      {
        id: -1,
        clientKey: this.takeClientKey(),
        manufacturerId: null,
        manufacturerPartNumber: '',
        quantity: 1,
        packageType: ''
      }
    ];
  }

  removePartNumber(row: PartNumberRow): void {
    if (row.id >= 0) {
      this.removedPartNumberIds.push(row.id);
    }

    for (const order of this.orderNumbers) {
      if (order.partNumberKey === row.clientKey && order.id >= 0) {
        this.removedOrderNumberIds.push(order.id);
      }
    }

    this.orderNumbers = this.orderNumbers.filter(order => order.partNumberKey !== row.clientKey);
    this.partNumbers = this.partNumbers.filter(item => item.clientKey !== row.clientKey);
  }

  addOrderNumber(): void {
    this.orderNumbers = [
      ...this.orderNumbers,
      {
        id: -1,
        partNumberKey: this.partNumbers[0]?.clientKey ?? null,
        distributorId: null,
        orderNumberValue: '',
        quantityMin: 1,
        quantitySteps: 1
      }
    ];
  }

  removeOrderNumber(row: OrderNumberRow): void {
    if (row.id >= 0) {
      this.removedOrderNumberIds.push(row.id);
    }

    this.orderNumbers = this.orderNumbers.filter(item => item !== row);
  }

  partNumberOptionLabel(row: PartNumberRow): string {
    const manufacturer = this.manufacturerList.find(item => item.id === row.manufacturerId)?.name;
    const mpn = row.manufacturerPartNumber.trim();

    if (manufacturer && mpn) {
      return `${manufacturer} · ${mpn}`;
    }

    if (mpn) {
      return mpn;
    }

    if (manufacturer) {
      return manufacturer;
    }

    return 'New part number';
  }

  setSymbol(id: number | null): void {
    this.part.symbolId = id ?? undefined;
    if (id != null) {
      this.loadSymbolPreview(id);
    } else {
      this.symbolPreviewImageData = '';
      this.symbolPreviewImageViewBox = '0 0 1 1';
    }
  }

  setFootprint(id: number | null): void {
    this.part.footprintId = id ?? undefined;
    if (id != null) {
      this.loadFootprintPreview(id);
    } else {
      this.footprintPreviewImageData = '';
      this.footprintPreviewImageViewBox = '0 0 1 1';
    }
  }

  private applyPreset(preset: PartEditPreset | undefined): void {
    if (!preset) {
      return;
    }

    this.part.comment = preset.comment ?? '';
    if (preset.libraryId != null && preset.libraryId > 0) {
      this.part.libraryId = preset.libraryId;
    }
    if (preset.symbolId != null && preset.symbolId > 0) {
      this.setSymbol(preset.symbolId);
    }
    if (preset.footprintId != null && preset.footprintId > 0) {
      this.setFootprint(preset.footprintId);
    }
  }

  /** Appends a newly created part to the original part that asked for an alternative. */
  private attachToOriginal(createdId: number): Observable<number> {
    const originalId = this.data.alternativeOfId;
    if (originalId == null || originalId < 0 || originalId === createdId) {
      return of(createdId);
    }

    return this.partService.getPart(originalId).pipe(
      switchMap(original => {
        const ids = (original.alternatives ?? []).filter(id => id > 0 && id !== original.id && id !== createdId);
        original.alternatives = [...ids, createdId];
        return this.partService.updateData(original.id, original);
      }),
      map(() => createdId),
      tap({ error: () => { this.alternativeLinkFailed = true; } })
    );
  }

  private loadNumbers(partId: number): void {
    this.partNumberService.getPartNumbers(this.sort, partId).pipe(
      switchMap(partNumbers => {
        this.partNumbers = partNumbers.map(partNumber => this.toPartNumberRow(partNumber));

        if (!partNumbers.length) {
          return of([] as OrderNumber[]);
        }

        return forkJoin(
          partNumbers.map(partNumber => this.orderNumberService.getOrderNumbers(this.sort, partNumber.id))
        ).pipe(map(groups => groups.flat()));
      })
    ).subscribe({
      next: orderNumbers => {
        this.orderNumbers = orderNumbers.map(orderNumber => this.toOrderNumberRow(orderNumber));
        this.isLoading = false;
        this.consumeDistributorProduct();
      },
      error: error => {
        console.error('Error:', error);
        this.isLoading = false;
        this.showError('Could not load part and order numbers.');
      }
    });
  }

  private loadPreviews(part: Part): void {
    if (part.symbolId) {
      this.loadSymbolPreview(part.symbolId);
    }

    if (part.footprintId) {
      this.loadFootprintPreview(part.footprintId);
    }
  }

  private loadSymbolPreview(symbolId: number): void {
    this.symbolService.getSymbolPreview(symbolId).subscribe(imageData => {
      const preview = parseSvgPreview(imageData);
      this.symbolPreviewImageViewBox = preview.viewBox;
      this.symbolPreviewImageData = this.sanitizer.bypassSecurityTrustHtml(preview.content);
    });
  }

  private loadFootprintPreview(footprintId: number): void {
    this.footprintService.getFootprintPreview(footprintId).subscribe(imageData => {
      const preview = parseSvgPreview(imageData);
      this.footprintPreviewImageViewBox = preview.viewBox;
      this.footprintPreviewImageData = this.sanitizer.bypassSecurityTrustHtml(preview.content);
    });
  }

  private showError(message: string, details: string[] = []): void {
    const text = message.trim();
    if (!text) {
      return;
    }

    if (this.pendingErrors.some(item => item.message === text)) {
      return;
    }

    this.pendingErrors.push({ message: text, details });
    this.presentError();
  }

  private presentError(): void {
    if (this.errorDialogOpen || !this.pendingErrors.length) {
      return;
    }

    const next = this.pendingErrors.shift();
    if (!next) {
      return;
    }

    this.errorDialogOpen = true;
    this.dialog.open(ErrorNoticeDialogComponent, {
      width: '28rem',
      maxWidth: '95vw',
      data: { message: next.message, details: next.details }
    }).afterClosed().subscribe(() => {
      this.errorDialogOpen = false;
      this.presentError();
    });
  }

  private validate(): string {
    for (const link of this.links) {
      const label = link.label.trim();
      const url = link.url.trim();
      if (!label && !url) {
        continue;
      }

      if (!/^https?:\/\//i.test(url)) {
        return 'Each link needs an address that starts with http:// or https://.';
      }
    }

    for (const row of this.orderNumbers) {
      if (!row.orderNumberValue.trim()) {
        continue;
      }

      const partNumber = this.partNumbers.find(item => item.clientKey === row.partNumberKey);
      if (!partNumber || !partNumber.manufacturerPartNumber.trim()) {
        return 'Each order number needs a part number with a manufacturer part number.';
      }
    }

    return '';
  }

  /**
   * Writes part numbers first so new rows receive server ids, then writes the
   * order numbers that point at them. Deletes run in the opposite order.
   */
  private persistNumbers(partId: number): Observable<void> {
    const partNumberDeletes = this.partNumberIdsToDelete();
    const orderDeletes = this.orderNumberIdsToDelete(new Set(partNumberDeletes));

    return this.whenAll(orderDeletes.map(id => this.ignoreMissing(this.orderNumberService.deleteData(id)))).pipe(
      switchMap(() => this.whenAll(partNumberDeletes.map(id => this.ignoreMissing(this.partNumberService.deleteData(id))))),
      switchMap(() => this.savePartNumbers(partId)),
      switchMap(idByKey => this.saveOrderNumbers(idByKey, new Set(orderDeletes)))
    );
  }

  private savePartNumbers(partId: number): Observable<Map<number, number>> {
    const idByKey = new Map<number, number>();
    const requests: Observable<void>[] = [];

    for (const row of this.partNumbers) {
      if (!row.manufacturerPartNumber.trim()) {
        continue;
      }

      const payload = {
        id: row.id >= 0 ? row.id : null,
        partId,
        manufacturerId: row.manufacturerId,
        manufacturerPartNumber: row.manufacturerPartNumber.trim(),
        quantity: row.quantity > 0 ? row.quantity : 1,
        packageType: row.packageType.trim()
      };

      if (row.id >= 0) {
        idByKey.set(row.clientKey, row.id);
        requests.push(this.partNumberService.updateData(row.id, payload).pipe(map(() => undefined)));
      } else {
        requests.push(
          this.partNumberService.postData(payload).pipe(
            tap((created: PartNumber) => {
              row.id = created.id;
              idByKey.set(row.clientKey, created.id);
            }),
            map(() => undefined)
          )
        );
      }
    }

    return this.whenAll(requests).pipe(map(() => idByKey));
  }

  private saveOrderNumbers(idByKey: Map<number, number>, deletedIds: Set<number>): Observable<void> {
    const requests: Observable<unknown>[] = [];

    for (const row of this.orderNumbers) {
      if (row.id >= 0 && deletedIds.has(row.id)) {
        continue;
      }

      const value = row.orderNumberValue.trim();
      const partNumberId = row.partNumberKey === null ? undefined : idByKey.get(row.partNumberKey);
      if (!value || partNumberId === undefined) {
        continue;
      }

      const payload = {
        id: row.id >= 0 ? row.id : null,
        orderNumberValue: value,
        partNumberId,
        distributorId: row.distributorId,
        quantityMin: row.quantityMin > 0 ? row.quantityMin : 1,
        quantitySteps: row.quantitySteps > 0 ? row.quantitySteps : 1
      };

      if (row.id >= 0) {
        requests.push(this.orderNumberService.updateData(row.id, payload));
      } else {
        requests.push(
          this.orderNumberService.postData(payload).pipe(
            tap((created: OrderNumber) => {
              row.id = created.id;
            })
          )
        );
      }
    }

    return this.whenAll(requests).pipe(map(() => undefined));
  }

  private partNumberIdsToDelete(): number[] {
    const ids = new Set(this.removedPartNumberIds);

    for (const row of this.partNumbers) {
      if (row.id >= 0 && !row.manufacturerPartNumber.trim()) {
        ids.add(row.id);
      }
    }

    return [...ids];
  }

  private orderNumberIdsToDelete(partNumberIdsBeingDeleted: Set<number>): number[] {
    const ids = new Set(this.removedOrderNumberIds);
    const deletedKeys = new Set(
      this.partNumbers
        .filter(row => row.id >= 0 && partNumberIdsBeingDeleted.has(row.id))
        .map(row => row.clientKey)
    );

    for (const row of this.orderNumbers) {
      if (row.id < 0) {
        continue;
      }

      const partNumberMissing = row.partNumberKey === null
        || deletedKeys.has(row.partNumberKey)
        || !this.partNumbers.some(partNumber => partNumber.clientKey === row.partNumberKey);

      if (!row.orderNumberValue.trim() || partNumberMissing) {
        ids.add(row.id);
      }
    }

    return [...ids];
  }

  private toPartNumberRow(partNumber: PartNumber): PartNumberRow {
    return {
      id: partNumber.id,
      clientKey: partNumber.id,
      manufacturerId: partNumber.manufacturerId ?? null,
      manufacturerPartNumber: partNumber.manufacturerPartNumber ?? '',
      quantity: partNumber.quantity ?? 1,
      packageType: partNumber.packageType ?? ''
    };
  }

  private toOrderNumberRow(orderNumber: OrderNumber): OrderNumberRow {
    return {
      id: orderNumber.id,
      partNumberKey: orderNumber.partNumberId ?? null,
      distributorId: orderNumber.distributorId ?? null,
      orderNumberValue: orderNumber.orderNumberValue ?? '',
      quantityMin: orderNumber.quantityMin ?? 1,
      quantitySteps: orderNumber.quantitySteps ?? 1
    };
  }

  private whenAll<T>(requests: Observable<T>[]): Observable<T[]> {
    return requests.length ? forkJoin(requests) : of([]);
  }

  /** A repeated save can hit a row that the previous attempt already deleted. */
  private ignoreMissing(request: Observable<unknown>): Observable<unknown> {
    return request.pipe(
      catchError(error => error?.status === 404 ? of(null) : throwError(() => error))
    );
  }

  onLookupQueryInput(value: string): void {
    this.lookupQuery = value;
    this.lookupQueryEdited = true;
  }

  get enabledLookupProviders(): DistributorProviderInfo[] {
    return this.distributorProviders.filter(item => item.enabled);
  }

  get lookupDisabled(): boolean {
    return this.isSaving || this.distributorBusy || !this.selectedEnabledProvider();
  }

  get alternativesDisabled(): boolean {
    return this.distributorBusy || !this.hasManufacturerPartNumber();
  }

  get ordersDisabled(): boolean {
    return this.alternativesDisabled;
  }

  get lookupStatusText(): string {
    if (!this.providersLoaded) {
      return '';
    }

    if (this.enabledLookupProviders.length) {
      return '';
    }

    return 'Enable a distributor API under Admin → APIs.';
  }

  lookupPart(): void {
    if (this.lookupDisabled) {
      return;
    }

    const query = this.lookupQuery.trim() || (this.part.name ?? '').trim();
    if (!query) {
      this.showError('Enter a name to look up.');
      return;
    }

    this.lookingUp = true;
    this.showError('');
    this.distributorSearch.keyword(query, this.selectedLookupProvider).subscribe({
      next: response => {
        this.lookingUp = false;
        this.openDistributorPick('keyword', response.results ?? [], []);
      },
      error: error => {
        this.lookingUp = false;
        this.showError(distributorSearchError(error, 'Could not look up this part.'));
      }
    });
  }

  findAlternatives(): void {
    if (this.alternativesDisabled) {
      return;
    }

    const rows = this.partNumbers.filter(row => row.manufacturerPartNumber.trim());
    this.searchingAlternatives = true;
    this.showError('');
    forkJoin(rows.map(row => this.alternativeCall(row))).subscribe({
      next: outcomes => {
        this.searchingAlternatives = false;
        const failed = outcomes.filter((item): item is { ok: false; error: unknown } => !item.ok);
        const succeeded = outcomes.filter((item): item is { ok: true; response: DistributorMultiResult } => item.ok);
        const warnings = this.uniqueMessages([
          ...failed.map(item => distributorSearchError(item.error, 'Could not find alternatives.')),
          ...succeeded.flatMap(item => (item.response.errors ?? []).map(error => this.providerErrorText(error)))
        ]);
        const rawCount = succeeded.reduce((count, item) => count + (item.response.results?.length ?? 0), 0);
        const everyResponseFailed = succeeded.length > 0
          && succeeded.every(item => (item.response.results?.length ?? 0) === 0 && (item.response.errors?.length ?? 0) > 0);
        if (failed.length === outcomes.length || (rawCount === 0 && everyResponseFailed)) {
          this.showError(warnings.join(' ') || 'Could not find alternatives.');
          return;
        }

        const merged = this.dedupeProducts(succeeded.flatMap(item => item.response.results ?? []));
        this.openDistributorPick('alternatives', merged, warnings);
      },
      error: error => {
        this.searchingAlternatives = false;
        this.showError(distributorSearchError(error, 'Could not find alternatives.'));
      }
    });
  }

  findOrderNumbers(): void {
    if (this.ordersDisabled) {
      return;
    }

    this.searchingOrders = true;
    this.showError('');
    this.distributorSearch.orders(this.orderQueries()).subscribe({
      next: response => {
        this.searchingOrders = false;
        const results = response.results ?? [];
        const warnings = this.uniqueMessages((response.errors ?? []).map(error => this.providerErrorText(error)));
        if (!results.length && warnings.length) {
          this.showError(warnings.join(' '));
          return;
        }

        this.openDistributorPick('orders', results, results.length ? warnings : []);
      },
      error: error => {
        this.searchingOrders = false;
        this.showError(distributorSearchError(error, 'Could not find order numbers.'));
      }
    });
  }

  get distributorBusy(): boolean {
    return this.lookingUp || this.searchingAlternatives || this.searchingOrders || this.downloadingDatasheet || this.importingImage || this.applyingDistributor;
  }

  get distributorBusyText(): string {
    if (this.importingImage) {
      return 'Downloading product image…';
    }
    if (this.downloadingDatasheet) {
      return 'Downloading datasheet…';
    }
    if (this.lookingUp) {
      return 'Searching…';
    }
    if (this.searchingAlternatives) {
      return 'Searching for alternatives…';
    }
    if (this.searchingOrders) {
      return 'Searching for order numbers…';
    }
    return 'Adding part data…';
  }

  private loadLookupProvider(): void {
    this.distributorSearch.getProviders().subscribe({
      next: response => {
        this.distributorProviders = response.providers ?? [];
        this.providersLoaded = true;
        this.applyDefaultLookupProvider(response.lookupProvider);
      },
      error: error => {
        this.providersLoaded = true;
        this.showError(distributorSearchError(error, 'Could not load distributor APIs.'));
      }
    });
  }

  private applyDefaultLookupProvider(lookupProvider: string | null | undefined): void {
    if (this.selectedLookupProvider) {
      return;
    }

    const adminDefault = (lookupProvider ?? '').trim().toLowerCase();
    const match = this.enabledLookupProviders.find(item => item.id.trim().toLowerCase() === adminDefault);
    this.selectedLookupProvider = match?.id ?? '';
  }

  private selectedEnabledProvider(): DistributorProviderInfo | undefined {
    const id = this.selectedLookupProvider.trim().toLowerCase();
    if (!id) {
      return undefined;
    }

    return this.enabledLookupProviders.find(item => item.id.trim().toLowerCase() === id);
  }

  private hasManufacturerPartNumber(): boolean {
    return this.partNumbers.some(row => row.manufacturerPartNumber.trim().length > 0);
  }

  private alternativeCall(row: PartNumberRow): Observable<AlternativeCall> {
    const manufacturer = this.manufacturerName(row.manufacturerId).trim();
    return this.distributorSearch.alternatives(
      row.manufacturerPartNumber.trim(),
      manufacturer || undefined
    ).pipe(
      map(response => ({ ok: true as const, response })),
      catchError((error: unknown) => of({ ok: false as const, error }))
    );
  }

  private orderQueries(): DistributorPartQuery[] {
    const queries: DistributorPartQuery[] = [];
    for (const row of this.partNumbers) {
      const manufacturerPartNumber = row.manufacturerPartNumber.trim();
      if (!manufacturerPartNumber) {
        continue;
      }

      const manufacturer = this.manufacturerName(row.manufacturerId).trim();
      const query: DistributorPartQuery = { manufacturerPartNumber };
      if (manufacturer) {
        query.manufacturer = manufacturer;
      }
      queries.push(query);
    }

    return queries;
  }

  private openDistributorPick(kind: DistributorPickKind, results: DistributorProduct[], warnings: string[]): void {
    const dialogRef = this.dialog.open<
      DistributorPickDialogComponent,
      DistributorPickDialogData,
      KeywordImportChoice | DistributorProduct[] | undefined
    >(DistributorPickDialogComponent, {
      width: '860px',
      maxWidth: '95vw',
      data: { kind, results, warnings: [] }
    });
    if (warnings.length) {
      this.showError('Some distributor searches failed.', warnings);
    }

    dialogRef.afterClosed().subscribe(result => {
      if (result == null) {
        return;
      }

      if (kind === 'keyword' && isKeywordImportChoice(result)) {
        this.applyKeywordProduct(result.product, new Set(result.fields));
        return;
      }

      if (kind === 'alternatives' && Array.isArray(result)) {
        this.applyAlternativeProducts(result);
        return;
      }

      if (kind === 'orders' && Array.isArray(result)) {
        this.applyOrderProducts(result);
      }
    });
  }

  private consumeDistributorProduct(): void {
    const product = this.pendingDistributorProduct;
    if (!product) {
      return;
    }

    this.pendingDistributorProduct = undefined;
    this.applyKeywordProduct(product);
  }

  private applyKeywordProduct(product: DistributorProduct, fields: ReadonlySet<ImportFieldId> | null = null): void {
    const want = (id: ImportFieldId): boolean => fields == null || fields.has(id);
    this.applyNotice = '';
    this.applyKeywordDetails(product, want);
    if (want('image')) {
      this.importProductImage(product);
    }

    const finish = (): void => {
      if (!want('datasheet')) {
        this.applyingDistributor = false;
        return;
      }

      if (fields == null) {
        this.offerDatasheet(product);
        return;
      }

      const url = (product.datasheetUrl ?? '').trim();
      if (!url) {
        this.applyingDistributor = false;
        return;
      }

      this.importDatasheet(url, this.filenameFromUrl(url));
    };

    const manufacturerPartNumber = (product.manufacturerPartNumber ?? '').trim();
    const needsRow = manufacturerPartNumber.length > 0
      && (want('partNumber') || want('package') || want('order'));
    if (!needsRow) {
      if (want('datasheet') && (product.datasheetUrl ?? '').trim()) {
        this.applyingDistributor = true;
      }
      finish();
      return;
    }

    this.applyingDistributor = true;
    this.ensureManufacturer(product.manufacturer).pipe(
      switchMap(manufacturerId => {
        const row = this.addPartNumberRow(product, manufacturerId, want('package'));
        return row && want('order') ? this.addOrderForProduct(row, product) : of(undefined);
      })
    ).subscribe({
      next: () => finish(),
      error: error => {
        this.applyNotice = distributorSearchError(error, 'Could not add the manufacturer or distributor.');
        this.showError(this.applyNotice);
        finish();
      }
    });
  }

  private applyKeywordDetails(
    product: DistributorProduct,
    want: (id: ImportFieldId) => boolean
  ): void {
    if (want('description')) {
      const description = (product.description ?? '').trim();
      if (!(this.part.comment ?? '').trim() && description) {
        this.part.comment = description;
      }
    }

    if (want('status')) {
      const status = product.lifecycleStatus;
      if (status === 'active' || status === 'nrnd' || status === 'ltb' || status === 'eol') {
        this.part.lifecycleStatus = status;
      }
    }
  }

  private applyAlternativeProducts(products: DistributorProduct[]): void {
    if (!products.length) {
      return;
    }

    this.applyingDistributor = true;
    from(products).pipe(
      concatMap(product => this.ensureManufacturer(product.manufacturer).pipe(
        tap(manufacturerId => {
          this.addPartNumberRow(product, manufacturerId);
        }),
        catchError((error: unknown) => {
          this.showError(distributorSearchError(error, 'Could not add the manufacturer.'));
          return of(null);
        })
      ))
    ).subscribe({
      complete: () => {
        this.applyingDistributor = false;
      },
      error: () => {
        this.applyingDistributor = false;
      }
    });
  }

  private applyOrderProducts(products: DistributorProduct[]): void {
    if (!products.length) {
      return;
    }

    this.applyingDistributor = true;
    from(products).pipe(
      concatMap(product => {
        const manufacturerPartNumber = (product.manufacturerPartNumber ?? '').trim().toLowerCase();
        const row = this.partNumbers.find(item =>
          item.manufacturerPartNumber.trim().toLowerCase() === manufacturerPartNumber);
        if (!row) {
          return of(undefined);
        }

        return this.addOrderForProduct(row, product).pipe(
          catchError((error: unknown) => {
            this.showError(distributorSearchError(error, 'Could not add the distributor.'));
            return of(undefined);
          })
        );
      })
    ).subscribe({
      complete: () => {
        this.applyingDistributor = false;
      },
      error: () => {
        this.applyingDistributor = false;
      }
    });
  }

  onImageFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.imageUploading) {
      return;
    }

    if (file.size > this.maxImageBytes) {
      this.showError('The product image is larger than 5 MB.');
      return;
    }

    this.imageUploading = true;
    this.showError('');
    this.showLocalImage(file);
    this.partService.uploadImage(file).subscribe({
      next: result => {
        this.imageUploading = false;
        this.part.imageHash = result.hash;
      },
      error: error => {
        this.imageUploading = false;
        this.clearImage();
        this.showError(distributorSearchError(error, 'Could not upload the product image.'));
      }
    });
  }

  clearImage(): void {
    this.part.imageHash = null;
    this.revokeLocalImage();
    this.imagePreviewUrl = null;
  }

  private loadProductImage(part: Part): void {
    const hash = (part.imageHash ?? '').trim();
    this.revokeLocalImage();
    this.imagePreviewUrl = hash ? this.partService.imageContentUrl(hash) : null;
  }

  private importProductImage(product: DistributorProduct): void {
    const url = (product.imageUrl ?? '').trim();
    if (!url) {
      return;
    }

    this.importingImage = true;
    this.distributorSearch.importImage(url).subscribe({
      next: result => {
        this.importingImage = false;
        this.part.imageHash = result.hash;
        this.revokeLocalImage();
        this.imagePreviewUrl = this.partService.imageContentUrl(result.hash);
      },
      error: error => {
        this.importingImage = false;
        const message = distributorSearchError(error, 'Could not download the product image.');
        this.showError(this.applyNotice ? `${this.applyNotice} ${message}` : message);
      }
    });
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

  private offerDatasheet(product: DistributorProduct): void {
    const url = (product.datasheetUrl ?? '').trim();
    if (!url) {
      this.applyingDistributor = false;
      return;
    }

    const filename = this.filenameFromUrl(url);
    const dialogRef = this.dialog.open(DatasheetDownloadDialogComponent, {
      width: '32rem',
      maxWidth: '95vw',
      data: { label: filename || url }
    });
    dialogRef.afterClosed().subscribe(download => {
      if (download !== true) {
        this.applyingDistributor = false;
        return;
      }

      this.importDatasheet(url, filename);
    });
  }

  private importDatasheet(url: string, filename: string): void {
    this.downloadingDatasheet = true;
    this.applyingDistributor = true;
    this.distributorSearch.datasheet(url, filename || undefined).subscribe({
      next: created => {
        this.downloadingDatasheet = false;
        this.applyingDistributor = false;
        this.attachImportedDatasheet(created);
      },
      error: error => {
        this.downloadingDatasheet = false;
        this.applyingDistributor = false;
        const message = distributorSearchError(error, 'Could not download the datasheet.');
        this.showError(this.applyNotice ? `${this.applyNotice} ${message}` : message);
      }
    });
  }

  private attachImportedDatasheet(created: { id: number; uuid: string; filename: string; directory: string }): void {
    if (!this.datasheetList.some(sheet => sheet.id === created.id)) {
      const sheet: Datasheet = {
        id: created.id,
        uuid: created.uuid ?? '',
        filename: created.filename ?? '',
        directory: created.directory ?? '',
        fileSize: 0,
        comment: null,
        updatedAt: '',
        createdAt: ''
      };
      const source = this.data.datasheetList;
      if (!source.some(item => item.id === sheet.id)) {
        source.push(sheet);
      }
      if (this.datasheetList !== source) {
        this.datasheetList = source.slice();
      }
    }

    if (!this.datasheetIds.includes(created.id)) {
      this.datasheetIds = [...this.datasheetIds, created.id];
    }

    this.refreshDatasheets();
  }

  private addPartNumberRow(
    product: DistributorProduct,
    manufacturerId: number | null,
    includePackage = true
  ): PartNumberRow | null {
    const manufacturerPartNumber = (product.manufacturerPartNumber ?? '').trim();
    if (!manufacturerPartNumber) {
      return null;
    }

    const packageType = includePackage ? (product.package ?? '').trim() : '';
    const existing = this.partNumbers.find(row =>
      row.manufacturerPartNumber.trim().toLowerCase() === manufacturerPartNumber.toLowerCase()
      && row.manufacturerId === manufacturerId);
    if (existing) {
      if (packageType && !existing.packageType.trim()) {
        existing.packageType = packageType;
      }
      return existing;
    }

    const row: PartNumberRow = {
      id: -1,
      clientKey: this.takeClientKey(),
      manufacturerId,
      manufacturerPartNumber,
      quantity: 1,
      packageType
    };
    this.partNumbers = [...this.partNumbers, row];
    return row;
  }

  private addOrderForProduct(row: PartNumberRow, product: DistributorProduct): Observable<void> {
    const orderNumber = (product.orderNumber ?? '').trim();
    const distributorName = (product.distributorName ?? '').trim();
    if (!orderNumber || !distributorName) {
      return of(undefined);
    }

    return this.ensureDistributor(distributorName).pipe(
      tap(distributorId => {
        if (distributorId == null || this.hasOrder(row.clientKey, distributorId, orderNumber)) {
          return;
        }

        this.orderNumbers = [
          ...this.orderNumbers,
          {
            id: -1,
            partNumberKey: row.clientKey,
            distributorId,
            orderNumberValue: orderNumber,
            quantityMin: this.quantityOrOne(product.quantityMin),
            quantitySteps: this.quantityOrOne(product.quantitySteps)
          }
        ];
      }),
      map(() => undefined)
    );
  }

  private ensureManufacturer(name: string | null | undefined): Observable<number | null> {
    const trimmed = (name ?? '').trim();
    if (!trimmed) {
      return of(null);
    }

    const existing = this.manufacturerList.find(item => item.name.trim().toLowerCase() === trimmed.toLowerCase());
    if (existing) {
      return of(existing.id);
    }

    return this.manufacturerService.postData({ name: trimmed, url: '' }).pipe(
      map(created => this.adoptManufacturer(created as Manufacturer, trimmed))
    );
  }

  private ensureDistributor(name: string): Observable<number | null> {
    const trimmed = name.trim();
    if (!trimmed) {
      return of(null);
    }

    const existing = this.distributorList.find(item => item.name.trim().toLowerCase() === trimmed.toLowerCase());
    if (existing) {
      return of(existing.id);
    }

    return this.distributorService.postData({ name: trimmed, url: '' }).pipe(
      map(created => this.adoptDistributor(created as Distributor, trimmed))
    );
  }

  private adoptManufacturer(created: Manufacturer, fallbackName: string): number {
    const row: Manufacturer = {
      id: created.id,
      uuid: created.uuid ?? '',
      name: (created.name ?? '').trim() || fallbackName,
      url: created.url ?? '',
      trustedPartsId: created.trustedPartsId ?? null,
      updatedAt: created.updatedAt ?? '',
      createdAt: created.createdAt ?? ''
    };
    const source = this.data.manufacturerList;
    if (!source.some(item => item.id === row.id)) {
      source.push(row);
    }
    this.manufacturerList = source.slice();
    return row.id;
  }

  private adoptDistributor(created: Distributor, fallbackName: string): number {
    const row: Distributor = {
      id: created.id,
      uuid: created.uuid ?? '',
      name: (created.name ?? '').trim() || fallbackName,
      url: created.url ?? '',
      trustedPartsId: created.trustedPartsId ?? null,
      isFavorite: created.isFavorite ?? false,
      updatedAt: created.updatedAt ?? '',
      createdAt: created.createdAt ?? ''
    };
    const source = this.data.distributorList;
    if (!source.some(item => item.id === row.id)) {
      source.push(row);
    }
    this.distributorList = source.slice();
    return row.id;
  }

  private hasOrder(partNumberKey: number, distributorId: number, orderNumber: string): boolean {
    const wanted = orderNumber.trim().toLowerCase();
    return this.orderNumbers.some(row =>
      row.partNumberKey === partNumberKey
      && row.distributorId === distributorId
      && row.orderNumberValue.trim().toLowerCase() === wanted);
  }

  private dedupeProducts(products: DistributorProduct[]): DistributorProduct[] {
    const seen = new Set<string>();
    const merged: DistributorProduct[] = [];
    for (const product of products) {
      const manufacturerPartNumber = (product.manufacturerPartNumber ?? '').trim();
      if (!manufacturerPartNumber) {
        continue;
      }

      const manufacturer = (product.manufacturer ?? '').trim().toLowerCase();
      const key = `${manufacturer}\0${manufacturerPartNumber.toLowerCase()}`;
      if (seen.has(key) || this.partHasMpn(manufacturerPartNumber, product.manufacturer)) {
        continue;
      }

      seen.add(key);
      merged.push(product);
    }

    return merged;
  }

  private partHasMpn(manufacturerPartNumber: string, manufacturer: string | null | undefined): boolean {
    const wantedMpn = manufacturerPartNumber.trim().toLowerCase();
    const wantedName = (manufacturer ?? '').trim().toLowerCase();
    return this.partNumbers.some(row =>
      row.manufacturerPartNumber.trim().toLowerCase() === wantedMpn
      && this.manufacturerName(row.manufacturerId).trim().toLowerCase() === wantedName);
  }

  private manufacturerName(id: number | null): string {
    if (id == null) {
      return '';
    }

    return this.manufacturerList.find(item => item.id === id)?.name ?? '';
  }

  private quantityOrOne(value: number | null | undefined): number {
    return value != null && value > 0 ? value : 1;
  }

  private takeClientKey(): number {
    const key = this.nextClientKey;
    this.nextClientKey -= 1;
    return key;
  }

  private filenameFromUrl(url: string): string {
    try {
      const path = new URL(url).pathname;
      const name = decodeURIComponent(path.split('/').filter(part => part.length > 0).pop() ?? '');
      return name && !name.includes('..') ? name : '';
    } catch {
      return '';
    }
  }

  private providerErrorText(error: DistributorProviderError): string {
    const provider = (error.provider ?? '').trim();
    const message = (error.message ?? '').trim();
    if (provider && message) {
      return `${provider}: ${message}`;
    }

    return message || provider || 'A distributor search failed.';
  }

  private uniqueMessages(messages: string[]): string[] {
    const seen = new Set<string>();
    const unique: string[] = [];
    for (const message of messages) {
      const text = message.trim();
      if (!text || seen.has(text)) {
        continue;
      }

      seen.add(text);
      unique.push(text);
    }

    return unique;
  }
}
