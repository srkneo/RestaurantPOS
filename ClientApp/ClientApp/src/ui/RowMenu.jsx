import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import Icon from './Icon';

// "More" button for a table row: the less used actions open in a small list under it. The list is placed on the
// screen (fixed) so a scrolling table never cuts it off; it follows the button when the page scrolls and opens upwards
// when there is no room below. Closes on a click outside, Escape or after a choice.
// The list stays in the page while closed (hidden by CSS), so its buttons are only usable while open.
export default function RowMenu({ label, children }) {
  const [open, setOpen] = useState(false);
  const [place, setPlace] = useState(null);
  const ref = useRef(null);
  const button = useRef(null);
  const menu = useRef(null);

  const measure = () => {
    if (!button.current) return;
    const r = button.current.getBoundingClientRect();
    const h = menu.current ? menu.current.offsetHeight : 0;
    const right = Math.max(8, window.innerWidth - r.right);
    const below = r.bottom + 4 + h <= window.innerHeight - 8 || r.top - 4 - h < 8;
    setPlace(below ? { top: r.bottom + 4, right } : { top: r.top - 4 - h, right });
  };

  useLayoutEffect(() => { if (open) measure(); }, [open]);

  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    window.addEventListener('scroll', measure, true);
    window.addEventListener('resize', measure);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('scroll', measure, true);
      window.removeEventListener('resize', measure);
    };
  }, [open]);

  return (
    <div className="pos-row-menu" ref={ref}>
      <button ref={button} type="button" className="btn btn-outline-secondary btn-sm px-3" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        {label} <Icon name="chevron-down" className="pos-nav-chevron" />
      </button>
      <div ref={menu} className={`pos-more-menu${open ? ' open' : ''}`} style={place ? { top: place.top, right: place.right } : undefined}
        onClick={() => setOpen(false)}>
        {children}
      </div>
    </div>
  );
}
