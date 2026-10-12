export interface SvgPreview {
  /** Inner markup of the source SVG, safe to project into an inline <svg>. */
  content: string;
  viewBox: string;
}

export const EMPTY_SVG_PREVIEW: SvgPreview = { content: '', viewBox: '0 0 1 1' };

/**
 * Unwraps a standalone SVG document from the preview endpoints into markup that
 * can be dropped inside an existing `<svg>` element: the XML prolog, DOCTYPE and
 * the outer `<svg>` tag are stripped, and the original viewBox is preserved so
 * the drawing keeps its aspect ratio.
 */
export function parseSvgPreview(raw: string): SvgPreview {
  let svg = decodeURIComponent(raw)
    .replace(/<\?xml[^>]*>\s*/i, '')
    .replace(/<!DOCTYPE[^>]*>\s*/i, '');

  const viewBox = svg.match(/viewBox="([^"]+)"/)?.[1] ?? EMPTY_SVG_PREVIEW.viewBox;

  svg = svg
    .replace(/<svg[^>]*?>/i, '')
    .replace(/<\/svg>/i, '');

  return { content: sanitizeSvgFragment(svg), viewBox };
}

const DROPPED_SVG = new Set([
  'script', 'foreignobject', 'iframe', 'embed', 'object', 'image', 'set',
  'animate', 'animatetransform', 'animatemotion', 'handler', 'audio', 'video',
  'canvas', 'link', 'meta', 'style'
]);

/** Removes active content before the fragment is placed in the page. */
export function sanitizeSvgFragment(fragment: string): string {
  if (!fragment || typeof DOMParser === 'undefined') {
    return '';
  }

  const doc = new DOMParser().parseFromString(
    `<svg xmlns="http://www.w3.org/2000/svg">${fragment}</svg>`,
    'image/svg+xml'
  );
  const root = doc.documentElement;
  if (!root || root.nodeName.toLowerCase() === 'parsererror' || doc.querySelector('parsererror')) {
    return '';
  }

  pruneSvg(root);
  return root.innerHTML;
}

function pruneSvg(node: Element): void {
  for (const child of Array.from(node.children)) {
    if (DROPPED_SVG.has(child.localName.toLowerCase())) {
      child.remove();
      continue;
    }
    pruneSvg(child);
  }

  for (const attribute of Array.from(node.attributes)) {
    const name = attribute.localName.toLowerCase();
    if (name.startsWith('on')) {
      node.removeAttribute(attribute.name);
      continue;
    }
    if (name === 'href' || name === 'src' || name === 'xlink:href') {
      const value = attribute.value.trim();
      if (!value.startsWith('#') || value.includes(':') || value.includes('\\')) {
        node.removeAttribute(attribute.name);
      }
      continue;
    }
    if (name === 'style' && /url\s*\(|expression|javascript|@import|behavior/i.test(attribute.value)) {
      node.removeAttribute(attribute.name);
    }
  }
}
