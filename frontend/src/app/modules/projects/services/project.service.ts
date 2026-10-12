import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import {
  asRecord,
  readBool,
  readCount,
  readNullableNumber,
  readNullableString,
  readNumber,
  readString,
  readStringList
} from '../../../shared/json-read';
import {
  BomDiff,
  BomDiffItem,
  BomFieldChange,
  BomGroup,
  BomLine,
  BomStatus,
  ExportScope,
  PartUsage,
  ProjectAccessEntry,
  ProjectCreate,
  ProjectDetail,
  ProjectRelease,
  ReleaseArtifact,
  CheckSummary,
  ProjectStatus,
  ProjectSummary,
  ProjectWrite,
  ReleaseWrite,
  RevisionDetail,
  RevisionPage,
  RevisionPageInfo,
  RevisionQuery,
  ProjectRevision,
  VisualDiffChange,
  VisualDiffEnvelope,
  VisualDiffLayer,
  VisualDiffManifest,
  VisualDiffPage
} from '../types/project';

@Injectable({
  providedIn: 'root'
})
export class ProjectService {
  // Kept off main.ts so specs can load these components without a module cycle.
  private readonly apiUrl = '/api';

  constructor(private http: HttpClient) { }

  list(status: string, q: string): Observable<ProjectSummary[]> {
    let params = new HttpParams();
    if (status && status !== 'all') {
      params = params.set('status', status);
    }
    if (q.trim()) {
      params = params.set('q', q.trim());
    }
    return this.http.get<unknown>(`${this.apiUrl}/project`, { params }).pipe(
      map(body => readArray(body).map(normalizeProject))
    );
  }

  get(id: number): Observable<ProjectDetail> {
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}`).pipe(
      map(body => normalizeProjectDetail(body))
    );
  }

  create(body: ProjectCreate): Observable<ProjectDetail> {
    return this.http.post<unknown>(`${this.apiUrl}/project`, {
      repositoryId: body.repositoryId,
      projectFile: body.projectFile,
      name: body.name || null,
      status: body.status,
      customer: body.customer || null,
      ownerUserId: body.ownerUserId,
      description: body.description || null,
      exportScope: body.exportScope
    }).pipe(map(response => normalizeProjectDetail(response)));
  }

  update(id: number, body: ProjectWrite): Observable<ProjectDetail> {
    return this.http.put<unknown>(`${this.apiUrl}/project/${id}`, {
      name: body.name,
      description: body.description || null,
      status: body.status,
      customer: body.customer || null,
      ownerUserId: body.ownerUserId,
      exportScope: body.exportScope
    }).pipe(map(response => normalizeProjectDetail(response)));
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/project/${id}`);
  }

  getAccess(id: number): Observable<ProjectAccessEntry[]> {
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}/access`).pipe(
      map(body => readArray(body).map(normalizeAccess))
    );
  }

  saveAccess(id: number, entries: { groupId: number; canWrite: boolean }[]): Observable<ProjectAccessEntry[]> {
    return this.http.put<unknown>(`${this.apiUrl}/project/${id}/access`, entries).pipe(
      map(body => readArray(body).map(normalizeAccess))
    );
  }

  revisions(id: number, query: RevisionQuery): Observable<RevisionPage> {
    let params = new HttpParams()
      .set('_limit', String(query.limit))
      .set('_offset', String(query.offset));
    if (query.ref?.trim()) {
      params = params.set('ref', query.ref.trim());
    }
    if (query.includeUnreachable) {
      params = params.set('includeUnreachable', 'true');
    }
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}/revision`, { params, observe: 'response' }).pipe(
      map(response => normalizeRevisionPage(response, query))
    );
  }

  revision(id: number, sha: string): Observable<RevisionDetail> {
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}/revision/${encodeURIComponent(sha)}`).pipe(
      map(body => normalizeRevisionDetail(body))
    );
  }

  analyze(id: number, sha: string): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/project/${id}/revision/${encodeURIComponent(sha)}/analyze`, {});
  }

  rematch(id: number, sha: string): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/project/${id}/revision/${encodeURIComponent(sha)}/rematch`, {});
  }

  bom(id: number, sha: string, group: BomGroup): Observable<BomLine[]> {
    const params = new HttpParams().set('group', group);
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}/revision/${encodeURIComponent(sha)}/bom`, { params }).pipe(
      map(body => readArray(body, 'lines', 'items', 'components').map(normalizeBomLine))
    );
  }

  pageUrl(id: number, sha: string, page: string): string {
    return `${this.apiUrl}/project/${id}/revision/${encodeURIComponent(sha)}/page/${encodeURIComponent(page)}.svg`;
  }

  bomDiff(id: number, base: string, target: string): Observable<BomDiff> {
    const params = new HttpParams().set('base', base).set('target', target);
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}/diff`, { params }).pipe(
      map(body => normalizeBomDiff(body))
    );
  }

  requestVisualDiff(id: number, base: string, target: string): Observable<HttpResponse<VisualDiffEnvelope>> {
    return this.http.post<VisualDiffEnvelope>(
      `${this.apiUrl}/project/${id}/visual-diff`,
      { base, target },
      { observe: 'response' }
    );
  }

  visualDiff(id: number, diffId: string): Observable<VisualDiffEnvelope> {
    return this.http.get<VisualDiffEnvelope>(
      `${this.apiUrl}/project/${id}/visual-diff/${encodeURIComponent(diffId)}`
    );
  }

  visualDiffFileUrl(id: number, diffId: string, path: string): string {
    const encoded = path.split('/').map(segment => encodeURIComponent(segment)).join('/');
    return `${this.apiUrl}/project/${id}/visual-diff/${encodeURIComponent(diffId)}/file/${encoded}`;
  }

  usage(partId: number): Observable<PartUsage[]> {
    return this.http.get<unknown>(`${this.apiUrl}/project/usage/part/${partId}`).pipe(
      map(body => readArray(body).map(normalizeUsage))
    );
  }

  /**
   * A missing route is a 404 and the caller shows an empty state.
   */
  releases(id: number): Observable<ProjectRelease[]> {
    return this.http.get<unknown>(`${this.apiUrl}/project/${id}/release`).pipe(
      map(body => readArray(body).map(normalizeRelease))
    );
  }

  createRelease(id: number, body: ReleaseWrite): Observable<ProjectRelease> {
    const payload: { name: string; notes: string | null; sha?: string; tag?: string } = {
      name: body.name,
      notes: body.notes || null
    };
    if (body.sha.trim()) {
      payload.sha = body.sha.trim();
    }
    if (body.tag.trim()) {
      payload.tag = body.tag.trim();
    }
    return this.http.post<unknown>(`${this.apiUrl}/project/${id}/release`, payload).pipe(
      map(response => normalizeRelease(response))
    );
  }

  updateRelease(id: number, releaseId: number, body: { notes?: string; state?: string }): Observable<ProjectRelease> {
    return this.http.put<unknown>(`${this.apiUrl}/project/${id}/release/${releaseId}`, body).pipe(
      map(response => normalizeRelease(response))
    );
  }

  downloadArtifact(id: number, artifactId: number): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/project/${id}/artifact/${artifactId}`, { responseType: 'blob' });
  }
}

