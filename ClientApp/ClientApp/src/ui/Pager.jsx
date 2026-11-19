import React, { useEffect, useMemo, useState } from 'react';
import { useT } from '../i18n/I18nContext';

// One page of a long list. Goes back to page 1 when the list changes (a new search, filter or date).
export function usePaged(items, pageSize = 50) {
  const [page, setPage] = useState(1);
  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  useEffect(() => {
    setPage(1);
  }, [items.length, pageSize]);
  const current = Math.min(page, pageCount);
  const pageItems = useMemo(() => items.slice((current - 1) * pageSize, current * pageSize), [items, current, pageSize]);
  return { page: current, pageCount, pageItems, setPage, total: items.length };
}

// Previous / Next under a list; hidden when everything fits on one page.
export default function Pager({ page, pageCount, total, onPage }) {
  const t = useT();
  if (pageCount <= 1) return null;
  return (
    <nav className="d-flex align-items-center justify-content-center flex-wrap gap-2 mt-2" aria-label={t('pager.label')}>
      <button type="button" className="btn btn-outline-secondary btn-sm px-3" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        {t('pager.prev')}
      </button>
      <span className="pos-hint">{t('pager.status', { page, pages: pageCount, total })}</span>
      <button type="button" className="btn btn-outline-secondary btn-sm px-3" disabled={page >= pageCount} onClick={() => onPage(page + 1)}>
        {t('pager.next')}
      </button>
    </nav>
  );
}
