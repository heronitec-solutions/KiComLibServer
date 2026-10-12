import { Injectable } from "@angular/core";
import { CadModel } from "../types/cad-model";
import { CadModelService } from "./cad-model.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { DirectoryFilter, EMPTY_DIRECTORY_FILTER, matchesText } from "../../../shared/search-filter";

@Injectable()
export class CadModelDataSource extends PagedDataSource<CadModel> {
  private filter: DirectoryFilter = { ...EMPTY_DIRECTORY_FILTER };

  constructor(private cadModelService: CadModelService) {
    super();
  }

  loadCadModels(sort: Sort): void {
    this.isLoading$.next(true);
    this.cadModelService.getCadModels(sort).subscribe(cadModels => {
      this.setRows(cadModels);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: DirectoryFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: CadModel[]): CadModel[] {
    return rows.filter(row => {
      const matchesSearch = matchesText(this.filter.search, [
        row.filename, row.directory, row.comment, row.hash, row.fileHash
      ]);
      const matchesDirectory = this.filter.directory === 'all' || row.directory === this.filter.directory;
      return matchesSearch && matchesDirectory;
    });
  }
}
