import { Injectable } from "@angular/core";
import { LibraryFile } from "../types/library-file";
import { EMPTY_LIBRARY_FILE_FILTER, LibraryFileFilter } from "../types/library-file-filter";
import { LibraryFileService } from "./library-file.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { matchesText } from "../../../shared/search-filter";

@Injectable()
export class LibraryFileDataSource extends PagedDataSource<LibraryFile> {
  private filter: LibraryFileFilter = { ...EMPTY_LIBRARY_FILE_FILTER };

  constructor(private libraryFileService: LibraryFileService) {
    super();
  }

  loadLibraryFiles(sort: Sort): void {
    this.isLoading$.next(true);
    this.libraryFileService.getLibraryFiles(sort).subscribe(libraryFiles => {
      this.setRows(libraryFiles);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: LibraryFileFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: LibraryFile[]): LibraryFile[] {
    return rows.filter(row => {
      const kindLabel = row.type === 0 ? 'footprint' : row.type === 1 ? 'symbol' : '';
      const matchesSearch = matchesText(this.filter.search, [
        row.name, row.comment, row.filename, row.description, row.version, kindLabel
      ]);
      const matchesKind = this.filter.kind === 'all'
        || (this.filter.kind === 'footprint' ? row.type === 0 : row.type === 1);
      return matchesSearch && matchesKind;
    });
  }
}
