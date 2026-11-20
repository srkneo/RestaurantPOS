// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import RowMenu from './RowMenu';

afterEach(cleanup);

describe('Row menu closing', () => {
  it('closes on a click outside but not on a click inside, and toggles with its button', () => {
    const { container } = render(
      <div>
        <p>Outside</p>
        <RowMenu label="More"><button type="button">Split bill</button></RowMenu>
      </div>
    );
    const toggle = screen.getByRole('button', { name: 'More' });
    const list = container.querySelector('.pos-more-menu');
    fireEvent.click(toggle);
    expect(list).toHaveClass('open');
    fireEvent.mouseDown(list);
    expect(list).toHaveClass('open');
    fireEvent.mouseDown(screen.getByText('Outside'));
    expect(list).not.toHaveClass('open');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
  });
});
