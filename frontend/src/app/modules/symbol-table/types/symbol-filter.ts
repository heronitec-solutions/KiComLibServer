import { YesNoFilter } from '../../../shared/search-filter';

export interface SymbolFilter {
  search: string;
  libraryFileId: number | 'all';
  inBom: YesNoFilter;
}

export const EMPTY_SYMBOL_FILTER: SymbolFilter = {
  search: '',
  libraryFileId: 'all',
  inBom: 'all',
};
