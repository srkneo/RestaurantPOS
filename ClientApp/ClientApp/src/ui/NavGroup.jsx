import React, { useEffect, useRef, useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import Icon from './Icon';

// One heading of the main menu that opens a short list of pages. With only one page for this user it is a plain link,
// and with none it is not shown. Closes on a click outside, on Escape and when a page is chosen.
export default function NavGroup({ label, items, icon }) {
  const { pathname } = useLocation();
  const [open, setOpen] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  if (items.length === 0) return null;
  if (items.length === 1) return <NavLink to={items[0].to}>{icon && <Icon name={icon} className="me-1" />}{items[0].label}</NavLink>;

  const active = items.some((i) => pathname === i.to || pathname.startsWith(`${i.to}/`));
  return (
    <div className="pos-nav-group" ref={ref}>
      <button type="button" className={`pos-nav-toggle${active ? ' active' : ''}`} aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        {icon && <Icon name={icon} />}{label} <Icon name="chevron-down" className="pos-nav-chevron" />
      </button>
      {open && (
        <div className="pos-nav-menu">
          {items.map((i) => <NavLink key={i.to} to={i.to} onClick={() => setOpen(false)}>{i.label}</NavLink>)}
        </div>
      )}
    </div>
  );
}
