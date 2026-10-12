/** Case-insensitive substring match against any of the given fields. */
export function matchesText(query: string, fields: Array<string | number | null | undefined>): boolean {
  const term = query.trim().toLowerCase();
  if (!term) {
    return true;
  }
  return fields.some(field => String(field ?? '').toLowerCase().includes(term));
}

export interface SearchFilter {
  search: string;
}

export const EMPTY_SEARCH_FILTER: SearchFilter = { search: '' };

export type YesNoFilter = 'all' | 'yes' | 'no';

export function matchesYesNo(filter: YesNoFilter, value: boolean): boolean {
  return filter === 'all' || (filter === 'yes' ? value : !value);
}

/** `'all'` or an exact directory path taken from the loaded rows. */
export interface DirectoryFilter {
  search: string;
  directory: string;
}

export const EMPTY_DIRECTORY_FILTER: DirectoryFilter = {
  search: '',
  directory: 'all',
};

export function uniqueSorted(values: Array<string | null | undefined>): string[] {
  return [...new Set(values.map(value => (value ?? '').trim()).filter(value => value.length > 0))]
    .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
}
