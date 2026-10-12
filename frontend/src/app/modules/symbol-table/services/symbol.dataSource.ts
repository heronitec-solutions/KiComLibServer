import { Injectable } from "@angular/core";
import { Symbol } from "../types/symbol";
import { EMPTY_SYMBOL_FILTER, SymbolFilter } from "../types/symbol-filter";
import { SymbolService } from "./symbol.service";
import { Sort } from "@angular/material/sort";
import { PagedDataSource } from "../../../shared/paged-data-source";
import { matchesText, matchesYesNo } from "../../../shared/search-filter";

@Injectable()
export class SymbolDataSource extends PagedDataSource<Symbol> {
  private filter: SymbolFilter = { ...EMPTY_SYMBOL_FILTER };

  constructor(private symbolService: SymbolService) {
    super();
  }

  loadSymbols(sort: Sort): void {
    this.isLoading$.next(true);
    this.symbolService.getSymbols(sort).subscribe(symbols => {
      this.setRows(symbols);
      this.isLoading$.next(false);
    });
  }

  setFilter(filter: SymbolFilter): void {
    this.filter = filter;
    this.refresh({ resetPage: true });
  }

  protected override applyFilter(rows: Symbol[]): Symbol[] {
    return rows.filter(row => {
      const matchesSearch = matchesText(this.filter.search, [row.name, row.description, row.reference]);
      const matchesLibrary = this.filter.libraryFileId === 'all' || row.libraryFileId === this.filter.libraryFileId;
      return matchesSearch && matchesLibrary && matchesYesNo(this.filter.inBom, row.inBom);
    });
  }
}
