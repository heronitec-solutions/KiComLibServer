export type LibraryFileKindFilter = 'all' | 'symbol' | 'footprint';

export interface LibraryFileFilter {
  search: string;
  kind: LibraryFileKindFilter;
}

export const EMPTY_LIBRARY_FILE_FILTER: LibraryFileFilter = {
  search: '',
  kind: 'all',
};
