import { YesNoFilter } from '../../../shared/search-filter';

export interface FootprintFilter {
  search: string;
  libraryFileId: number | 'all';
  boardOnly: YesNoFilter;
  layer: string;
}

export const EMPTY_FOOTPRINT_FILTER: FootprintFilter = {
  search: '',
  libraryFileId: 'all',
  boardOnly: 'all',
  layer: 'all',
};
