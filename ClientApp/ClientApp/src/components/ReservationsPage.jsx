import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import FormDialog from '../ui/FormDialog';
import { readErrors, send, Field } from '../ui/forms';
import './OrderHistory.css';

const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const blank = (date) => ({ customerName: '', phone: '', people: '2', date, time: '20:00', tableId: '', note: '' });
const BADGE = { Booked: 'text-bg-primary', Seated: 'text-bg-success', Cancelled: 'text-bg-secondary', NoShow: 'text-bg-warning' };

// Table bookings of one day. Waiter, cashier, manager and owner book, seat, cancel or mark a no-show (and undo it).
export default function ReservationsPage() {
  const t = useT();
  const { showToast } = useToast();
  const [day, setDay] = useState(() => iso(new Date()));
  const [list, setList] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [tables, setTables] = useState([]);
  const [editing, setEditing] = useState(null);
  const [form, setForm] = useState(() => blank(iso(new Date())));
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (d) => {
    setLoadState('loading');
    try {
      const res = await fetch(`/api/reservations?date=${encodeURIComponent(d)}`);
      if (!res.ok) throw new Error('load');
      setList(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => { load(day); }, [load, day]);

  useEffect(() => {
    let alive = true;
    fetch('/api/tables').then((res) => (res.ok ? res.json() : [])).then((data) => { if (alive) setTables(Array.isArray(data) ? data : []); })
      .catch(() => {});
    return () => { alive = false; };
  }, []);

  const set = (field) => (e) => setForm((f) => ({ ...f, [field]: e.target.value }));
  const generalError = errors.general !== undefined ? (errors.general || t('common.error')) : undefined;

  const open = (r) => {
    setErrors({});
    setEditing(r ?? {});
    setForm(r
      ? { customerName: r.customerName, phone: r.phone ?? '', people: String(r.people), date: r.date, time: r.time, tableId: r.tableId ? String(r.tableId) : '', note: r.note ?? '' }
      : blank(day));
  };

  const save = async () => {
    const body = {
      customerName: form.customerName.trim(), phone: form.phone.trim() || null, people: Number(form.people) || 0,
      date: form.date, time: form.time, tableId: form.tableId ? Number(form.tableId) : null, note: form.note.trim() || null,
    };
    setBusy(true);
    try {
      const res = await send(editing.id ? `/api/reservations/${editing.id}` : '/api/reservations', editing.id ? 'PUT' : 'POST', body);
      if (!res.ok) { setErrors(await readErrors(res)); return; }
      const saved = await res.json();
      setEditing(null);
      showToast(t('res.saved', { name: saved.customerName, time: saved.time }));
      if (saved.date !== day) setDay(saved.date); else load(day);
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const setStatus = async (r, status) => {
    const res = await send(`/api/reservations/${r.id}/status`, 'POST', { status }).catch(() => null);
    if (res?.ok) {
      showToast(t('res.changed', { name: r.customerName, status: t(`res.status.${status}`) }));
      load(day);
    } else {
      const errs = res ? await readErrors(res) : {};
      showToast(errs.status || errs.general || t('common.error'), 'error');
    }
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('res.title')}</h1>
        <button type="button" className="btn btn-primary px-3" onClick={() => open(null)}>{t('res.add')}</button>
      </div>
      <div className="d-flex flex-wrap align-items-end gap-2 mb-3">
        <div>
          <label htmlFor="res-day" className="form-label">{t('res.day')}</label>
          <input id="res-day" type="date" className="form-control" value={day} onChange={(e) => e.target.value && setDay(e.target.value)} />
        </div>
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={() => load(day)} />}
      {loadState === 'ready' && list.length === 0 && <StateMessage kind="empty" message={t('res.empty')} />}
      {loadState === 'ready' && list.length > 0 && (
        <div className="pos-card table-responsive">
          <table className="table align-middle">
            <thead>
              <tr>
                <th scope="col">{t('res.time')}</th>
                <th scope="col">{t('res.name')}</th>
                <th scope="col">{t('res.people')}</th>
                <th scope="col">{t('res.table')}</th>
                <th scope="col">{t('res.note')}</th>
                <th scope="col">{t('res.statusCol')}</th>
                <th scope="col">{t('res.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {list.map((r) => (
                <tr key={r.id}>
                  <td className="fw-semibold text-nowrap">{r.time}</td>
                  <td>
                    <div>{r.customerName}</div>
                    {r.phone && <a href={`tel:${r.phone.replace(/[^\d+]/g, '')}`} className="small">{r.phone}</a>}
                  </td>
                  <td>{r.people}</td>
                  <td>{r.tableNumber ? t('res.tableN', { number: r.tableNumber }) : t('res.anyTable')}</td>
                  <td>{r.note ?? ''}</td>
                  <td><span className={`badge ${BADGE[r.status] ?? 'text-bg-secondary'}`}>{t(`res.status.${r.status}`)}</span></td>
                  <td>
                    <div className="d-flex flex-wrap gap-2">
                      {r.status === 'Booked' ? (
                        <>
                          <button type="button" className="btn btn-sm btn-primary" aria-label={`${t('res.seat')} ${r.customerName}`} onClick={() => setStatus(r, 'Seated')}>{t('res.seat')}</button>
                          <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={`${t('res.editBtn')} ${r.customerName}`} onClick={() => open(r)}>{t('res.editBtn')}</button>
                          <button type="button" className="btn btn-sm btn-outline-warning" aria-label={`${t('res.noShow')} ${r.customerName}`} onClick={() => setStatus(r, 'NoShow')}>{t('res.noShow')}</button>
                          <button type="button" className="btn btn-sm btn-outline-danger" aria-label={`${t('res.cancel')} ${r.customerName}`} onClick={() => setStatus(r, 'Cancelled')}>{t('res.cancel')}</button>
                        </>
                      ) : (
                        <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={`${t('res.undo')} ${r.customerName}`} onClick={() => setStatus(r, 'Booked')}>{t('res.undo')}</button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <FormDialog open={editing !== null} title={editing?.id ? t('res.edit') : t('res.add')} submitLabel={t('res.save')}
        busy={busy} error={generalError} onCancel={() => setEditing(null)} onSubmit={save}>
        <Field id="res-name" label={t('res.name')} error={errors.customername}>
          <input id="res-name" className="form-control" maxLength={60} autoComplete="off" value={form.customerName} onChange={set('customerName')} />
        </Field>
        <Field id="res-phone" label={t('res.phone')} error={errors.phone}>
          <input id="res-phone" className="form-control" inputMode="tel" maxLength={20} autoComplete="off" value={form.phone} onChange={set('phone')} />
        </Field>
        <Field id="res-people" label={t('res.people')} error={errors.people}>
          <input id="res-people" type="number" min={1} max={100} className="form-control" value={form.people} onChange={set('people')} />
        </Field>
        <div className="d-flex gap-2">
          <div className="flex-grow-1">
            <Field id="res-date" label={t('res.date')} error={errors.date}>
              <input id="res-date" type="date" className="form-control" value={form.date} onChange={set('date')} />
            </Field>
          </div>
          <div className="flex-grow-1">
            <Field id="res-time" label={t('res.time')} error={errors.time}>
              <input id="res-time" type="time" className="form-control" value={form.time} onChange={set('time')} />
            </Field>
          </div>
        </div>
        <Field id="res-table" label={t('res.table')} error={errors.tableid}>
          <select id="res-table" className="form-select" value={form.tableId} onChange={set('tableId')}>
            <option value="">{t('res.anyTable')}</option>
            {tables.map((tb) => <option key={tb.id} value={String(tb.id)}>{t('res.tableN', { number: tb.tableNumber })}</option>)}
          </select>
        </Field>
        <Field id="res-note" label={t('res.note')} error={errors.note}>
          <input id="res-note" className="form-control" maxLength={200} autoComplete="off" value={form.note} onChange={set('note')} />
        </Field>
      </FormDialog>
    </main>
  );
}
