/** Entity kinds that participate in versioning. */
export type VersionedEntity =
  | 'symbol'
  | 'footprint'
  | 'model3d'
  | 'datasheet'
  | 'template'
  | 'part'
  | 'manufacturer'
  | 'distributor'
  | 'partnumber'
  | 'ordernumber';

export type CommitOp = 'create' | 'update' | 'rename' | 'delete';

/** Row from GET /api/commit?_limit&_offset */
export interface CommitSummary {
  id: number;
  uuid: string;
  message: string;
  author: string;
  client: string | null;
  createdAt: string;
  changeCount: number;
}

/** One change entry inside a commit detail. */
export interface CommitChange {
  entity: VersionedEntity | string;
  uuid: string;
  id: number;
  name: string;
  op: CommitOp | string;
  revisionId: number;
  revisionNo: number;
  previousRevisionId: number | null;
  isHead?: boolean;
}

/** Response from GET /api/commit/{id} */
export interface CommitDetail {
  id: number;
  uuid: string;
  message: string;
  author: string;
  client: string | null;
  parentCommitId: number | null;
  createdAt: string;
  changes: CommitChange[];
}

/** One revision from GET /api/{entity}/{id}/history */
export interface RevisionInfo {
  revisionId: number;
  revisionNo: number;
  isDeleted: boolean;
  name?: string;
  filename?: string;
  directory?: string;
  libraryFileId?: number;
  hash: string | null;
  previewBlobHash?: string | null;
  commitId: number;
  commitUuid: string;
  message: string;
  author: string;
  client: string | null;
  createdAt: string;
}

/** Metadata side of a binary diff. */
export interface BinaryDiffSide {
  hash: string | null;
  size: number | null;
  filename: string | null;
  directory: string | null;
}

export interface UnifiedDiffResult {
  format: 'unified';
  diff: string;
}

export interface BinaryDiffResult {
  format: 'binary';
  from: BinaryDiffSide | null;
  to: BinaryDiffSide | null;
}

export type DiffResult = UnifiedDiffResult | BinaryDiffResult;

/** One entry from GET /api/commit/{id}/diff */
export interface CommitDiffEntry {
  entity: VersionedEntity | string;
  id: number;
  revisionNo: number;
  diff: DiffResult;
}

export function isUnifiedDiff(diff: DiffResult | null | undefined): diff is UnifiedDiffResult {
  return !!diff && diff.format === 'unified';
}

export function isBinaryDiff(diff: DiffResult | null | undefined): diff is BinaryDiffResult {
  return !!diff && diff.format === 'binary';
}
