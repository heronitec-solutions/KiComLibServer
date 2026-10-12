export interface OrderNumber {
  id: number;
  uuid?: string;
  orderNumberValue: string;
  partNumberId: number;
  distributorId: number;
  quantityMin: number;
  quantitySteps: number;
  updatedAt?: string;
  createdAt?: string;
}
