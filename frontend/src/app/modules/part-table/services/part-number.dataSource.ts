import { DataSource } from "@angular/cdk/collections";
import { Injectable } from "@angular/core";
import { BehaviorSubject, Observable } from "rxjs";
import { PartNumber } from "../types/part-number";
import { PartNumberService } from "./part-number.service";
import { Sort } from "@angular/material/sort";
import { MatPaginator } from "@angular/material/paginator";

@Injectable()
export class PartNumberDataSource extends DataSource<PartNumber> {
  partNumbers$ = new BehaviorSubject<PartNumber[]>([]);
  isLoading$ = new BehaviorSubject<boolean>(false);
  paginator!: MatPaginator;

  constructor(private partNumberService: PartNumberService) {
    super();
  }

  connect(): Observable<PartNumber[]> {
    return this.partNumbers$.asObservable();
  }

  disconnect(): void {
    this.partNumbers$.complete();
  }

  loadPartNumbers(
    sort: Sort,
    filerPartId: number | undefined = undefined,
    filerManufacturerId: number | undefined = undefined
  ): void {
    this.isLoading$.next(true);
    this.partNumberService.getPartNumbers(sort, filerPartId, filerManufacturerId).subscribe(partNumbers => {
      this.partNumbers$.next(partNumbers);
      this.isLoading$.next(false);
    });
  }
}