function readArray(body: unknown, ...nested: string[]): Record<string, unknown>[] {
  if (Array.isArray(body)) {
    return body.map(asRecord).filter((row): row is Record<string, unknown> => !!row);
  }
  const row = asRecord(body);
  if (!row) {
    return [];
  }
  for (const key of nested.length ? nested : ['items', 'projects', 'value', 'results']) {
    const value = row[key];
    if (Array.isArray(value)) {
      return value.map(asRecord).filter((item): item is Record<string, unknown> => !!item);
    }
  }
  return [];
}

function projectStatus(value: string): ProjectStatus {
  if (value === 'released' || value === 'obsolete' || value === 'development') {
    return value;
  }
  return 'development';
}

function exportScope(value: string): ExportScope {
  return value === 'repository' ? 'repository' : 'project_dir';
}

function normalizeProject(row: Record<string, unknown>): ProjectSummary {
  const owner = asRecord(row['owner']);
  const counts = asRecord(row['counts']);
  return {
    id: readNumber(row, 'id'),
    name: readString(row, 'name') || 'Untitled project',
    status: projectStatus(readString(row, 'status')),
    customer: readString(row, 'customer'),
    ownerName: readString(row, 'ownerName', 'owner') || (owner ? readString(owner, 'displayName', 'username', 'name') : ''),
    revisionCount: counts
      ? readCount(counts, 'revisions', 'revisionCount')
      : readCount(row, 'revisionCount', 'revisions'),
    headComponentCount: counts
      ? readCount(counts, 'components', 'componentCount', 'headComponentCount')
      : readCount(row, 'headComponentCount', 'componentCount', 'headComponents'),
    warningCount: counts
      ? readCount(counts, 'warnings', 'warningCount')
      : readCount(row, 'warningCount', 'warnings')
  };
}

