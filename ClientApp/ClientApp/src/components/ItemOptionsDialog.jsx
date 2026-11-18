import React, { useEffect, useState } from 'react';
import { useT } from '../i18n/I18nContext';
import FormDialog from '../ui/FormDialog';
import { formatMoney } from '../ui/format';

// POS: opens when an item with sizes or extras is tapped. The price shown here is only a preview; the server prices
// the order from the menu.
export default function ItemOptionsDialog({ item, onCancel, onAdd }) {
  const t = useT();
  const variants = item?.variants ?? [];
  const addOns = item?.addOns ?? [];
  const [variantId, setVariantId] = useState(null);
  const [chosen, setChosen] = useState([]);

  useEffect(() => {
    setVariantId((item?.variants ?? []).find((v) => v.isAvailable)?.id ?? null);
    setChosen([]);
  }, [item]);

  if (!item) return null;

  const variant = variants.find((v) => v.id === variantId) ?? null;
  const extras = addOns.filter((a) => chosen.includes(a.id));
  const price = (variant ? Number(variant.price) : Number(item.price)) + extras.reduce((sum, a) => sum + Number(a.price), 0);
  const noSize = variants.length > 0 && !variant;

  const toggle = (id) => setChosen((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));

  const submit = () => {
    if (noSize) return;
    onAdd({
      id: item.id,
      name: variant ? `${item.name} (${variant.name})` : item.name,
      price,
      variantId: variant?.id ?? null,
      addOnIds: extras.map((a) => a.id),
      options: extras.length > 0 ? extras.map((a) => a.name).join(', ') : null,
    });
  };

  return (
    <FormDialog open title={item.name} submitLabel={t('options.add', { price: formatMoney(price) })} onCancel={onCancel} onSubmit={submit}
      error={noSize ? t('options.allSoldOut') : null}>
      {variants.length > 0 && (
        <div role="group" aria-label={t('options.chooseSize')}>
          <div className="fw-semibold mb-2">{t('options.chooseSize')}</div>
          <div className="pos-chips">
            {variants.map((v) => (
              <button key={v.id} type="button" className="pos-chip" aria-pressed={v.id === variantId} disabled={!v.isAvailable}
                onClick={() => setVariantId(v.id)}>
                {v.name} · {formatMoney(v.price)}{v.isAvailable ? '' : ` (${t('options.soldOut')})`}
              </button>
            ))}
          </div>
        </div>
      )}
      {addOns.length > 0 && (
        <div role="group" aria-label={t('options.chooseExtras')}>
          <div className="fw-semibold mb-2">{t('options.chooseExtras')}</div>
          <div className="pos-chips">
            {addOns.map((a) => (
              <button key={a.id} type="button" className="pos-chip" aria-pressed={chosen.includes(a.id)} disabled={!a.isAvailable}
                onClick={() => toggle(a.id)}>
                {a.name} + {formatMoney(a.price)}{a.isAvailable ? '' : ` (${t('options.soldOut')})`}
              </button>
            ))}
          </div>
        </div>
      )}
    </FormDialog>
  );
}
