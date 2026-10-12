export interface WarehouseBatch {
  id: number;
  name: string;
  quantity: number;
  location: string;
}

export interface WarehouseItem {
  id: number;
  partId: number | null;
  name: string;
  value: string;
  comment: string;
  hasImage?: boolean;
  imageHash?: string | null;
  quantity: number;
  locations: string;
  batches: WarehouseBatch[];
  updatedAt?: string;
  createdAt?: string;
}

export interface WarehouseBatchWrite {
  name: string;
  quantity: number;
  location: string;
}

export interface WarehouseWrite {
  partId: number | null;
  name: string;
  value: string;
  comment: string;
  batches: WarehouseBatchWrite[];
  /** Set a hash to store a photo, "" to remove it. Omit to leave an existing photo unchanged. */
  imageHash?: string | null;
}

export type WarehouseStatusFilter = 'all' | 'stock' | 'empty' | 'ordered';

export interface WarehouseFilter {
  search: string;
  status: WarehouseStatusFilter;
}

export const EMPTY_WAREHOUSE_FILTER: WarehouseFilter = {
  search: '',
  status: 'all'
};

export const BATCH_SUGGESTIONS = ['Ordered', 'Received', 'In Stock', 'Reserved'];
