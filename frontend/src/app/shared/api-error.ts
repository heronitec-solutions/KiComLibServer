export function apiErrorMessage(error: unknown, fallback: string): string {
  const body = (error as { error?: { error?: string } })?.error;
  return body?.error || fallback;
}
