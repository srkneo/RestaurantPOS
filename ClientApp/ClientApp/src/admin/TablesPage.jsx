import React, { useCallback, useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import { useToast } from '../ui/ToastContext';
import StateMessage from '../ui/StateMessage';
import { readErrors, send, Field } from '../ui/forms';
import '../components/OrderHistory.css';
import './StaffPage.css';
import FormDialog from '../ui/FormDialog';

export default function TablesPage() {
    const t = useT();
    const { showToast } = useToast();

    const [tables, setTables] = useState([]);
    const [loadState, setLoadState] = useState('loading');
    const [mode, setMode] = useState(null);
    const [current, setCurrent] = useState(null);
    const [form, setForm] = useState({ tableNumber: '', sortOrder: '0', isActive: true });
    const [errors, setErrors] = useState({});
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            const res = await fetch('/api/admin/tables');
            if (res.ok) {
                setTables(await res.json());
                setLoadState('ready');
            } else {
                setLoadState('error');
            }
        } catch (error) {
            setLoadState('error');
        }
    }, []);

    useEffect(() => {
        load();
    }, [load]);

    const handleTableNumberChange = (e) => {
        setForm({ ...form, tableNumber: e.target.value });
    };

    const handleSortOrderChange = (e) => {
        setForm({ ...form, sortOrder: e.target.value });
    };

    const handleIsActiveChange = (e) => {
        setForm({ ...form, isActive: e.target.checked });
    };

    const submit = async () => {
        if (form.tableNumber.trim() === '') {
            setErrors({ tablenumber: t('tables.numberRequired') });
            return;
        }

        const body = {
            tableNumber: form.tableNumber.trim(),
            sortOrder: Number(form.sortOrder) || 0,
            isActive: form.isActive
        };

        setBusy(true);
        let res;
        if (mode === 'add') {
            res = await send('/api/admin/tables', 'POST', body);
        } else {
            res = await send(`/api/admin/tables/${current.id}`, 'PUT', body);
        }

        if (!res.ok) {
            setErrors(await readErrors(res));
        } else {
            showToast(t('tables.saved'));
            setMode(null);
            load();
        }
        setBusy(false);
    };

    return (
        <main className="pos-page">
            <div className="pos-page-head">
                <h1>{t('tables.title')}</h1>
                <button className="btn btn-primary px-3" onClick={() => {
                    setMode('add');
                    setCurrent(null);
                    setErrors({});
                    setForm({ tableNumber: '', sortOrder: String(tables.length + 1), isActive: true });
                }}>
                    {t('tables.add')}
                </button>
            </div>

            <StateMessage kind={loadState} onRetry={load} message={t('tables.empty')} />

            {loadState === 'ready' && tables.length > 0 && (
                <div className="pos-card table-responsive">
                    <table className="table align-middle">
                        <thead>
                            <tr>
                                <th>{t('tables.number')}</th>
                                <th>{t('tables.position')}</th>
                                <th>{t('staff.status')}</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {tables.map(table => (
                                <tr key={table.id}>
                                    <td className="fw-semibold">{table.tableNumber}</td>
                                    <td>{table.sortOrder}</td>
                                    <td>
                                        <span className={`pos-status ${table.isActive ? 'pos-status-active' : 'pos-status-inactive'}`}>
                                            {table.isActive ? t('tables.inUse') : t('tables.notInUse')}
                                        </span>
                                    </td>
                                    <td>
                                        <button className="btn btn-outline-secondary btn-sm px-3" onClick={() => {
                                            setMode('edit');
                                            setCurrent(table);
                                            setErrors({});
                                            setForm({ tableNumber: table.tableNumber, sortOrder: String(table.sortOrder), isActive: table.isActive });
                                        }}>
                                            {t('tables.edit')}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            <FormDialog
                open={mode !== null}
                title={mode === 'add' ? t('tables.add') : t('tables.editTitle', { name: current?.tableNumber ?? '' })}
                submitLabel={t('staff.save')}
                busy={busy}
                error={errors.general !== undefined ? (errors.general || t('common.error')) : null}
                onCancel={() => setMode(null)}
                onSubmit={submit}
            >
                <Field id="table-number" label={t('tables.number')} error={errors.tablenumber}>
                    <input id="table-number" className="form-control" maxLength={20} value={form.tableNumber} onChange={handleTableNumberChange} />
                </Field>
                <Field id="table-position" label={t('tables.position')}>
                    <input id="table-position" type="number" className="form-control" value={form.sortOrder} onChange={handleSortOrderChange} />
                </Field>
                <div className="pos-check">
                    <input id="table-active" type="checkbox" className="form-check-input" checked={form.isActive} onChange={handleIsActiveChange} />
                    <label htmlFor="table-active" className="mb-0 fw-semibold">{t('tables.inUse')}</label>
                </div>
            </FormDialog>
        </main>
    );
}
