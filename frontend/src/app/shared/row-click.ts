/** True when the click or key came from a control inside the row. */
export function isRowControlEvent(event: Event): boolean {
  const target = event.target;
  const element = target instanceof Element
    ? target
    : target instanceof Node
      ? target.parentElement
      : null;
  return !!element?.closest('button, a, input, select, textarea, label');
}
