import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import { useAuth } from '../AuthContext';
import StateMessage from '../ui/StateMessage';
import FormDialog from '../ui/FormDialog';
import Pager, { usePaged } from '../ui/Pager';
import Icon from '../ui/Icon';
import { readErrors, send, Field } from '../ui/forms';
import { formatMoney, parseServerDate } from '../ui/format';

const blank = { name: '', phone: '', consent: false };
const shortDate = (value) => parseServerDate(value)?.toLocaleDateString() ?? '';

// Owner and managers: turn loyalty on and set how many points a bill gives.
function LoyaltySettingsCard() {
  const t = useT();
  const { showToast } = useToast();
  const [s, setS] = useState(null);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    fetch('/api/customers/settings').then((res) => (res.ok ? res.json() : null))
      .then((d) => { if (alive && d) setS({ enabled: !!d.enabled, rupeesPerPoint: String(d.rupeesPerPoint), pointValue: String(d.pointValue) }); })
      .catch(() => {});
    return () => { alive = false; };
  }, []);

  if (!s) return null;

  const save = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      const res = await send('/api/customers/settings', 'PUT',
        { enabled: s.enabled, rupeesPerPoint: Number(s.rupeesPerPoint) || 0, pointValue: Number(s.pointValue) || 0 });
      if (!res.ok) { setErrors(await readErrors(res)); return; }
      setErrors({});
      showToast(t('loyalty.saved'));
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="pos-card mb-3 d-flex flex-column gap-3" onSubmit={save} noValidate>
      <h2 className="h5 mb-0">{t('loyalty.title')}</h2>
      <div className="form-check">
        <input id="loy-on" type="checkbox" className="form-check-input" checked={s.enabled} onChange={(e) => setS({ ...s, enabled: e.target.checked })} />
        <label htmlFor="loy-on" className="form-check-label">{t('loyalty.enabled')}</label>
      </div>
      <div className="d-flex flex-wrap gap-3">
        <Field id="loy-rate" label={t('loyalty.rupeesPerPoint')} error={errors.rupeesperpoint}>
          <input id="loy-rate" type="number" min={1} max={10000} className="form-control" value={s.rupeesPerPoint} onChange={(e) => setS({ ...s, rupeesPerPoint: e.target.value })} />
        </Field>
        <Field id="loy-value" label={t('loyalty.pointValue')} error={errors.pointvalue}>
          <input id="loy-value" type="number" min={0} max={100} step="0.01" className="form-control" value={s.pointValue} onChange={(e) => setS({ ...s, pointValue: e.target.value })} />
        </Field>
      </div>
      <p className="pos-hint mb-0">{t('loyalty.hint')}</p>
      {errors.general !== undefined && <div className="invalid-feedback d-block">{errors.general || t('common.error')}</div>}
      <div><button type="submit" className="btn btn-primary px-3" disabled={busy}>{t('loyalty.save')}</button></div>
    </form>
  );
}

