// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { I18nProvider } from '../i18n/I18nContext';
import { ToastProvider, useToast } from './ToastContext';
import ConfirmDialog from './ConfirmDialog';
import StateMessage from './StateMessage';

function ToastButton() {
  const { showToast } = useToast();
  return <button type="button" onClick={() => showToast('Order 12 is ready')}>Show</button>;
}

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe('toasts', () => {
  it('shows a message and closes it', () => {
    render(<I18nProvider><ToastProvider><ToastButton /></ToastProvider></I18nProvider>);
    fireEvent.click(screen.getByText('Show'));
    expect(screen.getByText('Order 12 is ready')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByText('Order 12 is ready')).not.toBeInTheDocument();
  });

  it('hides the message after the set time', () => {
    vi.useFakeTimers();
    render(<I18nProvider><ToastProvider duration={1000}><ToastButton /></ToastProvider></I18nProvider>);
    fireEvent.click(screen.getByText('Show'));
    act(() => { vi.advanceTimersByTime(1100); });
    expect(screen.queryByText('Order 12 is ready')).not.toBeInTheDocument();
  });
});

describe('confirm dialog', () => {
  it('calls cancel and confirm', () => {
    const onCancel = vi.fn();
    const onConfirm = vi.fn();
    render(<I18nProvider><ConfirmDialog open title="Cancel order 12?" onCancel={onCancel} onConfirm={onConfirm} confirmLabel="Yes, cancel" danger /></I18nProvider>);

    expect(screen.getByRole('dialog')).toHaveTextContent('Cancel order 12?');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getByRole('button', { name: 'Yes, cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('renders nothing when closed', () => {
    render(<I18nProvider><ConfirmDialog open={false} title="Hidden" /></I18nProvider>);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('state message', () => {
  it('shows an error with a retry button', () => {
    const onRetry = vi.fn();
    render(<I18nProvider><StateMessage kind="error" onRetry={onRetry} /></I18nProvider>);
    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
