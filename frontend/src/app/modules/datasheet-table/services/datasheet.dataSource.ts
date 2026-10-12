import { Injectable } from "@angular/core";
import { Datasheet } from "../types/datasheet";
import { DatasheetService } from "./datasheet.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { DirectoryFilter, EMPTY_DIRECTORY_FILTER, matchesText } from "../../../shared/search-filter";

@Injectable()
export class DatasheetDataSource extends PagedDataSource<Datasheet> {
  private filter: DirectoryFilter = { ...EMPTY_DIRECTORY_FILTER };

  constructor(private datasheetService: DatasheetService) {
    super();
  }

  loadDatasheets(sort: Sort): void {
    this.isLoading$.next(true);
    this.datasheetService.getDatasheets(sort).subscribe(datasheets => {
      this.setRows(datasheets);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: DirectoryFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: Datasheet[]): Datasheet[] {
    return rows.filter(row => {
      const matchesSearch = matchesText(this.filter.search, [
        row.filename, row.directory, row.comment, row.hash, row.fileHash
      ]);
      const matchesDirectory = this.filter.directory === 'all' || row.directory === this.filter.directory;
      return matchesSearch && matchesDirectory;
    });
  }
}