// Guests who agreed to collect points (DPDP consent asked at the counter). Cashier adds and edits; managers also forget a guest.
export default function CustomersPage() {
  const t = useT();
  const { showToast } = useToast();
  const roles = useAuth()?.user?.roles ?? [];
  const isManager = roles.includes('manager') || roles.includes('admin');
  const [query, setQuery] = useState('');
  const [list, setList] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [editing, setEditing] = useState(null);
  const [form, setForm] = useState(blank);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const [history, setHistory] = useState(null);
  const [forgetting, setForgetting] = useState(null);
  const paged = usePaged(list, 20, 'customers');

  const load = useCallback(async (q) => {
    try {
      const res = await fetch(`/api/customers${q ? `?search=${encodeURIComponent(q)}` : ''}`);
      if (!res.ok) throw new Error('load');
      setList(await res.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    const timer = setTimeout(() => load(query.trim()), query ? 300 : 0);
    return () => clearTimeout(timer);
  }, [load, query]);

  const open = (c) => {
    setErrors({});
    setEditing(c ?? {});
    setForm(c ? { name: c.name, phone: c.phone, consent: true } : blank);
  };

  const save = async () => {
    if (!editing.id && !form.consent) { setErrors({ consent: t('cust.consentNeeded') }); return; }
    setBusy(true);
    try {
      const body = { name: form.name.trim(), phone: form.phone.trim(), consent: form.consent };
      const res = await send(editing.id ? `/api/customers/${editing.id}` : '/api/customers', editing.id ? 'PUT' : 'POST', body);
      if (!res.ok) { setErrors(await readErrors(res)); return; }
      const saved = await res.json();
      setEditing(null);
      showToast(t('cust.saved', { name: saved.name }));
      load(query.trim());
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const showHistory = async (c) => {
    try {
      const res = await fetch(`/api/customers/${c.id}/moves`);
      if (!res.ok) throw new Error('load');
      setHistory({ guest: c, moves: await res.json() });
    } catch {
      showToast(t('common.error'), 'error');
    }
  };

  const forget = async () => {
    const guest = forgetting;
    setBusy(true);
    try {
      const res = await send(`/api/customers/${guest.id}`, 'DELETE');
      if (!res.ok) throw new Error('forget');
      setForgetting(null);
      showToast(t('cust.forgotten', { name: guest.name }));
      load(query.trim());
    } catch {
      showToast(t('common.error'), 'error');
    } finally {
      setBusy(false);
    }
  };

  const generalError = errors.general !== undefined ? (errors.general || t('common.error')) : undefined;

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('cust.title')}</h1>
        <button type="button" className="btn btn-primary px-3" onClick={() => open(null)}>{t('cust.add')}</button>
      </div>

      {isManager && <LoyaltySettingsCard />}

      <div className="mb-3" style={{ maxWidth: 360 }}>
        <label htmlFor="cust-search" className="form-label">{t('cust.search')}</label>
        <input id="cust-search" type="search" className="form-control" autoComplete="off" value={query} onChange={(e) => setQuery(e.target.value)} />
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={() => load(query.trim())} />}
      {loadState === 'ready' && list.length === 0 && <StateMessage kind="empty" message={t('cust.empty')} />}
      {loadState === 'ready' && list.length > 0 && (
        <>
          <Pager page={paged.page} pageCount={paged.pageCount} total={paged.total} onPage={paged.setPage} pageSize={paged.pageSize} onPageSize={paged.setPageSize} />
          <div className="pos-card table-responsive">
            <table className="table align-middle">
              <thead>
                <tr>
                  <th scope="col">{t('cust.name')}</th>
                  <th scope="col">{t('cust.phone')}</th>
                  <th scope="col" className="text-end">{t('cust.points')}</th>
                  <th scope="col" className="text-end">{t('cust.visits')}</th>
                  <th scope="col" className="text-end">{t('cust.spent')}</th>
                  <th scope="col">{t('cust.lastVisit')}</th>
                  <th scope="col">{t('cust.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {paged.pageItems.map((c) => (
                  <tr key={c.id}>
                    <td className="fw-semibold">{c.name}</td>
                    <td><a href={`tel:${c.phone}`}>{c.phone}</a></td>
                    <td className="text-end fw-semibold">{c.points}</td>
                    <td className="text-end">{c.visits}</td>
                    <td className="text-end">{formatMoney(c.spent)}</td>
                    <td>{shortDate(c.lastVisitAt)}</td>
                    <td>
                      <div className="d-flex flex-wrap gap-2">
                        <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={`${t('cust.history')} ${c.name}`} onClick={() => showHistory(c)}><Icon name="receipt" />{t('cust.history')}</button>
                        <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={`${t('cust.edit')} ${c.name}`} onClick={() => open(c)}><Icon name="pen" />{t('cust.edit')}</button>
                        {isManager && (
                          <button type="button" className="btn btn-sm btn-outline-danger" aria-label={`${t('cust.forget')} ${c.name}`} onClick={() => setForgetting(c)}><Icon name="trash-can" />{t('cust.forget')}</button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <FormDialog open={editing !== null} title={editing?.id ? t('cust.edit') : t('cust.add')} submitLabel={t('cust.save')}
        busy={busy} error={generalError} onCancel={() => setEditing(null)} onSubmit={save}>
        <Field id="cust-name" label={t('cust.name')} error={errors.name}>
          <input id="cust-name" className="form-control" maxLength={60} autoComplete="off" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </Field>
        <Field id="cust-phone" label={t('cust.phone')} error={errors.phone}>
          <input id="cust-phone" className="form-control" inputMode="tel" maxLength={20} autoComplete="off" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
        </Field>
        {!editing?.id && (
          <div>
            <div className="form-check">
              <input id="cust-consent" type="checkbox" className="form-check-input" checked={form.consent} onChange={(e) => setForm({ ...form, consent: e.target.checked })} />
              <label htmlFor="cust-consent" className="form-check-label">{t('cust.consent')}</label>
            </div>
            {errors.consent && <div className="invalid-feedback d-block">{errors.consent}</div>}
          </div>
        )}
      </FormDialog>

      <FormDialog open={history !== null} size="wide" hideCancel title={history ? t('cust.historyTitle', { name: history.guest.name }) : ''}
        submitLabel={t('common.close')} onCancel={() => setHistory(null)} onSubmit={() => setHistory(null)}>
        {history && history.moves.length === 0 && <p className="mb-0">{t('cust.noHistory')}</p>}
        {history && history.moves.length > 0 && (
          <div className="table-responsive">
            <table className="table align-middle">
              <thead>
                <tr>
                  <th scope="col">{t('cust.lastVisit')}</th>
                  <th scope="col">{t('cust.kindCol')}</th>
                  <th scope="col" className="text-end">{t('cust.points')}</th>
                  <th scope="col" className="text-end">{t('cust.balance')}</th>
                  <th scope="col">{t('cust.note')}</th>
                </tr>
              </thead>
              <tbody>
                {history.moves.map((m) => (
                  <tr key={m.id}>
                    <td>{shortDate(m.at)}</td>
                    <td>{t(`cust.kind.${m.kind}`)}</td>
                    <td className="text-end">{m.points > 0 ? `+${m.points}` : m.points}</td>
                    <td className="text-end">{m.balanceAfter}</td>
                    <td>{m.note ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </FormDialog>

      <FormDialog open={forgetting !== null} title={forgetting ? t('cust.forgetTitle', { name: forgetting.name }) : ''} submitLabel={t('cust.forget')}
        busy={busy} onCancel={() => setForgetting(null)} onSubmit={forget}>
        <p className="mb-0">{t('cust.forgetText')}</p>
      </FormDialog>
    </main>
  );
}
