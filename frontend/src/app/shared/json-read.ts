/** Tolerant readers for API JSON while the projects backend is still settling. */

export function asRecord(value: unknown): Record<string, unknown> | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }
  return value as Record<string, unknown>;
}

export function readString(row: Record<string, unknown>, ...keys: string[]): string {
  for (const key of keys) {
    const value = row[key];
    if (typeof value === 'string') {
      return value;
    }
    if (typeof value === 'number' && Number.isFinite(value)) {
      return String(value);
    }
  }
  return '';
}

export function readNullableString(row: Record<string, unknown>, ...keys: string[]): string | null {
  const value = readString(row, ...keys);
  return value ? value : null;
}

export function readNumber(row: Record<string, unknown>, ...keys: string[]): number {
  for (const key of keys) {
    const value = row[key];
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
    if (typeof value === 'string' && value.trim() && Number.isFinite(Number(value))) {
      return Number(value);
    }
  }
  return 0;
}

export function readNullableNumber(row: Record<string, unknown>, ...keys: string[]): number | null {
  for (const key of keys) {
    if (row[key] == null || row[key] === '') {
      continue;
    }
    return readNumber(row, key);
  }
  return null;
}

export function readBool(row: Record<string, unknown>, ...keys: string[]): boolean {
  for (const key of keys) {
    const value = row[key];
    if (typeof value === 'boolean') {
      return value;
    }
  }
  return false;
}

export function readStringList(value: unknown): string[] {
  if (typeof value === 'string' && value.trim()) {
    return [value];
  }
  if (!Array.isArray(value)) {
    return [];
  }
  return value.map(item => {
    if (typeof item === 'string') {
      return item;
    }
    const row = asRecord(item);
    return row ? readString(row, 'name', 'ref', 'id', 'sha') : '';
  }).filter(item => item.length > 0);
}

export function readCount(row: Record<string, unknown>, ...keys: string[]): number {
  for (const key of keys) {
    const value = row[key];
    if (Array.isArray(value)) {
      return value.length;
    }
  }
  return readNumber(row, ...keys);
}
