import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import { MAX_PHOTOS, CSV_TEMPLATE, toRows, emptyRow, rowProblem, toImportItems, csvToRows, shrinkImage } from './menuScan';
import '../components/OrderHistory.css';
import './StaffPage.css';

const GST_RATES = [0, 5, 12, 18, 28];

export default function MenuScanPage() {
  const t = useT();
  const { showToast } = useToast();
  const navigate = useNavigate();

  const [step, setStep] = useState('pick');
  const [photos, setPhotos] = useState([]);
  const [rows, setRows] = useState([]);
  const [taxRate, setTaxRate] = useState('5');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [scansLeft, setScansLeft] = useState(null);

  const choosePhotos = (e) => {
    const files = Array.from(e.target.files || []);
    if (files.length > MAX_PHOTOS) {
      setPhotos([]);
      setError(t('scan.tooMany'));
    } else {
      setPhotos(files);
      setError('');
    }
  };

  const readMenu = async () => {
    setStep('reading');
    setError('');
    try {
      const form = new FormData();
      for (const photo of photos) form.append('photos', await shrinkImage(photo));
      const res = await fetch('/api/admin/menu/scan', { method: 'POST', body: form });
      if (res.ok) {
        const data = await res.json();
        setScansLeft(data.scansLeftToday);
        if (!data.items || data.items.length === 0) {
          setError(t('scan.nothingFound'));
          setStep('pick');
        } else {
          setRows(toRows(data.items));
          setStep('review');
        }
      } else {
        const errs = await readErrors(res);
        setError(errs.photos || errs.general || t('common.error'));
        setStep('pick');
      }
    } catch {
      setError(t('common.error'));
      setStep('pick');
    }
  };

  const chooseCsv = async (e) => {
    const file = (e.target.files || [])[0];
    if (!file) return;
    const found = csvToRows(await file.text());
    if (found.length === 0) {
      setError(t('scan.nothingFound'));
      return;
    }
    setRows(found);
    setError('');
    setStep('review');
  };

  const update = (key, field, value) => {
    setRows(rows.map((r) => (r.key === key ? { ...r, [field]: value } : r)));
  };

  const count = rows.filter((r) => r.include).length;

  const save = async () => {
    const index = rows.findIndex((r) => rowProblem(r) !== null);
    if (index >= 0) {
      setError(t(rowProblem(rows[index]) === 'name' ? 'scan.rowName' : 'scan.rowPrice', { row: index + 1 }));
      return;
    }
    setBusy(true);
    setError('');
    try {
      const res = await send('/api/admin/menu/import', 'POST', { items: toImportItems(rows), taxRate: Number(taxRate) });
      if (res.ok) {
        const data = await res.json();
        showToast(t('scan.saved', { created: data.created, skipped: data.skipped }));
        navigate('/admin/menu');
      } else {
        const errs = await readErrors(res);
        setError(errs.items || errs.general || t('common.error'));
      }
    } finally {
      setBusy(false);
    }
  };

  const startAgain = () => {
    setStep('pick');
    setRows([]);
    setPhotos([]);
    setError('');
  };

  return (
    <main className="pos-page">
      <div className="pos-page-head">
        <h1>{step === 'review' ? t('scan.review') : t('scan.title')}</h1>
        <Link className="btn btn-outline-secondary px-3" to="/admin/menu">{t('scan.back')}</Link>
      </div>
      {error && <p className="text-danger" role="alert">{error}</p>}
      {step === 'reading' && <StateMessage kind="loading" message={t('scan.reading')} />}
      {step === 'pick' && (
        <div className="pos-card d-flex flex-column gap-3">
          <p className="mb-0">{t('scan.intro')}</p>
          <Field id="scan-photos" label={t('scan.photos')}>
            <input id="scan-photos" type="file" className="form-control" accept="image/jpeg,image/png,image/webp" multiple onChange={choosePhotos} />
          </Field>
          {photos.length > 0 && <p className="mb-0">{t('scan.selected', { count: photos.length })}</p>}
          <div>
            <button type="button" className="btn btn-primary px-3" disabled={photos.length === 0} onClick={readMenu}>{t('scan.read')}</button>
          </div>
          <hr className="my-1" />
          <p className="mb-0">{t('scan.orCsv')}</p>
          <Field id="scan-csv" label={t('scan.csvFile')}>
            <input id="scan-csv" type="file" className="form-control" accept=".csv,text/csv" onChange={chooseCsv} />
          </Field>
          <a href={`data:text/csv;charset=utf-8,${encodeURIComponent(CSV_TEMPLATE)}`} download="menu-sample.csv">{t('scan.template')}</a>
        </div>
      )}
      {step === 'review' && (
        <>
          <p>{t('scan.reviewHint')}</p>
          {scansLeft !== null && <p className="pos-hint">{t('scan.scansLeft', { count: scansLeft })}</p>}
          <div className="mb-3" style={{ maxWidth: 220 }}>
            <Field id="scan-tax" label={t('menu.taxRate')}>
              <select id="scan-tax" className="form-select" value={taxRate} onChange={(e) => setTaxRate(e.target.value)}>
                {GST_RATES.map((r) => <option key={r} value={String(r)}>{r}</option>)}
              </select>
            </Field>
          </div>
          <div className="pos-card table-responsive">
            <table className="table align-middle">
              <thead>
                <tr>
                  <th>{t('scan.add')}</th>
                  <th>{t('menu.category')}</th>
                  <th>{t('menu.name')}</th>
                  <th>{t('menu.price')}</th>
                  <th>{t('menu.veg')}</th>
                  <th>{t('menu.description')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => (
                  <tr key={row.key}>
                    <td>
                      <input type="checkbox" className="form-check-input" aria-label={`${t('scan.add')} ${row.name}`} checked={row.include} onChange={(e) => update(row.key, 'include', e.target.checked)} />
                      {row.alreadyOnMenu && <span className="pos-status pos-status-inactive ms-2">{t('scan.alreadyOnMenu')}</span>}
                    </td>
                    <td>
                      <input className="form-control" aria-label={`${t('menu.category')} ${i + 1}`} maxLength={100} value={row.category} onChange={(e) => update(row.key, 'category', e.target.value)} />
                    </td>
                    <td>
                      <input className="form-control" aria-label={`${t('menu.name')} ${i + 1}`} maxLength={100} value={row.name} onChange={(e) => update(row.key, 'name', e.target.value)} />
                    </td>
                    <td>
                      <input type="text" inputMode="decimal" className="form-control" style={{ width: 110 }} aria-label={`${t('menu.price')} ${i + 1}`} value={row.price} onChange={(e) => update(row.key, 'price', e.target.value)} />
                    </td>
                    <td>
                      <input type="checkbox" className="form-check-input" aria-label={`${t('menu.veg')} ${i + 1}`} checked={row.isVeg} onChange={(e) => update(row.key, 'isVeg', e.target.checked)} />
                    </td>
                    <td>
                      <input className="form-control" aria-label={`${t('menu.description')} ${i + 1}`} maxLength={500} value={row.description} onChange={(e) => update(row.key, 'description', e.target.value)} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="d-flex flex-wrap gap-2 mt-3">
            <button type="button" className="btn btn-outline-secondary px-3" onClick={() => setRows([...rows, emptyRow()])}>{t('scan.addRow')}</button>
            <button type="button" className="btn btn-outline-secondary px-3" onClick={startAgain}>{t('scan.startAgain')}</button>
            <button type="button" className="btn btn-primary px-3" disabled={count === 0 || busy} onClick={save}>{t('scan.save', { count })}</button>
          </div>
        </>
      )}
    </main>
  );
}
