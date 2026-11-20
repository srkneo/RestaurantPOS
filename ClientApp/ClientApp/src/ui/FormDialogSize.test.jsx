// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import FormDialog from './FormDialog';

afterEach(cleanup);

describe('Form dialog sizes', () => {
  it('is normal by default with Cancel, and wide with one button when asked', () => {
    const { unmount } = render(<I18nProvider><FormDialog open title="Edit" submitLabel="Save" onCancel={() => {}} onSubmit={() => {}}>x</FormDialog></I18nProvider>);
    expect(screen.getByRole('dialog')).not.toHaveClass('pos-dialog-wide');
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    unmount();
    render(<I18nProvider><FormDialog open size="wide" hideCancel title="History" submitLabel="Close" onCancel={() => {}} onSubmit={() => {}}>x</FormDialog></I18nProvider>);
    expect(screen.getByRole('dialog')).toHaveClass('pos-dialog-wide');
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
  });
});
