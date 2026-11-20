// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import Icon from './Icon';

afterEach(cleanup);

describe('Icon', () => {
  it('draws a known icon hidden from screen readers, and nothing for an unknown name', () => {
    const { container } = render(<button type="button" aria-label="Close"><Icon name="xmark" /><Icon name="no-such-icon" /></button>);
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
    const svgs = container.querySelectorAll('svg');
    expect(svgs).toHaveLength(1);
    expect(svgs[0]).toHaveAttribute('aria-hidden', 'true');
  });
});
