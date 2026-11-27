import React, { useState } from 'react';
import { useT } from '../i18n/I18nContext';

export function measureText(amount, unit) {
  if (unit === 'kg') {
    return amount < 1 ? `${Math.round(amount * 1000)} g` : `${Number(amount.toFixed(3))} kg`;
  } else if (unit === 'litre') {
    return amount < 1 ? `${Math.round(amount * 1000)} ml` : `${Number(amount.toFixed(3))} L`;
  }
  return `${amount} ${unit}`;
}

export default function WeightDialog({ item, onCancel, onAdd }) {
  const t = useT();
  const [amount, setAmount] = useState('');
  const [error, setError] = useState('');

  if (!item) return null;

  const handleAdd = () => {
    const v = Number(amount);
    if (v > 0 && v <= 100) {
      onAdd(v);
    } else {
      setError(t('weight.invalid'));
    }
  };

  return (
    <div role="dialog" aria-modal="true" aria-label={item.name}>
      <h1>{item.name} - {t('weight.title')}</h1>
      <div>
        <button onClick={() => onAdd(0.25)}>{measureText(0.25, item.sellUnit)}</button>
        <button onClick={() => onAdd(0.5)}>{measureText(0.5, item.sellUnit)}</button>
        <button onClick={() => onAdd(1)}>{measureText(1, item.sellUnit)}</button>
      </div>
      <div>
        <label htmlFor="weight-amount">{t(`weight.amount.${item.sellUnit}`)}</label>
        <input
          id="weight-amount"
          type="number"
          step="0.001"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />
        {error && <div>{error}</div>}
      </div>
      <button onClick={handleAdd}>{t('weight.add')}</button>
      <button onClick={onCancel}>{t('common.cancel')}</button>
    </div>
  );
}
