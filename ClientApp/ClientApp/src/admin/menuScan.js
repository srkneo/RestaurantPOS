// Helpers for the menu scan page: review rows, CSV import and smaller photos before upload.

export const MAX_PHOTOS = 4;

export const CSV_TEMPLATE = 'category,name,price,veg,description\nStarters,Paneer Tikka,220,yes,Grilled cottage cheese\nMains,Chicken Curry,260,no,\n';

let nextKey = 1;
const newKey = () => `row-${nextKey++}`;

// Rows from a scan: items already on the menu start unticked.
export function toRows(items) {
  return (items ?? []).map((i) => ({
    key: newKey(),
    include: !i.alreadyOnMenu,
    category: i.category ?? '',
    name: i.name ?? '',
    price: i.price === undefined || i.price === null ? '' : String(i.price),
    isVeg: i.isVeg !== false,
    description: i.description ?? '',
    alreadyOnMenu: !!i.alreadyOnMenu,
  }));
}

export function emptyRow() {
  return { key: newKey(), include: true, category: '', name: '', price: '', isVeg: true, description: '', alreadyOnMenu: false };
}

// '₹ 1,250.50' -> 1250.5; empty or not a number -> NaN.
export function parsePrice(value) {
  const text = String(value ?? '').replace(/[₹,\s]/g, '').replace(/^rs\.?/i, '');
  if (text === '') return NaN;
  const n = Number(text);
  return Number.isFinite(n) ? n : NaN;
}

export function rowProblem(row) {
  if (!row.include) return null;
  if (row.name.trim() === '') return 'name';
  const price = parsePrice(row.price);
  if (Number.isNaN(price) || price < 0 || price > 100000) return 'price';
  return null;
}

// Body rows for POST /api/admin/menu/import (only ticked rows).
export function toImportItems(rows) {
  return rows.filter((r) => r.include).map((r) => ({
    category: r.category.trim(),
    name: r.name.trim(),
    price: Math.round(parsePrice(r.price) * 100) / 100,
    isVeg: r.isVeg,
    description: r.description.trim() === '' ? null : r.description.trim(),
  }));
}

// Simple CSV reader: commas, quotes ("a, b" and "" inside quotes), CRLF, Excel's BOM.
export function parseCsvLines(text) {
  const rows = [];
  let row = [];
  let field = '';
  let quoted = false;
  const src = String(text ?? '').replace(/^﻿/, '');
  for (let i = 0; i < src.length; i++) {
    const c = src[i];
    if (quoted) {
      if (c === '"' && src[i + 1] === '"') { field += '"'; i++; }
      else if (c === '"') quoted = false;
      else field += c;
    } else if (c === '"') quoted = true;
    else if (c === ',') { row.push(field); field = ''; }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && src[i + 1] === '\n') i++;
      row.push(field); field = '';
      rows.push(row); row = [];
    } else field += c;
  }
  if (field !== '' || row.length > 0) { row.push(field); rows.push(row); }
  return rows.filter((r) => r.some((f) => f.trim() !== ''));
}

const VEG_NO = ['no', 'n', 'false', '0', 'non-veg', 'nonveg', 'non veg', 'nv'];

// CSV with a header row (category, name, price, veg, description in any order),
// or without one in that order. Every row starts ticked.
export function csvToRows(text) {
  const lines = parseCsvLines(text);
  if (lines.length === 0) return [];
  const header = lines[0].map((h) => h.trim().toLowerCase());
  const hasHeader = header.includes('name');
  const col = (name, fallback) => (hasHeader ? header.indexOf(name) : fallback);
  const at = { category: col('category', 0), name: col('name', 1), price: col('price', 2), veg: col('veg', 3), description: col('description', 4) };
  const get = (line, i) => (i >= 0 && i < line.length ? line[i].trim() : '');
  return (hasHeader ? lines.slice(1) : lines).map((line) => ({
    key: newKey(),
    include: true,
    category: get(line, at.category),
    name: get(line, at.name),
    price: get(line, at.price).replace(/[₹,\s]/g, ''),
    isVeg: !VEG_NO.includes(get(line, at.veg).toLowerCase()),
    description: get(line, at.description),
    alreadyOnMenu: false,
  }));
}

// Phone photos are large; make the longest side at most maxSide pixels as JPEG.
// Any problem (old browser, unreadable file): send the original.
export async function shrinkImage(file, maxSide = 2000, quality = 0.85) {
  try {
    if (typeof createImageBitmap !== 'function' || typeof document === 'undefined') return file;
    const bitmap = await createImageBitmap(file);
    const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
    if (scale === 1 && file.size <= 1.5 * 1024 * 1024) { bitmap.close?.(); return file; }
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);
    const ctx = canvas.getContext('2d');
    if (!ctx) return file;
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close?.();
    const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    if (!blob) return file;
    return new File([blob], (file.name || 'menu').replace(/\.[^.]+$/, '') + '.jpg', { type: 'image/jpeg' });
  } catch {
    return file;
  }
}