function normalizeProjectDetail(body: unknown): ProjectDetail {
  const row = asRecord(body) ?? {};
  const summary = normalizeProject(row);
  const permissions = asRecord(row['permissions']);
  const links = asRecord(row['links']);
  const repository = asRecord(row['repository']);
  const warnings = row['warnings'];
  return {
    ...summary,
    description: readString(row, 'description'),
    repositoryId: readNullableNumber(row, 'repositoryId'),
    ownerUserId: readNullableNumber(row, 'ownerUserId'),
    projectFile: readString(row, 'projectFile', 'project_file'),
    exportScope: exportScope(readString(row, 'exportScope', 'export_scope')),
    headSha: readString(row, 'headSha', 'headCommitSha', 'head'),
    webUrl: readString(row, 'webUrl') || (links ? readString(links, 'web', 'project') : '') || (repository ? readString(repository, 'webUrl') : ''),
    defaultBranch: readString(row, 'defaultBranch') || (repository ? readString(repository, 'defaultBranch') : ''),
    permissions: {
      canWrite: permissions ? readBool(permissions, 'canWrite') : false,
      canAdmin: permissions ? readBool(permissions, 'canAdmin') : false
    },
    warnings: Array.isArray(warnings) ? warnings.map(item => String(item)) : []
  };
}

function normalizeAccess(row: Record<string, unknown>): ProjectAccessEntry {
  const group = asRecord(row['group']);
  return {
    groupId: readNumber(row, 'groupId', 'id'),
    groupName: readString(row, 'groupName', 'name') || (group ? readString(group, 'name') : ''),
    canWrite: readBool(row, 'canWrite')
  };
}

function normalizeRevision(row: Record<string, unknown>): ProjectRevision {
  const counts = asRecord(row['counts']);
  return {
    id: readNumber(row, 'id'),
    commitSha: readString(row, 'commitSha', 'sha'),
    message: readString(row, 'message'),
    authorName: readString(row, 'authorName', 'author'),
    committedAt: readString(row, 'committedAt', 'date', 'authoredAt'),
    refs: readStringList(row['refs']),
    reachable: row['reachable'] === undefined ? true : readBool(row, 'reachable'),
    analysisStatus: readString(row, 'analysisStatus', 'status') || 'pending',
    analysisError: readString(row, 'analysisError', 'error'),
    componentCount: counts
      ? readCount(counts, 'components', 'componentCount')
      : readCount(row, 'componentCount', 'components'),
    warningCount: counts
      ? readCount(counts, 'warnings', 'warningCount')
      : readCount(row, 'warningCount', 'warnings')
  };
}

function normalizeRevisionPage(response: HttpResponse<unknown>, query: RevisionQuery): RevisionPage {
  const body = response.body;
  const record = asRecord(body);
  let items: ProjectRevision[];
  let declared: number | null = null;
  if (Array.isArray(body)) {
    items = body.map(asRecord).filter((row): row is Record<string, unknown> => !!row).map(normalizeRevision);
  } else if (record) {
    items = readArray(body, 'items', 'revisions', 'value').map(normalizeRevision);
    const totalValue = record['total'] ?? record['totalCount'] ?? record['count'];
    if (typeof totalValue === 'number') {
      declared = totalValue;
    }
  } else {
    items = [];
  }
  const header = Number(response.headers.get('X-Total-Count') || response.headers.get('x-total-count'));
  if (Number.isFinite(header) && header > 0) {
    declared = header;
  }
  const total = declared ?? (items.length < query.limit
    ? query.offset + items.length
    : query.offset + items.length + 1);
  return { items, total };
}

function normalizeRevisionDetail(body: unknown): RevisionDetail {
  const row = asRecord(body) ?? {};
  const titleBlock = asRecord(row['titleBlock']);
  const pages = readArray(row['pages'] ? { pages: row['pages'] } : row, 'pages').map(normalizePageInfo);
  const revision = normalizeRevision(row);
  const warnings = row['warnings'];
  return {
    ...revision,
    title: titleBlock ? readString(titleBlock, 'title') : readString(row, 'title'),
    revisionLabel: titleBlock ? readString(titleBlock, 'rev') : '',
    pages,
    warnings: Array.isArray(warnings) ? warnings.map(item => String(item)) : []
  };
}

function normalizePageInfo(row: Record<string, unknown>): RevisionPageInfo {
  return {
    page: readString(row, 'page', 'id') || '1',
    sheetNames: readString(row, 'sheetNames', 'sheetPath'),
    file: readString(row, 'file'),
    paper: readString(row, 'paper')
  };
}

