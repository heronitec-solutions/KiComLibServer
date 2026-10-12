import { DistributorProduct } from './distributor-search.service';
import { lifecycleLabel } from '../types/part';

/** Pieces of a keyword lookup that can be written onto the part. */
export type ImportFieldId =
  | 'description'
  | 'status'
  | 'image'
  | 'datasheet'
  | 'partNumber'
  | 'package'
  | 'order';

export interface ImportFieldOption {
  id: ImportFieldId;
  label: string;
  /** Short preview shown when the field can be imported. */
  detail: string;
  /** Full text for the tooltip when the preview is shortened. */
  title: string;
  available: boolean;
  /** Why a disabled field cannot be imported. */
  reason: string;
}

export interface KeywordImportChoice {
  product: DistributorProduct;
  fields: ImportFieldId[];
}

/**
 * Fields each distributor search actually fills.
 * A value on the product still counts as available, so a rare extra
 * (for example a Mouser alternate packaging) stays selectable.
 * DigiKey and Arrow map a package; only DigiKey maps a photo.
 * TME has no lifecycle. Mouser does not map a photo or a datasheet.
 * Farnell, Arrow and TME have no product photo.
 */
const SUPPORTED: Record<ImportFieldId, ReadonlySet<string> | 'all'> = {
  description: 'all',
  status: new Set(['digikey', 'mouser', 'farnell', 'arrow']),
  image: new Set(['digikey']),
  datasheet: new Set(['digikey', 'farnell', 'arrow', 'tme']),
  partNumber: 'all',
  package: new Set(['digikey', 'arrow']),
  order: 'all'
};

const KNOWN_PROVIDERS = new Set(['mouser', 'digikey', 'farnell', 'arrow', 'tme']);

const PROVIDER_LABELS: Record<string, string> = {
  mouser: 'Mouser',
  digikey: 'DigiKey',
  farnell: 'Farnell',
  arrow: 'Arrow',
  tme: 'TME'
};

const FIELD_LABELS: Record<ImportFieldId, string> = {
  description: 'Description',
  status: 'Status',
  image: 'Product image',
  datasheet: 'Datasheet',
  partNumber: 'Manufacturer part number',
  package: 'Package',
  order: 'Order number'
};

const FIELD_ORDER: ImportFieldId[] = [
  'description',
  'status',
  'image',
  'datasheet',
  'partNumber',
  'package',
  'order'
];

export function isKeywordImportChoice(value: unknown): value is KeywordImportChoice {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }

  const choice = value as KeywordImportChoice;
  return choice.product != null && typeof choice.product === 'object' && Array.isArray(choice.fields);
}

/** Options for one lookup hit. Unavailable fields stay in the list so they can be shown disabled. */
export function importFieldOptions(product: DistributorProduct): ImportFieldOption[] {
  return FIELD_ORDER.map(id => optionFor(id, product));
}

/**
 * Package and an order number are stored on the manufacturer part-number row,
 * so turning either on also selects that row, and turning the row off clears both.
 */
export function toggleImportField(
  checked: ReadonlySet<ImportFieldId>,
  id: ImportFieldId,
  on: boolean
): Set<ImportFieldId> {
  const next = new Set(checked);
  if (on) {
    next.add(id);
    if (id === 'order' || id === 'package') {
      next.add('partNumber');
    }
  } else {
    next.delete(id);
    if (id === 'partNumber') {
      next.delete('order');
      next.delete('package');
    }
  }

  return next;
}

function optionFor(id: ImportFieldId, product: DistributorProduct): ImportFieldOption {
  const present = hasValue(id, product);
  const supported = providerSupports(id, product.provider);
  const available = present;
  const preview = present ? previewOf(id, product) : { detail: '', title: '' };
  return {
    id,
    label: FIELD_LABELS[id],
    detail: preview.detail,
    title: preview.title,
    available,
    reason: available ? '' : unavailableReason(product.provider, supported)
  };
}

function hasValue(id: ImportFieldId, product: DistributorProduct): boolean {
  const partNumber = text(product.manufacturerPartNumber);
  switch (id) {
    case 'description':
      return text(product.description).length > 0;
    case 'status':
      return product.lifecycleStatus === 'active'
        || product.lifecycleStatus === 'nrnd'
        || product.lifecycleStatus === 'ltb'
        || product.lifecycleStatus === 'eol';
    case 'image':
      return text(product.imageUrl).length > 0;
    case 'datasheet':
      return text(product.datasheetUrl).length > 0;
    case 'partNumber':
      return partNumber.length > 0;
    case 'package':
      return partNumber.length > 0 && text(product.package).length > 0;
    case 'order':
      return partNumber.length > 0 && text(product.orderNumber).length > 0;
    default:
      return false;
  }
}

function providerSupports(id: ImportFieldId, provider: string): boolean {
  const supported = SUPPORTED[id];
  if (supported === 'all') {
    return true;
  }

  const key = provider.trim().toLowerCase();
  if (!KNOWN_PROVIDERS.has(key)) {
    return true;
  }

  return supported.has(key);
}

function unavailableReason(provider: string, supported: boolean): string {
  if (supported) {
    return 'Not available for this part';
  }

  const name = PROVIDER_LABELS[provider.trim().toLowerCase()] ?? 'This API';
  return `${name} does not provide this`;
}

function previewOf(id: ImportFieldId, product: DistributorProduct): { detail: string; title: string } {
  switch (id) {
    case 'description':
      return clip(text(product.description));
    case 'status':
      return plain(lifecycleLabel(product.lifecycleStatus));
    case 'image':
      return plain('Product photo');
    case 'datasheet':
      return clip(fileLabel(text(product.datasheetUrl)));
    case 'partNumber':
      return clip([text(product.manufacturer), text(product.manufacturerPartNumber)].filter(part => part.length > 0).join(' · '));
    case 'package':
      return clip(text(product.package));
    case 'order':
      return clip(orderDetail(product));
    default:
      return plain('');
  }
}

function orderDetail(product: DistributorProduct): string {
  const identity = [text(product.distributorName), text(product.orderNumber)].filter(part => part.length > 0);
  const quantities: string[] = [];
  if (product.quantityMin != null) {
    quantities.push(`min ${product.quantityMin}`);
  }
  if (product.quantitySteps != null) {
    quantities.push(`step ${product.quantitySteps}`);
  }

  const head = identity.join(' · ');
  const tail = quantities.join(', ');
  if (head && tail) {
    return `${head} · ${tail}`;
  }

  return head || tail;
}

function fileLabel(url: string): string {
  try {
    const path = new URL(url).pathname;
    const name = decodeURIComponent(path.split('/').filter(part => part.length > 0).pop() ?? '');
    if (name && !name.includes('..')) {
      return name;
    }
  } catch {
    return 'Datasheet';
  }

  return 'Datasheet';
}

function clip(value: string, max = 90): { detail: string; title: string } {
  const textValue = value.trim();
  if (textValue.length <= max) {
    return plain(textValue);
  }

  return {
    detail: `${textValue.slice(0, max - 1).trimEnd()}…`,
    title: textValue
  };
}

function plain(value: string): { detail: string; title: string } {
  return { detail: value, title: '' };
}

function text(value: string | null | undefined): string {
  return (value ?? '').trim();
}
