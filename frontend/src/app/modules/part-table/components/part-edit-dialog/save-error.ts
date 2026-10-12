export interface SaveFailure {
  summary: string;
  details: string[];
}

const SUMMARY = 'Saving failed. Check the values and try again.';

const FIELD_LABELS: Record<string, string> = {
  name: 'Name',
  value: 'Value',
  comment: 'Comment',
  datasheet: 'Datasheet',
  datasheetid: 'Datasheet',
  datasheetids: 'Datasheet',
  links: 'Link',
  libraryid: 'Library',
  symbolid: 'Symbol',
  footprintid: 'Footprint',
  keywords: 'Keywords',
  lifecyclestatus: 'Status',
  manufacturerpartnumber: 'Part number',
  packagetype: 'Package',
  quantity: 'Quantity',
  ordernumber: 'Order number',
  ordernumbervalue: 'Order number',
  quantitymin: 'Minimum quantity',
  quantitysteps: 'Quantity steps',
  manufacturerid: 'Manufacturer',
  distributorid: 'Distributor',
  partnumberid: 'Part number',
  partid: 'Part',
  uuid: 'Identifier',
  imagehash: 'Product image'
};

/** Keeps the save failure summary and adds each invalid field with the server's reason. */
export function describeSaveFailure(error: unknown): SaveFailure {
  return {
    summary: SUMMARY,
    details: extractFieldErrors(error)
  };
}

function extractFieldErrors(error: unknown): string[] {
  const body = readBody(error);
  if (typeof body === 'string') {
    return body ? [body] : [];
  }

  if (!body || typeof body !== 'object') {
    return [];
  }

  const record = body as { errors?: unknown; detail?: unknown };
  const lines = linesFromErrors(record.errors);
  if (lines.length) {
    return lines;
  }

  if (typeof record.detail === 'string') {
    const detail = record.detail.trim();
    if (detail && !detail.startsWith('<')) {
      return [detail];
    }
  }

  return [];
}

function readBody(error: unknown): unknown {
  if (typeof error === 'string') {
    return parseBody(error);
  }

  if (!error || typeof error !== 'object' || !('error' in error)) {
    return null;
  }

  const body = (error as { error?: unknown }).error;
  if (typeof body === 'string') {
    return parseBody(body);
  }

  return body ?? null;
}

function parseBody(text: string): unknown {
  const trimmed = text.trim();
  if (!trimmed || trimmed.startsWith('<') || trimmed.length > 500) {
    return null;
  }

  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      return JSON.parse(trimmed) as unknown;
    } catch {
      return null;
    }
  }

  return trimmed;
}

function linesFromErrors(errors: unknown): string[] {
  if (!errors || typeof errors !== 'object' || Array.isArray(errors)) {
    return [];
  }

  const lines: string[] = [];
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    const messages = asMessages(value);
    if (!messages.length) {
      continue;
    }

    const reason = messages.join(' ');
    const label = fieldLabel(key);
    lines.push(label ? `${label}: ${reason}` : reason);
  }

  return lines;
}

function asMessages(value: unknown): string[] {
  if (typeof value === 'string') {
    const text = value.trim();
    return text ? [text] : [];
  }

  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .filter((item): item is string => typeof item === 'string')
    .map(item => item.trim())
    .filter(Boolean);
}

function fieldLabel(key: string): string {
  const leaf = key.replace(/^\$\.?/, '').split('.').pop()?.replace(/\[\d+\]/g, '') ?? '';
  const normalized = leaf.replace(/_/g, '').toLowerCase();
  if (!normalized) {
    return '';
  }

  return FIELD_LABELS[normalized] ?? humanize(leaf);
}

function humanize(key: string): string {
  const spaced = key
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .trim();
  if (!spaced) {
    return '';
  }

  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
