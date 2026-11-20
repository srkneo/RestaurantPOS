// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter } from 'react-router-dom';
import NavGroup from './NavGroup';

afterEach(cleanup);

const ITEMS = [{ to: '/admin/staff', label: 'Staff' }, { to: '/admin/store', label: 'Store' }];

describe('Main menu group', () => {
  it('opens its pages, marks the group of the current page and closes with Escape', () => {
    render(<MemoryRouter initialEntries={['/admin/store']}><NavGroup label="Admin" items={ITEMS} /></MemoryRouter>);
    const toggle = screen.getByRole('button', { name: 'Admin' });
    expect(toggle).toHaveClass('active');
    expect(screen.queryByRole('link', { name: 'Staff' })).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('link', { name: 'Staff' })).toHaveAttribute('href', '/admin/staff');
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('link', { name: 'Staff' })).not.toBeInTheDocument();
  });

  it('closes after a page is chosen or on a click outside', () => {
    render(<MemoryRouter initialEntries={['/pos']}><div><NavGroup label="Admin" items={ITEMS} /><p>Outside</p></div></MemoryRouter>);
    const toggle = screen.getByRole('button', { name: 'Admin' });
    expect(toggle).not.toHaveClass('active');
    fireEvent.click(toggle);
    fireEvent.click(screen.getByRole('link', { name: 'Store' }));
    expect(screen.queryByRole('link', { name: 'Store' })).not.toBeInTheDocument();
    fireEvent.click(toggle);
    fireEvent.mouseDown(screen.getByText('Outside'));
    expect(screen.queryByRole('link', { name: 'Store' })).not.toBeInTheDocument();
  });

  it('is a plain link with one page and nothing with none', () => {
    const { container } = render(<MemoryRouter><NavGroup label="Admin" items={[ITEMS[0]]} /><NavGroup label="Empty" items={[]} /></MemoryRouter>);
    expect(screen.getByRole('link', { name: 'Staff' })).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(container.textContent).not.toContain('Empty');
  });
});
