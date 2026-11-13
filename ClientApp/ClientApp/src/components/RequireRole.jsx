import React from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';

export default function RequireRole({ roles, children }) {
  const { user, loading } = useAuth();
  if (loading) return <p style={{ padding: '30px' }}>Loading...</p>;
  if (!user) return <Navigate to="/login" replace />;
  if (roles && !roles.some((r) => user.roles.includes(r))) {
    return <p style={{ padding: '30px' }}>You do not have access to this page.</p>;
  }
  return children;
}
