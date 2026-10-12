export type ProjectStatus = 'development' | 'released' | 'obsolete';
export type ExportScope = 'project_dir' | 'repository';
export type AnalysisStatus = 'pending' | 'running' | 'done' | 'failed' | 'unsupported';
export type BomStatus = 'ok' | 'eol' | 'not_in_catalog' | 'ambiguous' | 'field_outdated' | 'dnp';
export type BomGroup = 'part' | 'none';

export interface ProjectPermissions {
  canWrite: boolean;
  canAdmin: boolean;
}

export interface ProjectSummary {
  id: number;
  name: string;
  status: ProjectStatus;
  customer: string;
  ownerName: string;
  revisionCount: number;
  headComponentCount: number;
  warningCount: number;
}

export interface ProjectDetail extends ProjectSummary {
  description: string;
  repositoryId: number | null;
  ownerUserId: number | null;
  projectFile: string;
  exportScope: ExportScope;
  headSha: string;
  webUrl: string;
  defaultBranch: string;
  permissions: ProjectPermissions;
  warnings: string[];
}

export interface ProjectWrite {
  name: string;
  description: string;
  status: ProjectStatus;
  customer: string;
  ownerUserId: number | null;
  exportScope: ExportScope;
}

export interface ProjectCreate extends ProjectWrite {
  repositoryId: number;
  projectFile: string;
}

export interface ProjectAccessEntry {
  groupId: number;
  groupName: string;
  canWrite: boolean;
}

export interface ProjectRevision {
  id: number;
  commitSha: string;
  message: string;
  authorName: string;
  committedAt: string;
  refs: string[];
  reachable: boolean;
  analysisStatus: string;
  analysisError: string;
  componentCount: number;
  warningCount: number;
}

export interface RevisionPageInfo {
  page: string;
  sheetNames: string;
  file: string;
  paper: string;
}

export interface RevisionDetail extends ProjectRevision {
  title: string;
  revisionLabel: string;
  pages: RevisionPageInfo[];
  warnings: string[];
}

export interface RevisionQuery {
  limit: number;
  offset: number;
  ref?: string;
  includeUnreachable?: boolean;
}

export interface RevisionPage {
  items: ProjectRevision[];
  total: number;
}

export interface BomLine {
  reference: string;
  value: string;
  footprint: string;
  mpn: string;
  manufacturer: string;
  quantity: number;
  status: BomStatus | BomStatus[];
  dnp: boolean;
  partId: number | null;
  partName: string;
  outdatedFields: string[];
}

export interface BomFieldChange {
  [field: string]: string[];
}

export interface BomDiffItem {
  reference: string;
  value: string;
  status: string;
  fromValue: string;
  toValue: string;
  toStatus: string;
  changes: BomFieldChange;
  partId: number | null;
}

export interface BomDiff {
  base: string;
  target: string;
  summary: {
    added: number;
    removed: number;
    replaced: number;
    changed: number;
    renamed: number;
  };
  added: BomDiffItem[];
  removed: BomDiffItem[];
  replaced: BomDiffItem[];
  changed: BomDiffItem[];
  renamed: BomDiffItem[];
}

export interface PartUsage {
  projectId: number;
  projectName: string;
  revision: string;
  release: string;
  quantity: number;
}

export interface ReleaseArtifact {
  id: number;
  kind: string;
  fileName: string;
  variant: string;
}

export interface CheckSummary {
  errors: number;
  warnings: number;
}

export interface ProjectRelease {
  id: number;
  name: string;
  tag: string;
  sha: string;
  notes: string;
  state: string;
  releasedAt: string;
  artifacts: ReleaseArtifact[];
  erc: CheckSummary | null;
  drc: CheckSummary | null;
  outputsStatus: string;
  outputsError: string;
}

export interface ReleaseWrite {
  name: string;
  notes: string;
  sha: string;
  tag: string;
}

export type VisualLayerStatus = 'added' | 'removed' | 'changed' | 'moved';
export type VisualLayerCategory = 'parts' | 'wiring' | 'graphics' | 'routing' | 'zones';

export interface VisualDiffLayer {
  status: string;
  category: string;
  file: string;
  aligned: boolean;
}

export interface VisualDiffChange {
  status: string;
  kind: string;
  key: string;
  reference: string;
  value: string;
  at: number[][];
  catalogStatus: string;
  detail: { [field: string]: string[] };
}

export interface VisualDiffPage {
  id: string;
  kind: string;
  page: string;
  side: string;
  sheetNames: string;
  file: string;
  viewBox: number[];
  crop: number[] | null;
  base: string | null;
  target: string | null;
  only: string;
  aligned: boolean;
  layers: VisualDiffLayer[];
  changes: VisualDiffChange[];
}

export interface VisualDiffManifest {
  version: number;
  kicad: string;
  base: string;
  target: string;
  summary: { [category: string]: { [status: string]: number } };
  pages: VisualDiffPage[];
}

export interface VisualDiffEnvelope {
  id?: string | number;
  uuid?: string;
  status?: string;
  error?: string;
  manifest?: VisualDiffManifest;
  pages?: VisualDiffPage[];
  version?: number;
  kicad?: string;
  base?: string;
  target?: string;
  summary?: VisualDiffManifest['summary'];
}

export interface ZoomTarget {
  x: number;
  y: number;
  scale: number;
}

export interface SheetNode {
  name: string;
  changeCount: number;
  pageId?: string;
  children: SheetNode[];
}
