import React, { useEffect, useMemo, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import Icon from './Icon';

export const PAGE_SIZES = [10, 20, 50, 100];

// Rows per page chosen on this device for this list (falls back when storage is blocked).
function readSize(key, fallback) {
  if (!key) return fallback;
  try {
    const saved = Number(window.localStorage.getItem(`pager:${key}`));
    return PAGE_SIZES.includes(saved) ? saved : fallback;
  } catch {
    return fallback;
  }
}

// One page of a long list. Goes back to page 1 when the list changes (a new search, filter or date) or the page size.
// key: remember the rows-per-page choice for this list on this device.
export function usePaged(items, defaultSize = 50, key = null) {
  const [pageSize, setSize] = useState(() => readSize(key, defaultSize));
  const [page, setPage] = useState(1);
  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  useEffect(() => {
    setPage(1);
  }, [items.length, pageSize]);
  const current = Math.min(page, pageCount);
  const pageItems = useMemo(() => items.slice((current - 1) * pageSize, current * pageSize), [items, current, pageSize]);
  const setPageSize = (size) => {
    setSize(size);
    if (key) {
      try { window.localStorage.setItem(`pager:${key}`, String(size)); } catch { /* storage blocked: keep it for this visit only */ }
    }
  };
  return { page: current, pageCount, pageItems, setPage, total: items.length, pageSize, setPageSize };
}

// Count, page choice, Previous / Next and (with onPageSize) rows per page. Shown whenever the list has rows;
// the page buttons only when there is more than one page.
export default function Pager({ page, pageCount, total, onPage, pageSize, onPageSize }) {
  const t = useT();
  if (!total) return null;
  const from = pageSize ? (page - 1) * pageSize + 1 : null;
  const to = pageSize ? Math.min(page * pageSize, total) : null;
  return (
    <nav className="d-flex align-items-center justify-content-between flex-wrap gap-2 my-2" aria-label={t('pager.label')}>
      <span className="pos-hint">
        {from != null ? t('pager.showing', { from, to, total }) : t('pager.status', { page, pages: pageCount, total })}
      </span>
      <div className="d-flex align-items-center flex-wrap gap-2">
        {onPageSize && (
          <div className="d-flex align-items-center gap-1 pos-hint">
            <span aria-hidden="true">{t('pager.perPage')}</span>
            <select className="form-select form-select-sm w-auto" aria-label={t('pager.perPage')} value={pageSize}
              onChange={(e) => onPageSize(Number(e.target.value))}>
              {PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
        )}
        {pageCount > 1 && (
          <>
            <button type="button" className="btn btn-outline-secondary btn-sm px-3" disabled={page <= 1} onClick={() => onPage(page - 1)}>
              <Icon name="chevron-left" className="pos-nav-chevron" /> {t('pager.prev')}
            </button>
            <div className="d-flex align-items-center gap-1 pos-hint">
              <span aria-hidden="true">{t('pager.page')}</span>
              <select className="form-select form-select-sm w-auto" aria-label={t('pager.pageSelect')} value={page}
                onChange={(e) => onPage(Number(e.target.value))}>
                {Array.from({ length: pageCount }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
              </select>
              <span aria-hidden="true">{t('pager.of', { pages: pageCount })}</span>
            </div>
            <button type="button" className="btn btn-outline-secondary btn-sm px-3" disabled={page >= pageCount} onClick={() => onPage(page + 1)}>
              {t('pager.next')} <Icon name="chevron-right" className="pos-nav-chevron" />
            </button>
          </>
        )}
      </div>
    </nav>
  );
}
