import { Injectable } from "@angular/core";
import { Sort } from "@angular/material/sort";

import { Footprint } from "../types/footprint";
import { EMPTY_FOOTPRINT_FILTER, FootprintFilter } from "../types/footprint-filter";
import { FootprintService } from "./footprint.service";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { matchesText, matchesYesNo } from "../../../shared/search-filter";

@Injectable()
export class FootprintDataSource extends PagedDataSource<Footprint> {
  private filter: FootprintFilter = { ...EMPTY_FOOTPRINT_FILTER };

  constructor(private footprintService: FootprintService) {
    super();
  }

  loadFootprints(sort: Sort): void {
    this.isLoading$.next(true);
    this.footprintService.getFootprints(sort).subscribe({
      next: footprints => {
        this.setRows(footprints);
        this.isLoading$.next(false);
      },
      error: () => {
        this.setRows([]);
        this.isLoading$.next(false);
      }
    });
  }

  setFilter(filter: FootprintFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: Footprint[]): Footprint[] {
    return rows.filter(row => {
      const matchesSearch = matchesText(this.filter.search, [row.name, row.description, row.tags, row.layer]);
      const matchesLibrary = this.filter.libraryFileId === 'all' || row.libraryFileId === this.filter.libraryFileId;
      const matchesLayer = this.filter.layer === 'all' || row.layer === this.filter.layer;
      return matchesSearch && matchesLibrary && matchesYesNo(this.filter.boardOnly, row.boardOnly) && matchesLayer;
    });
  }
}
