import { YesNoFilter } from '../../../shared/search-filter';

export interface DistributorFilter {
  search: string;
  favorite: YesNoFilter;
}

export const EMPTY_DISTRIBUTOR_FILTER: DistributorFilter = {
  search: '',
  favorite: 'all',
};
