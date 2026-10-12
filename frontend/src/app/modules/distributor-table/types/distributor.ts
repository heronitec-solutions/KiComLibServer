export interface Distributor {
  id: number;
  uuid: string;
  name: string;
  url: string;
  trustedPartsId: string | null;
  isFavorite: boolean;
  updatedAt: string;
  createdAt: string;
}
