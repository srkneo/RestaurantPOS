import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { useT } from '../i18n/I18nContext';

const ToastContext = createContext({ showToast: () => 0, dismiss: () => {} });
let nextId = 1;

// Use instead of alert(): const { showToast } = useToast(); showToast('Order placed', 'success' | 'error')
export function ToastProvider({ children, duration = 4000 }) {
  const [toasts, setToasts] = useState([]);
  const timers = useRef(new Map());
  const t = useT();

  const dismiss = useCallback((id) => {
    setToasts((list) => list.filter((x) => x.id !== id));
    const timer = timers.current.get(id);
    if (timer) {
      clearTimeout(timer);
      timers.current.delete(id);
    }
  }, []);

  const showToast = useCallback((message, variant = 'success') => {
    const id = nextId++;
    setToasts((list) => [...list.slice(-2), { id, message, variant }]);
    timers.current.set(id, setTimeout(() => dismiss(id), duration));
    return id;
  }, [dismiss, duration]);

  useEffect(() => {
    const map = timers.current;
    return () => map.forEach((timer) => clearTimeout(timer));
  }, []);

  const value = useMemo(() => ({ showToast, dismiss }), [showToast, dismiss]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="pos-toasts" role="status" aria-live="polite">
        {toasts.map((x) => (
          <div key={x.id} className={`pos-toast pos-toast-${x.variant}`}>
            <span>{x.message}</span>
            <button type="button" aria-label={t('common.close')} onClick={() => dismiss(x.id)}>×</button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export const useToast = () => useContext(ToastContext);
