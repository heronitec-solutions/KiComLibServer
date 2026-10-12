import { BomStatus, ProjectStatus } from './types/project';

export function projectStatusLabel(status: string): string {
  switch (status) {
    case 'development':
      return 'In development';
    case 'released':
      return 'Released';
    case 'obsolete':
      return 'Obsolete';
    default:
      return status;
  }
}

export function projectStatusClass(status: string): string {
  switch (status) {
    case 'released':
      return 'badge-success';
    case 'obsolete':
      return 'badge-muted';
    default:
      return 'badge-muted';
  }
}

export function isProjectStatus(value: string): value is ProjectStatus {
  return value === 'development' || value === 'released' || value === 'obsolete';
}

export function bomStatusLabel(status: string): string {
  switch (status) {
    case 'ok':
      return 'OK';
    case 'eol':
      return 'End of life';
    case 'not_in_catalog':
      return 'Not in catalog';
    case 'ambiguous':
      return 'Ambiguous';
    case 'field_outdated':
      return 'Field outdated';
    case 'dnp':
      return 'DNP';
    default:
      return status;
  }
}

export function bomStatusClass(status: string): string {
  switch (status) {
    case 'ok':
      return 'badge-success';
    case 'eol':
      return 'badge-destructive';
    case 'not_in_catalog':
      return 'badge-destructive';
    case 'ambiguous':
      return 'badge-muted';
    case 'field_outdated':
      return 'badge-muted';
    case 'dnp':
      return 'badge-muted';
    default:
      return 'badge-muted';
  }
}

export function isBomStatus(value: string): value is BomStatus {
  return value === 'ok'
    || value === 'eol'
    || value === 'not_in_catalog'
    || value === 'ambiguous'
    || value === 'field_outdated'
    || value === 'dnp';
}

export function analysisStatusLabel(status: string): string {
  switch (status) {
    case 'pending':
      return 'Pending';
    case 'running':
      return 'Running';
    case 'done':
      return 'Done';
    case 'failed':
      return 'Failed';
    case 'unsupported':
      return 'Unsupported';
    default:
      return status;
  }
}

export function shortSha(sha: string): string {
  if (!sha) {
    return '—';
  }
  return sha.length > 12 ? sha.slice(0, 12) : sha;
}

export function revisionOptionLabel(revision: { commitSha: string; message: string; refs: string[] }): string {
  const tag = revision.refs.find(name => name.startsWith('refs/tags/') && !name.endsWith('^{}'));
  const branch = revision.refs.find(name => name.startsWith('refs/heads/'));
  const ref = tag || branch;
  const name = ref
    ? ref.replace(/^refs\/(?:tags|heads)\//, '')
    : shortSha(revision.commitSha);
  const message = (revision.message || '').trim();
  return message ? `${name} — ${message}` : name;
}

export function changeGroupLabel(status: string): string {
  switch (status) {
    case 'added':
      return 'Added';
    case 'removed':
      return 'Removed';
    case 'replaced':
      return 'Replaced';
    case 'changed':
      return 'Changed';
    case 'moved':
      return 'Moved';
    case 'renamed':
      return 'Renamed';
    default:
      return status;
  }
}
