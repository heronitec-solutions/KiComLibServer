import { Injectable } from "@angular/core";
import { Distributor } from "../types/distributor";
import { DistributorFilter, EMPTY_DISTRIBUTOR_FILTER } from "../types/distributor-filter";
import { DistributorService } from "./distributor.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { matchesText, matchesYesNo } from "../../../shared/search-filter";

@Injectable()
export class DistributorDataSource extends PagedDataSource<Distributor> {
  private filter: DistributorFilter = { ...EMPTY_DISTRIBUTOR_FILTER };

  constructor(private distributorService: DistributorService) {
    super();
  }

  loadDistributors(sort: Sort): void {
    this.isLoading$.next(true);
    this.distributorService.getDistributors(sort).subscribe(distributors => {
      this.setRows(distributors);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: DistributorFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  /** Re-applies the current filter after a row was edited in place. */
  refreshView(): void {
    this.refresh();
  }

  protected override applyFilter(rows: Distributor[]): Distributor[] {
    return rows.filter(row =>
      matchesText(this.filter.search, [row.name, row.url, row.trustedPartsId])
      && matchesYesNo(this.filter.favorite, row.isFavorite));
  }
}
