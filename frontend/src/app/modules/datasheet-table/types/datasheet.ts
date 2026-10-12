export interface Datasheet {
  id: number;
  uuid: string;
  filename: string;
  fileSize: number;
  directory: string;
  comment: string | null;
  hash?: string;
  fileHash?: string;
  revisionNo?: number;
  headRevisionId?: number;
  updatedAt: string;
  createdAt: string;
}
