import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';

const Login = () => {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    const ok = await login(email, password);
    if (ok) navigate('/', { replace: true });
    else setError('Invalid email or password');
  };

  return (
    <div style={styles.wrap}>
      <form onSubmit={submit} style={styles.card}>
        <h1 style={styles.title}>Sign in</h1>
        <input type="email" placeholder="Email" aria-label="Email" value={email} onChange={(e) => setEmail(e.target.value)} style={styles.input} required />
        <input type="password" placeholder="Password" aria-label="Password" value={password} onChange={(e) => setPassword(e.target.value)} style={styles.input} required />
        {error && <p style={styles.error}>{error}</p>}
        <button type="submit" style={styles.button}>Sign in</button>
      </form>
    </div>
  );
};

const styles = {
  wrap: { display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', backgroundColor: '#f8f9fa' },
  card: { display: 'flex', flexDirection: 'column', gap: '14px', width: '340px', padding: '30px', backgroundColor: '#ffffff', borderRadius: '12px', boxShadow: '0 4px 12px rgba(0,0,0,0.1)' },
  title: { margin: 0, color: '#2c3e50', textAlign: 'center' },
  input: { padding: '12px', fontSize: '1rem', border: '1px solid #e0e0e0', borderRadius: '6px' },
  button: { padding: '12px', fontSize: '1.1rem', fontWeight: 'bold', color: '#ffffff', backgroundColor: '#27ae60', border: 'none', borderRadius: '6px', cursor: 'pointer' },
  error: { margin: 0, color: '#e74c3c', textAlign: 'center' },
};

export default Login;
