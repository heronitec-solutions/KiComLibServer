export interface PartNumber {
  id: number;
  uuid?: string;
  partId: number;
  manufacturerId: number;
  manufacturerPartNumber: string;
  quantity: number;
  packageType: string;
  updatedAt?: string;
  createdAt?: string;
}