function bomStatuses(value: unknown): BomStatus | BomStatus[] {
  const allowed = ['ok', 'eol', 'not_in_catalog', 'ambiguous', 'field_outdated', 'dnp'];
  if (Array.isArray(value)) {
    const statuses = value.map(item => String(item)).filter((item): item is BomStatus => allowed.includes(item));
    return statuses.length ? statuses : ['not_in_catalog'];
  }
  const status = String(value || '');
  return (allowed.includes(status) ? status : 'not_in_catalog') as BomStatus;
}

function normalizeBomLine(row: Record<string, unknown>): BomLine {
  const part = asRecord(row['part']);
  const outdated = row['outdatedFields'] ?? row['fields'];
  return {
    reference: readString(row, 'reference', 'references') || readStringList(row['references']).join(', '),
    value: readString(row, 'value'),
    footprint: readString(row, 'footprint'),
    mpn: readString(row, 'mpn'),
    manufacturer: readString(row, 'manufacturer'),
    quantity: readNumber(row, 'quantity', 'qty') || 1,
    status: bomStatuses(row['status'] ?? row['catalogStatus']),
    dnp: readBool(row, 'dnp'),
    partId: part ? readNullableNumber(part, 'id') : readNullableNumber(row, 'partId'),
    partName: readString(row, 'partName') || (part ? readString(part, 'name') : ''),
    outdatedFields: Array.isArray(outdated) ? outdated.map(item => String(item)) : []
  };
}

function normalizeDiffItem(row: Record<string, unknown>): BomDiffItem {
  const from = asRecord(row['from']);
  const to = asRecord(row['to']);
  const changes = asRecord(row['changes']);
  const fieldChanges: BomFieldChange = {};
  if (changes) {
    for (const key of Object.keys(changes)) {
      const value = changes[key];
      fieldChanges[key] = Array.isArray(value) ? value.map(item => String(item)) : [String(value ?? '')];
    }
  }
  return {
    reference: readString(row, 'reference'),
    value: readString(row, 'value'),
    status: readString(row, 'status', 'catalogStatus'),
    fromValue: from ? readString(from, 'value') : '',
    toValue: to ? readString(to, 'value') : '',
    toStatus: to ? readString(to, 'status') : '',
    changes: fieldChanges,
    partId: readNullableNumber(row, 'partId')
  };
}

function normalizeBomDiff(body: unknown): BomDiff {
  const row = asRecord(body) ?? {};
  const summary = asRecord(row['summary']) ?? {};
  const group = (key: string) => readArray({ items: row[key] }, 'items').map(normalizeDiffItem);
  return {
    base: readString(row, 'base'),
    target: readString(row, 'target'),
    summary: {
      added: readNumber(summary, 'added'),
      removed: readNumber(summary, 'removed'),
      replaced: readNumber(summary, 'replaced'),
      changed: readNumber(summary, 'changed'),
      renamed: readNumber(summary, 'renamed')
    },
    added: group('added'),
    removed: group('removed'),
    replaced: group('replaced'),
    changed: group('changed'),
    renamed: group('renamed')
  };
}

function normalizeUsage(row: Record<string, unknown>): PartUsage {
  const project = asRecord(row['project']);
  const release = row['release'];
  const releaseRow = asRecord(release);
  return {
    projectId: readNumber(row, 'projectId') || (project ? readNumber(project, 'id') : 0),
    projectName: readString(row, 'projectName', 'name') || (project ? readString(project, 'name') : ''),
    revision: readString(row, 'revision', 'revisionSha', 'commitSha', 'sha'),
    release: typeof release === 'string'
      ? release
      : (releaseRow ? readString(releaseRow, 'name', 'tag') : readString(row, 'releaseName', 'tag')),
    quantity: readNumber(row, 'quantity')
  };
}

function normalizeRelease(body: unknown): ProjectRelease {
  const row = asRecord(body) ?? {};
  const revision = asRecord(row['revision']);
  const artifacts = Array.isArray(row['artifacts'])
    ? row['artifacts'].map(normalizeArtifact)
    : [];
  return {
    id: readNumber(row, 'id'),
    name: readString(row, 'name'),
    tag: readString(row, 'tag') || readNullableString(row, 'tag') || '',
    sha: readString(row, 'sha', 'commitSha') || (revision ? readString(revision, 'commitSha', 'sha') : ''),
    notes: readString(row, 'notes'),
    state: readString(row, 'state') || 'released',
    releasedAt: readString(row, 'releasedAt'),
    artifacts,
    erc: readCheckSummary(row['erc']),
    drc: readCheckSummary(row['drc']),
    outputsStatus: readString(row, 'outputsStatus'),
    outputsError: readString(row, 'outputsError')
  };
}

