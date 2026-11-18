import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useT } from '../i18n/I18nContext';
import StateMessage from '../ui/StateMessage';
import BillReceipt from './BillReceipt';
import KotTicket from './KotTicket';
import DayReport from './DayReport';
import CreditNoteReceipt from './CreditNoteReceipt';

// Time for the logo and the UPI QR image to load before the print dialog opens.
export const PRINT_DELAY_MS = 400;

async function loadJson(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(String(res.status));
  return res.json();
}

function Toolbar({ onPrint }) {
  const t = useT();
  const navigate = useNavigate();
  return (
    <div className="print-toolbar">
      <button type="button" className="btn btn-primary px-4" onClick={onPrint}>{t('print.print')}</button>
      <button type="button" className="btn btn-outline-secondary px-4" onClick={() => (window.history.length > 1 ? navigate(-1) : window.close())}>{t('print.back')}</button>
    </div>
  );
}

// Shared flow: load, show, print once automatically when the address has ?auto=1.
function usePrintPage(load, afterPrint) {
  const [search] = useSearchParams();
  const [state, setState] = useState({ status: 'loading' });
  const autoDone = useRef(false);

  const reload = useCallback(async () => {
    setState({ status: 'loading' });
    try {
      setState({ status: 'ready', ...(await load()) });
    } catch {
      setState({ status: 'error' });
    }
  }, [load]);

  useEffect(() => { reload(); }, [reload]);

  const doPrint = useCallback(async () => {
    window.print();
    if (afterPrint) await afterPrint(setState);
  }, [afterPrint]);

  useEffect(() => {
    if (state.status !== 'ready' || autoDone.current || search.get('auto') !== '1') return undefined;
    // Marked inside the timer, so React's double effect run in development still prints once.
    const timer = setTimeout(() => {
      if (autoDone.current) return;
      autoDone.current = true;
      doPrint();
    }, PRINT_DELAY_MS);
    return () => clearTimeout(timer);
  }, [state.status, search, doPrint]);

  return { state, reload, doPrint };
}

export function PrintBillPage() {
  const { id } = useParams();
  const load = useCallback(async () => {
    const [bill, settings] = await Promise.all([loadJson(`/api/bills/${id}`), loadJson('/api/print-settings')]);
    const showQr = settings.upiReady && bill.status === 'Unpaid' && Number(bill.amountDue) > 0;
    return { bill, settings, duplicate: bill.printCount > 0, qrUrl: showQr ? `/api/bills/${id}/upi-qr.svg?due=${bill.amountDue}` : null };
  }, [id]);

  // Counted after printing; any later print of this bill says DUPLICATE.
  const afterPrint = useCallback(async (setState) => {
    try {
      await fetch(`/api/bills/${id}/printed`, { method: 'POST' });
    } catch {
      // the count is for the DUPLICATE mark only; printing itself already happened
    }
    setState((s) => ({ ...s, duplicate: true }));
  }, [id]);

  const { state, reload, doPrint } = usePrintPage(load, afterPrint);
  if (state.status === 'loading') return <StateMessage kind="loading" />;
  if (state.status === 'error') return <StateMessage kind="error" onRetry={reload} />;
  return (
    <>
      <Toolbar onPrint={doPrint} />
      <BillReceipt bill={state.bill} lang={state.settings.printLanguage} width={state.settings.paperWidth}
        duplicate={state.duplicate} qrUrl={state.qrUrl} />
    </>
  );
}

export function PrintKotPage() {
  const { id } = useParams();
  const load = useCallback(async () => {
    const [order, settings] = await Promise.all([loadJson(`/api/orders/${id}`), loadJson('/api/print-settings')]);
    return { order, settings };
  }, [id]);

  const [search] = useSearchParams();
  const round = Number(search.get('round')) || null;
  const { state, reload, doPrint } = usePrintPage(load, null);
  if (state.status === 'loading') return <StateMessage kind="loading" />;
  if (state.status === 'error') return <StateMessage kind="error" onRetry={reload} />;
  return (
    <>
      <Toolbar onPrint={doPrint} />
      <KotTicket order={state.order} lang={state.settings.printLanguage} width={state.settings.paperWidth} round={round} />
    </>
  );
}

// Z report of one business day (?date=yyyy-MM-dd, default today).
export function PrintDayPage() {
  const [search] = useSearchParams();
  const date = search.get('date') ?? '';
  const load = useCallback(async () => {
    const query = date ? `?date=${encodeURIComponent(date)}` : '';
    const [report, settings] = await Promise.all([loadJson(`/api/reports/day${query}`), loadJson('/api/print-settings')]);
    return { report, settings };
  }, [date]);

  const { state, reload, doPrint } = usePrintPage(load, null);
  if (state.status === 'loading') return <StateMessage kind="loading" />;
  if (state.status === 'error') return <StateMessage kind="error" onRetry={reload} />;
  return (
    <>
      <Toolbar onPrint={doPrint} />
      <DayReport report={state.report} lang={state.settings.printLanguage} width={state.settings.paperWidth} />
    </>
  );
}

export function PrintCreditNotePage() {
  const { id } = useParams();
  const load = useCallback(async () => {
    const [note, settings] = await Promise.all([loadJson(`/api/credit-notes/${id}`), loadJson('/api/print-settings')]);
    return { note, settings };
  }, [id]);

  const { state, reload, doPrint } = usePrintPage(load, null);
  if (state.status === 'loading') return <StateMessage kind="loading" />;
  if (state.status === 'error') return <StateMessage kind="error" onRetry={reload} />;
  return (
    <>
      <Toolbar onPrint={doPrint} />
      <CreditNoteReceipt note={state.note} lang={state.settings.printLanguage} width={state.settings.paperWidth} />
    </>
  );
}
