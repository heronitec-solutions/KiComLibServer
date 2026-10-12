import { DataSource } from "@angular/cdk/collections";
import { Injectable } from "@angular/core";
import { BehaviorSubject, Observable } from "rxjs";
import { OrderNumber } from "../types/order-number";
import { OrderNumberService } from "./order-number.service";
import { Sort } from "@angular/material/sort";
import { MatPaginator } from "@angular/material/paginator";

@Injectable()
export class OrderNumberDataSource extends DataSource<OrderNumber> {
  orderNumbers$ = new BehaviorSubject<OrderNumber[]>([]);
  isLoading$ = new BehaviorSubject<boolean>(false);
  paginator!: MatPaginator;

  constructor(private orderNumberService: OrderNumberService) {
    super();
  }

  connect(): Observable<OrderNumber[]> {
    return this.orderNumbers$.asObservable();
  }

  disconnect(): void {
    this.orderNumbers$.complete();
  }

  loadOrderNumbers(
    sort: Sort,
    filerPartNumberId: number | undefined = undefined,
    filerDistributorId: number | undefined = undefined
  ): void {
    this.isLoading$.next(true);
    this.orderNumberService.getOrderNumbers(sort, filerPartNumberId, filerDistributorId).subscribe(orderNumbers => {
      this.orderNumbers$.next(orderNumbers);
      this.isLoading$.next(false);
    });
  }
}
