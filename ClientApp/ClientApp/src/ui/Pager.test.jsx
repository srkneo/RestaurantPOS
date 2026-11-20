// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import Pager, { usePaged } from './Pager';

function List({ count, withSize = false }) {
  const items = Array.from({ length: count }, (_, i) => `Item ${i + 1}`);
  const { page, pageCount, pageItems, setPage, total, pageSize, setPageSize } = usePaged(items, withSize ? 20 : 50, withSize ? 'test-list' : null);
  return (
    <>
      <ul>{pageItems.map((i) => <li key={i}>{i}</li>)}</ul>
      <Pager page={page} pageCount={pageCount} total={total} onPage={setPage}
        {...(withSize ? { pageSize, onPageSize: setPageSize } : {})} />
    </>
  );
}

afterEach(() => {
  cleanup();
  try { window.localStorage.clear(); } catch { /* ignore */ }
});

describe('Pager', () => {
  it('shows one page at a time', () => {
    render(<I18nProvider><List count={120} /></I18nProvider>);
    expect(screen.getByText('Page 1 of 3 (120)')).toBeInTheDocument();
    expect(screen.getByText('Item 50')).toBeInTheDocument();
    expect(screen.queryByText('Item 51')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('Item 51')).toBeInTheDocument();
    expect(screen.getByText('Page 2 of 3 (120)')).toBeInTheDocument();
  });

  it('shows the count but no page buttons when everything fits on one page, and nothing for an empty list', () => {
    const { unmount } = render(<I18nProvider><List count={10} /></I18nProvider>);
    expect(screen.getByText('Page 1 of 1 (10)')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
    unmount();
    render(<I18nProvider><List count={0} /></I18nProvider>);
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument();
  });

  it('jumps to a page and changes rows per page, remembering the choice', () => {
    render(<I18nProvider><List count={45} withSize /></I18nProvider>);
    expect(screen.getByText('Showing 1–20 of 45')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Choose page'), { target: { value: '3' } });
    expect(screen.getByText('Item 41')).toBeInTheDocument();
    expect(screen.getByText('Showing 41–45 of 45')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Rows per page'), { target: { value: '50' } });
    expect(screen.getByText('Showing 1–45 of 45')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
    expect(window.localStorage.getItem('pager:test-list')).toBe('50');
  });
});
