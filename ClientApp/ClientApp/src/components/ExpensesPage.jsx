import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { formatMoney, parseServerDate } from '../ui/format';
import './OrderHistory.css';

export const EXPENSE_CATEGORIES = ['Vegetables', 'Groceries', 'Milk', 'Gas', 'Salary', 'Rent', 'Electricity', 'Repairs', 'Other'];
const blank = { category: 'Vegetables', amount: '', paidFrom: 'Till', note: '' };

// Money spent today (milk, vegetables, gas...). Paid from the till comes out of the shift's expected cash.
export default function ExpensesPage() {
  const t = useT();
  const { showToast } = useToast();
  const [data, setData] = useState(null);
  const [loadState, setLoadState] = useState('loading');
  const [form, setForm] = useState(blank);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/expenses');
      if (!res.ok) throw new Error('load');
      setData(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const set = (field) => (e) => setForm((f) => ({ ...f, [field]: e.target.value }));

  const save = async (e) => {
    e.preventDefault();
    const amount = Number(String(form.amount).replace(/,/g, ''));
    if (!form.amount || Number.isNaN(amount) || amount <= 0) {
      setError(t('expenses.badAmount'));
      return;
    }
    setBusy(true);
    setError('');
    try {
      const res = await fetch('/api/expenses', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ category: form.category, amount, paidFrom: form.paidFrom, note: form.note.trim() || null }),
      });
      const body = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(body.message || t('common.error'));
        return;
      }
      showToast(t('expenses.saved', { amount: formatMoney(body.amount) }));
      setForm((f) => ({ ...blank, category: f.category, paidFrom: f.paidFrom }));
      load();
    } catch {
      setError(t('pos.networkError'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head"><h1>{t('expenses.title')}</h1></div>
      <form className="pos-card p-3 d-flex flex-wrap align-items-end gap-2 mb-3" onSubmit={save}>
        <div>
          <label htmlFor="expense-category" className="form-label">{t('expenses.category')}</label>
          <select id="expense-category" className="form-select" value={form.category} onChange={set('category')}>
            {EXPENSE_CATEGORIES.map((c) => <option key={c} value={c}>{t(`expenses.cat.${c}`)}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="expense-amount" className="form-label">{t('expenses.amount')}</label>
          <input id="expense-amount" className="form-control" inputMode="decimal" autoComplete="off" value={form.amount} onChange={set('amount')} />
        </div>
        <div>
          <label htmlFor="expense-from" className="form-label">{t('expenses.paidFrom')}</label>
          <select id="expense-from" className="form-select" value={form.paidFrom} onChange={set('paidFrom')}>
            <option value="Till">{t('expenses.from.Till')}</option>
            <option value="Other">{t('expenses.from.Other')}</option>
          </select>
        </div>
        <div className="flex-grow-1">
          <label htmlFor="expense-note" className="form-label">{t('expenses.note')}</label>
          <input id="expense-note" className="form-control" maxLength={100} autoComplete="off" value={form.note} onChange={set('note')} />
        </div>
        <button type="submit" className="btn btn-primary px-4" disabled={busy}>{t('expenses.save')}</button>
        {error && <div className="w-100 text-danger" role="alert">{error}</div>}
      </form>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && data.expenses.length === 0 && <StateMessage kind="empty" message={t('expenses.empty')} />}
      {loadState === 'ready' && data.expenses.length > 0 && (
        <div className="pos-card table-responsive">
          <table className="table align-middle">
            <thead>
              <tr>
                <th scope="col">{t('expenses.time')}</th>
                <th scope="col">{t('expenses.category')}</th>
                <th scope="col">{t('expenses.paidFrom')}</th>
                <th scope="col">{t('expenses.note')}</th>
                <th scope="col" className="text-end">{t('expenses.amount')}</th>
              </tr>
            </thead>
            <tbody>
              {data.expenses.map((e) => (
                <tr key={e.id}>
                  <td className="text-nowrap">{parseServerDate(e.at)?.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) ?? ''}</td>
                  <td>{t(`expenses.cat.${e.category}`)}</td>
                  <td>{t(`expenses.from.${e.paidFrom}`)}</td>
                  <td>{e.note ?? ''}</td>
                  <td className="text-end fw-semibold">{formatMoney(e.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="px-3 mb-3 fw-semibold">{t('expenses.total', { total: formatMoney(data.total), till: formatMoney(data.fromTill) })}</p>
        </div>
      )}
    </main>
  );
}
