// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import RowMenu from './RowMenu';

afterEach(cleanup);

describe('Row menu', () => {
  it('opens, runs a choice and closes; Escape closes too', () => {
    const move = vi.fn();
    const { container } = render(<RowMenu label="More"><button type="button" onClick={move}>Move table</button></RowMenu>);
    const toggle = screen.getByRole('button', { name: 'More' });
    const list = container.querySelector('.pos-more-menu');
    expect(list).not.toHaveClass('open');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(list).toHaveClass('open');
    fireEvent.click(screen.getByRole('button', { name: 'Move table' }));
    expect(move).toHaveBeenCalled();
    expect(list).not.toHaveClass('open');
    fireEvent.click(toggle);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(list).not.toHaveClass('open');
  });
});
