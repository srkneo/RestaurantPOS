import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import Pager, { usePaged } from '../ui/Pager';
import { parseServerDate } from '../ui/format';
import { describeDetails, typeName } from './activityDetails';

const when = (value) => {
  const date = parseServerDate(value);
  return date ? new Intl.DateTimeFormat('en-IN', { timeZone: 'Asia/Kolkata', dateStyle: 'medium', timeStyle: 'short' }).format(date) : '';
};

// UI1: Activity log. The owner reads /api/admin/audit; the platform owner reads one restaurant (tenantId).
// The server answers a plain array, newest first.
export default function ActivityLogPage({ tenantId }) {
  const t = useT();
  const [rows, setRows] = useState([]);
  const [state, setState] = useState('loading');
  const [type, setType] = useState('all');
  const url = tenantId ? `/api/platform/tenants/${tenantId}/audit?take=500` : '/api/admin/audit?take=500';

  const load = useCallback(async () => {
    setState('loading');
    try {
      const res = await fetch(url);
      if (!res.ok) throw new Error(String(res.status));
      const body = await res.json();
      setRows(Array.isArray(body) ? body : []);
      setState('ready');
    } catch {
      setState('error');
    }
  }, [url]);

  useEffect(() => { load(); }, [load]);

  const types = useMemo(() => [...new Set(rows.map((r) => r.entityType))].sort((a, b) => a.localeCompare(b)), [rows]);
  const filtered = useMemo(() => (type === 'all' ? rows : rows.filter((r) => r.entityType === type)), [rows, type]);
  const { page, pageCount, pageItems, setPage, total } = usePaged(filtered, 20);

  const actionName = (action) => {
    const key = `activity.action.${action}`;
    const text = t(key);
    return text === key ? action : text;
  };
  const changeType = (e) => {
    setType(e.target.value);
    setPage(1);
  };

  return (
    <main className="container py-3 d-flex flex-column gap-3">
      <div className="d-flex justify-content-between align-items-center flex-wrap gap-2">
        <h1 className="h3 mb-0">{t('activity.title')}</h1>
        {tenantId && <a className="btn btn-outline-secondary" href="/platform">{t('activity.back')}</a>}
      </div>
      <p className="pos-hint mb-0">{t('activity.hint')}</p>
      {state === 'loading' && <StateMessage kind="loading" />}
      {state === 'error' && <StateMessage kind="error" onRetry={load} />}
      {state === 'ready' && rows.length === 0 && <p className="pos-hint">{t('activity.none')}</p>}
      {state === 'ready' && rows.length > 0 && (
        <>
          <div>
            <label htmlFor="activity-type" className="form-label">{t('activity.type')}</label>
            <select id="activity-type" className="form-select" value={type} onChange={changeType}>
              <option value="all">{t('activity.typeAll')}</option>
              {types.map((x) => <option key={x} value={x}>{typeName(x)}</option>)}
            </select>
          </div>
          {filtered.length === 0 ? (
            <p className="pos-hint mb-0">{t('activity.noMatch')}</p>
          ) : (
            <table className="table table-sm align-middle">
              <thead>
                <tr>
                  <th>{t('activity.when')}</th>
                  <th>{t('activity.who')}</th>
                  <th>{t('activity.what')}</th>
                  <th>{t('activity.details')}</th>
                </tr>
              </thead>
              <tbody>
                {pageItems.map((r) => (
                  <tr key={r.id} className={r.support ? 'table-warning' : ''}>
                    <td>{when(r.createdAt)}</td>
                    <td>
                      {r.by}
                      {r.support && <span className="badge text-bg-warning ms-2">{t('activity.support')}</span>}
                    </td>
                    <td><span>{`${actionName(r.action)} · ${typeName(r.entityType)}${r.entityId ? ` #${r.entityId}` : ''}`}</span></td>
                    <td><span className="text-break">{describeDetails(r.details)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <Pager page={page} pageCount={pageCount} total={total} onPage={setPage} />
        </>
      )}
    </main>
  );
}

export function PlatformActivityLogPage() {
  const { id } = useParams();
  return <ActivityLogPage tenantId={id} />;
}
