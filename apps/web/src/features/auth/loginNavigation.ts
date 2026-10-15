interface ReturnLocation {
  pathname?: unknown;
  search?: unknown;
  hash?: unknown;
}

export function getPostLoginDestination(state: unknown): string {
  if (!state || typeof state !== 'object' || !('from' in state)) {
    return '/tickets';
  }

  const from = (state as { from?: ReturnLocation }).from;
  if (typeof from?.pathname !== 'string' || !from.pathname.startsWith('/') || from.pathname.startsWith('//')) {
    return '/tickets';
  }

  const search = typeof from.search === 'string' ? from.search : '';
  const hash = typeof from.hash === 'string' ? from.hash : '';
  return `${from.pathname}${search}${hash}`;
}
