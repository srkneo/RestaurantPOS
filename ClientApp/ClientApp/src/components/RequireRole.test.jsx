// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from '../AuthContext';
import RequireRole from './RequireRole';

function mockSignedInUser(user) {
  vi.stubGlobal('fetch', vi.fn(async () => ({
    ok: user !== null,
    json: async () => user,
  })));
}

function renderPosPage(roles) {
  render(
    <AuthProvider>
      <MemoryRouter initialEntries={['/pos']}>
        <Routes>
          <Route path="/login" element={<p>Login page</p>} />
          <Route path="/pos" element={<RequireRole roles={roles}><p>POS screen</p></RequireRole>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>
  );
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('RequireRole', () => {
  it('sends a signed-out user to the login page', async () => {
    mockSignedInUser(null);
    renderPosPage(['cashier']);
    expect(await screen.findByText('Login page')).toBeInTheDocument();
  });

  it('blocks a user without the right role', async () => {
    mockSignedInUser({ email: 'kitchen@test.local', roles: ['kitchen'] });
    renderPosPage(['cashier', 'manager', 'admin']);
    expect(await screen.findByText('You do not have access to this page.')).toBeInTheDocument();
  });

  it('shows the page to a user with the right role', async () => {
    mockSignedInUser({ email: 'cashier@test.local', roles: ['cashier'] });
    renderPosPage(['cashier']);
    expect(await screen.findByText('POS screen')).toBeInTheDocument();
  });
});