function normalizeArtifact(body: unknown): ReleaseArtifact {
  const row = asRecord(body) ?? {};
  return {
    id: readNumber(row, 'id'),
    kind: readString(row, 'kind'),
    fileName: readString(row, 'fileName'),
    variant: readString(row, 'variant')
  };
}

function readCheckSummary(value: unknown): CheckSummary | null {
  const row = asRecord(value);
  if (!row) {
    return null;
  }
  return {
    errors: readNumber(row, 'errors'),
    warnings: readNumber(row, 'warnings')
  };
}

export function manifestFromEnvelope(body: VisualDiffEnvelope | null | undefined): VisualDiffManifest | null {
  if (!body) {
    return null;
  }
  if (body.manifest?.pages) {
    return normalizeManifest(body.manifest);
  }
  if (Array.isArray(body.pages)) {
    return normalizeManifest(body);
  }
  return null;
}

export function diffIdFromEnvelope(body: VisualDiffEnvelope | null | undefined): string {
  if (!body) {
    return '';
  }
  if (body.id != null && String(body.id)) {
    return String(body.id);
  }
  return body.uuid ? String(body.uuid) : '';
}

function normalizeManifest(body: VisualDiffEnvelope | VisualDiffManifest): VisualDiffManifest {
  const row = body as unknown as Record<string, unknown>;
  const pages = Array.isArray(row['pages'])
    ? (row['pages'] as unknown[]).map(normalizeVisualPage)
    : [];
  const summary = asRecord(row['summary']) ?? {};
  const summaryOut: VisualDiffManifest['summary'] = {};
  for (const key of Object.keys(summary)) {
    const counts = asRecord(summary[key]);
    if (!counts) {
      continue;
    }
    const bucket: { [status: string]: number } = {};
    for (const status of Object.keys(counts)) {
      const value = counts[status];
      bucket[status] = typeof value === 'number' ? value : Number(value) || 0;
    }
    summaryOut[key] = bucket;
  }
  return {
    version: readNumber(row, 'version'),
    kicad: readString(row, 'kicad'),
    base: readString(row, 'base'),
    target: readString(row, 'target'),
    summary: summaryOut,
    pages
  };
}

function normalizeVisualPage(value: unknown): VisualDiffPage {
  const row = asRecord(value) ?? {};
  const viewBox = Array.isArray(row['viewBox']) ? row['viewBox'].map(item => Number(item)) : [0, 0, 1, 1];
  const crop = Array.isArray(row['crop']) ? row['crop'].map(item => Number(item)) : null;
  const layers = Array.isArray(row['layers'])
    ? row['layers'].map(normalizeLayer)
    : [];
  const changes = Array.isArray(row['changes'])
    ? row['changes'].map(normalizeVisualChange)
    : [];
  return {
    id: readString(row, 'id') || readString(row, 'page') || 'page',
    kind: readString(row, 'kind') || 'schematic',
    page: readString(row, 'page'),
    side: readString(row, 'side'),
    sheetNames: readString(row, 'sheetNames', 'sheetPath'),
    file: readString(row, 'file'),
    viewBox,
    crop,
    base: row['base'] == null ? null : String(row['base']),
    target: row['target'] == null ? null : String(row['target']),
    only: readString(row, 'only'),
    aligned: row['aligned'] === undefined ? true : readBool(row, 'aligned'),
    layers,
    changes
  };
}

function normalizeLayer(value: unknown): VisualDiffLayer {
  const row = asRecord(value) ?? {};
  return {
    status: readString(row, 'status'),
    category: readString(row, 'category'),
    file: readString(row, 'file'),
    aligned: row['aligned'] === undefined ? true : readBool(row, 'aligned')
  };
}

function normalizeVisualChange(value: unknown): VisualDiffChange {
  const row = asRecord(value) ?? {};
  const at = Array.isArray(row['at'])
    ? row['at'].map(point => Array.isArray(point) ? point.map(item => Number(item)) : [])
    : [];
  const detailRow = asRecord(row['detail']);
  const detail: { [field: string]: string[] } = {};
  if (detailRow) {
    for (const key of Object.keys(detailRow)) {
      const value = detailRow[key];
      detail[key] = Array.isArray(value) ? value.map(item => String(item)) : [String(value ?? '')];
    }
  }
  return {
    status: readString(row, 'status'),
    kind: readString(row, 'kind'),
    key: readString(row, 'key'),
    reference: readString(row, 'reference'),
    value: readString(row, 'value'),
    at,
    catalogStatus: readString(row, 'catalogStatus', 'partStatus'),
    detail
  };
}
