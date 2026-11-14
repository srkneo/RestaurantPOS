import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import ConfirmDialog from '../ui/ConfirmDialog';
import FormDialog from '../ui/FormDialog';
import StateMessage from '../ui/StateMessage';
import { VegMark } from '../components/POSMenu';
import { formatMoney } from '../ui/format';
import '../components/OrderHistory.css';
import './StaffPage.css';
import './MenuPage.css';

const GST_RATES = [0, 5, 12, 18, 28];
const API = '/api/admin/menu';

async function readErrors(res) {
  try {
    const data = await res.json();
    if (data?.errors) {
      return Object.fromEntries(Object.entries(data.errors).map(([k, v]) => [k.toLowerCase(), Array.isArray(v) ? v.join(' ') : String(v)]));
    }
    return { general: data?.message || data?.title || '' };
  } catch {
    return { general: '' };
  }
}

function send(url, method, body) {
  return fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
}

function Field({ id, label, error, children }) {
  return (
    <div>
      <label htmlFor={id} className="form-label">{label}</label>
      {children}
      {error && <div className="invalid-feedback d-block">{error}</div>}
    </div>
  );
}

// Owner and manager page: categories, items, prices, GST rate, veg mark, sold out.
export default function MenuPage() {
  const t = useT();
  const { showToast } = useToast();
  const [categories, setCategories] = useState([]);
  const [items, setItems] = useState([]);
  const [loadState, setLoadState] = useState('loading');
  const [dialog, setDialog] = useState(null); // 'item' | 'category'
  const [current, setCurrent] = useState(null);
  const [form, setForm] = useState({});
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState(null);

  const load = useCallback(async () => {
    try {
      const [catRes, itemRes] = await Promise.all([fetch(`${API}/categories`), fetch(`${API}/items`)]);
      if (!catRes.ok || !itemRes.ok) throw new Error('load failed');
      setCategories(await catRes.json());
      setItems(await itemRes.json());
      setLoadState('ready');
    } catch {
      setLoadState('error');
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const set = (field) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setForm((f) => ({ ...f, [field]: value }));
  };

  const openItem = (item) => {
    setCurrent(item);
    setErrors({});
    setForm(item
      ? { name: item.name, categoryId: String(item.categoryId ?? ''), price: String(item.price), taxRate: String(item.taxRate), description: item.description ?? '', isVeg: item.isVeg, isAvailable: item.isAvailable, sortOrder: String(item.sortOrder) }
      : { name: '', categoryId: String(categories[0]?.id ?? ''), price: '', taxRate: '5', description: '', isVeg: true, isAvailable: true, sortOrder: '0' });
    setDialog('item');
  };

  const openCategory = (category) => {
    setCurrent(category);
    setErrors({});
    setForm({ name: category?.name ?? '', sortOrder: String(category?.sortOrder ?? categories.length + 1) });
    setDialog('category');
  };

  const close = useCallback(() => setDialog(null), []);

  const save = async () => {
    const local = {};
    if (!form.name?.trim()) local.name = t('menu.nameRequired');
    if (dialog === 'item') {
      if (form.price === '' || Number.isNaN(Number(form.price)) || Number(form.price) < 0) local.price = t('menu.priceInvalid');
      if (!form.categoryId) local.categoryid = t('menu.categoryRequired');
    }
    if (Object.keys(local).length > 0) {
      setErrors(local);
      return;
    }
    setBusy(true);
    try {
      let res;
      if (dialog === 'item') {
        const body = {
          name: form.name.trim(), price: Number(form.price), categoryId: Number(form.categoryId), description: form.description.trim() || null,
          isVeg: form.isVeg, isAvailable: form.isAvailable, taxRate: Number(form.taxRate), sortOrder: Number(form.sortOrder) || 0,
        };
        res = current ? await send(`${API}/items/${current.id}`, 'PUT', body) : await send(`${API}/items`, 'POST', body);
      } else {
        const body = { name: form.name.trim(), sortOrder: Number(form.sortOrder) || 0, isActive: true };
        res = current ? await send(`${API}/categories/${current.id}`, 'PUT', body) : await send(`${API}/categories`, 'POST', body);
      }
      if (!res.ok) {
        setErrors(await readErrors(res));
        return;
      }
      showToast(t('menu.saved'));
      setDialog(null);
      load();
    } catch {
      setErrors({ general: t('pos.networkError') });
    } finally {
      setBusy(false);
    }
  };

  const toggleAvailable = async (item) => {
    const next = !item.isAvailable;
    setItems((list) => list.map((i) => (i.id === item.id ? { ...i, isAvailable: next } : i)));
    try {
      const res = await send(`${API}/items/${item.id}/availability`, 'PUT', { isAvailable: next });
      if (!res.ok) throw new Error('failed');
      showToast(t('menu.availabilityChanged', { name: item.name, state: next ? t('menu.available') : t('menu.soldOut') }));
    } catch {
      setItems((list) => list.map((i) => (i.id === item.id ? { ...i, isAvailable: item.isAvailable } : i)));
      showToast(t('common.error'), 'error');
    }
  };

  const confirmDelete = async () => {
    const item = deleting;
    setDeleting(null);
    const res = await send(`${API}/items/${item.id}`, 'DELETE').catch(() => null);
    if (res?.ok) {
      showToast(t('menu.deleted', { name: item.name }));
      load();
    } else {
      showToast(t('common.error'), 'error');
    }
  };

  const generalError = errors.general !== undefined ? (errors.general || t('common.error')) : null;

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{t('menu.title')}</h1>
        <div className="d-flex flex-wrap gap-2">
          <Link className="btn btn-outline-primary px-3" to="/admin/menu/scan">{t('menu.scan')}</Link>
          <button type="button" className="btn btn-primary px-3" disabled={categories.length === 0} onClick={() => openItem(null)}>{t('menu.addItem')}</button>
        </div>
      </div>

      {loadState === 'loading' && <StateMessage kind="loading" />}
      {loadState === 'error' && <StateMessage kind="error" onRetry={load} />}
      {loadState === 'ready' && (
        <>
          <section className="pos-card pos-menu-cats" aria-labelledby="menu-cats-title">
            <div className="d-flex justify-content-between align-items-center gap-2">
              <h2 id="menu-cats-title" className="h5 mb-0">{t('menu.categories')}</h2>
              <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => openCategory(null)}>{t('menu.addCategory')}</button>
            </div>
            {categories.length === 0 ? (
              <p className="pos-hint mb-0">{t('menu.noCategories')}</p>
            ) : (
              <div className="pos-menu-cat-list">
                {categories.map((c) => (
                  <button key={c.id} type="button" className="pos-chip" onClick={() => openCategory(c)} aria-label={t('menu.editItem', { name: c.name })}>
                    {c.name}
                  </button>
                ))}
              </div>
            )}
          </section>

          {items.length === 0 ? (
            <StateMessage kind="empty" message={t('menu.empty')} />
          ) : (
            <div className="pos-card table-responsive">
              <table className="table align-middle">
                <thead>
                  <tr>
                    <th scope="col">{t('menu.name')}</th>
                    <th scope="col">{t('menu.category')}</th>
                    <th scope="col" className="text-end">{t('menu.price')}</th>
                    <th scope="col" className="text-end">{t('menu.taxRate')}</th>
                    <th scope="col">{t('menu.available')}</th>
                    <th scope="col"><span className="visually-hidden">{t('staff.actions')}</span></th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item) => (
                    <tr key={item.id}>
                      <td className="fw-semibold"><VegMark isVeg={item.isVeg} />{item.name}</td>
                      <td>{item.category}</td>
                      <td className="text-end text-nowrap">{formatMoney(item.price)}</td>
                      <td className="text-end">{item.taxRate}%</td>
                      <td>
                        <div className="form-check form-switch pos-switch">
                          <input className="form-check-input" type="checkbox" role="switch" id={`avail-${item.id}`} checked={item.isAvailable}
                            onChange={() => toggleAvailable(item)} aria-label={`${item.name}: ${t('menu.available')}`} />
                          <label className="form-check-label" htmlFor={`avail-${item.id}`}>{item.isAvailable ? t('menu.available') : t('menu.soldOut')}</label>
                        </div>
                      </td>
                      <td>
                        <div className="pos-row-actions">
                          <button type="button" className="btn btn-outline-secondary btn-sm px-3" onClick={() => openItem(item)}>{t('menu.edit')}</button>
                          <button type="button" className="btn btn-outline-danger btn-sm px-3" onClick={() => setDeleting(item)}>{t('menu.delete')}</button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      <FormDialog open={dialog === 'item'} title={current ? t('menu.editItem', { name: current.name }) : t('menu.addItem')} submitLabel={t('staff.save')}
        busy={busy} error={generalError} onCancel={close} onSubmit={save}>
        <Field id="item-name" label={t('menu.name')} error={errors.name}>
          <input id="item-name" className={`form-control${errors.name ? ' is-invalid' : ''}`} maxLength={100} value={form.name ?? ''} onChange={set('name')} />
        </Field>
        <Field id="item-category" label={t('menu.category')} error={errors.categoryid}>
          <select id="item-category" className={`form-select${errors.categoryid ? ' is-invalid' : ''}`} value={form.categoryId ?? ''} onChange={set('categoryId')}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </Field>
        <div className="d-flex gap-3">
          <div className="flex-grow-1">
            <Field id="item-price" label={t('menu.price')} error={errors.price}>
              <input id="item-price" type="number" inputMode="decimal" min="0" step="0.5" className={`form-control${errors.price ? ' is-invalid' : ''}`}
                value={form.price ?? ''} onChange={set('price')} />
            </Field>
          </div>
          <div className="flex-grow-1">
            <Field id="item-tax" label={t('menu.taxRate')} error={errors.taxrate}>
              <select id="item-tax" className="form-select" value={form.taxRate ?? '5'} onChange={set('taxRate')}>
                {GST_RATES.map((r) => <option key={r} value={r}>{r}%</option>)}
              </select>
            </Field>
          </div>
        </div>
        <Field id="item-description" label={t('menu.description')} error={errors.description}>
          <textarea id="item-description" className="form-control" rows={2} maxLength={500} value={form.description ?? ''} onChange={set('description')} />
        </Field>
        <div className="d-flex gap-4 flex-wrap">
          <div className="pos-check">
            <input id="item-veg" type="checkbox" className="form-check-input" checked={!!form.isVeg} onChange={set('isVeg')} />
            <label htmlFor="item-veg" className="mb-0 fw-semibold">{t('menu.veg')}</label>
          </div>
          <div className="pos-check">
            <input id="item-available" type="checkbox" className="form-check-input" checked={!!form.isAvailable} onChange={set('isAvailable')} />
            <label htmlFor="item-available" className="mb-0 fw-semibold">{t('menu.available')}</label>
          </div>
        </div>
        <Field id="item-sort" label={t('menu.sortOrder')}>
          <input id="item-sort" type="number" className="form-control" value={form.sortOrder ?? '0'} onChange={set('sortOrder')} />
        </Field>
      </FormDialog>

      <FormDialog open={dialog === 'category'} title={current ? t('menu.editItem', { name: current.name }) : t('menu.addCategory')} submitLabel={t('staff.save')}
        busy={busy} error={generalError} onCancel={close} onSubmit={save}>
        <Field id="category-name" label={t('menu.categoryName')} error={errors.name}>
          <input id="category-name" className={`form-control${errors.name ? ' is-invalid' : ''}`} maxLength={100} value={form.name ?? ''} onChange={set('name')} />
        </Field>
        <Field id="category-sort" label={t('menu.sortOrder')}>
          <input id="category-sort" type="number" className="form-control" value={form.sortOrder ?? '0'} onChange={set('sortOrder')} />
        </Field>
      </FormDialog>

      <ConfirmDialog open={!!deleting} title={t('menu.deleteTitle', { name: deleting?.name ?? '' })} message={t('menu.deleteHint')}
        confirmLabel={t('menu.delete')} danger onCancel={() => setDeleting(null)} onConfirm={confirmDelete} />
    </main>
  );
}
