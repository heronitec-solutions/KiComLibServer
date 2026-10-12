export interface LibraryFile {
  id: number;
  uuid: string;
  type: number;
  name: string;
  comment: string;
  filename: string;
  description: string;
  version: string;
  updatedAt: string;
  createdAt: string;
  footprints: object[];
  symbols: object[];
}
