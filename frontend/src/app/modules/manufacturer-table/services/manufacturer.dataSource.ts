import { Injectable } from "@angular/core";
import { Manufacturer } from "../types/manufacturer";
import { ManufacturerService } from "./manufacturer.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { EMPTY_SEARCH_FILTER, matchesText, SearchFilter } from "../../../shared/search-filter";

@Injectable()
export class ManufacturerDataSource extends PagedDataSource<Manufacturer> {
  private filter: SearchFilter = { ...EMPTY_SEARCH_FILTER };

  constructor(private manufacturerService: ManufacturerService) {
    super();
  }

  loadManufacturers(sort: Sort): void {
    this.isLoading$.next(true);
    this.manufacturerService.getManufacturers(sort).subscribe(manufacturers => {
      this.setRows(manufacturers);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: SearchFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: Manufacturer[]): Manufacturer[] {
    return rows.filter(row => matchesText(this.filter.search, [row.name, row.url, row.trustedPartsId]));
  }
}
