import React, { createContext, useContext, useEffect, useState } from 'react';

const AuthContext = createContext(null);

// The last signed-in user, so a till keeps working when the server cannot be reached (the server checks the login again later).
const LAST_USER = 'pos-last-user';
function lastUser() {
  try {
    return JSON.parse(localStorage.getItem(LAST_USER)) ?? null;
  } catch {
    return null;
  }
}
function rememberUser(user) {
  try {
    if (user) localStorage.setItem(LAST_USER, JSON.stringify(user));
    else localStorage.removeItem(LAST_USER);
  } catch {
    // storage blocked: offline sign-in is simply not kept
  }
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  const refresh = async () => {
    try {
      const res = await fetch('/api/auth/me');
      if ([502, 503, 504].includes(res.status)) {
        setUser(lastUser());
        return;
      }
      const me = res.ok ? await res.json() : null;
      setUser(me);
      rememberUser(me);
    } catch {
      setUser(lastUser());
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { refresh(); }, []);

  const login = async (email, password) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
    if (!res.ok) return false;
    await refresh();
    return true;
  };

  const logout = async () => {
    await fetch('/api/auth/logout', { method: 'POST' });
    setUser(null);
    rememberUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export const useAuth = () => useContext(AuthContext);
