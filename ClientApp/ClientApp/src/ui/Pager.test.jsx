// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import Pager, { usePaged } from './Pager';

function List({ count }) {
  const items = Array.from({ length: count }, (_, i) => `Item ${i + 1}`);
  const { page, pageCount, pageItems, setPage, total } = usePaged(items, 50);
  return (
    <>
      <ul>{pageItems.map((i) => <li key={i}>{i}</li>)}</ul>
      <Pager page={page} pageCount={pageCount} total={total} onPage={setPage} />
    </>
  );
}

afterEach(cleanup);

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

  it('is hidden when everything fits on one page', () => {
    render(<I18nProvider><List count={10} /></I18nProvider>);
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
  });
});
