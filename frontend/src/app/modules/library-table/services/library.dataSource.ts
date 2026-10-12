import { Injectable } from "@angular/core";
import { Library } from "../types/library";
import { LibraryService } from "./library.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { EMPTY_SEARCH_FILTER, matchesText, SearchFilter } from "../../../shared/search-filter";

@Injectable()
export class LibraryDataSource extends PagedDataSource<Library> {
  private filter: SearchFilter = { ...EMPTY_SEARCH_FILTER };

  constructor(private libraryService: LibraryService) {
    super();
  }

  loadLibraries(sort: Sort): void {
    this.isLoading$.next(true);
    this.libraryService.getLibraries(sort).subscribe(libraries => {
      this.setRows(libraries);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: SearchFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: Library[]): Library[] {
    return rows.filter(row => matchesText(this.filter.search, [row.name, row.filename, row.description]));
  }
}
