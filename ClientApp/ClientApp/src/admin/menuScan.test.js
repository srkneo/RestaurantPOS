import { describe, it, expect } from 'vitest';
import { toRows, parsePrice, rowProblem, toImportItems, parseCsvLines, csvToRows, shrinkImage, CSV_TEMPLATE } from './menuScan';

describe('menu scan helpers', () => {
  it('turns scanned items into rows; items already on the menu start unticked', () => {
    const rows = toRows([
      { category: 'Starters', name: 'Paneer Tikka', price: 220, isVeg: true, description: null, alreadyOnMenu: false },
      { category: 'Sides', name: 'Fries', price: 90, isVeg: true, description: 'Crispy', alreadyOnMenu: true },
    ]);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ include: true, category: 'Starters', name: 'Paneer Tikka', price: '220', isVeg: true, description: '' });
    expect(rows[1]).toMatchObject({ include: false, alreadyOnMenu: true, description: 'Crispy' });
    expect(rows[0].key).not.toBe(rows[1].key);
  });

  it('reads prices written the Indian way', () => {
    expect(parsePrice('₹ 1,250.50')).toBe(1250.5);
    expect(parsePrice('Rs. 99')).toBe(99);
    expect(parsePrice('')).toBeNaN();
    expect(parsePrice('abc')).toBeNaN();
  });

  it('finds the problem in a ticked row only', () => {
    expect(rowProblem({ include: true, name: ' ', price: '10' })).toBe('name');
    expect(rowProblem({ include: true, name: 'Tea', price: 'x' })).toBe('price');
    expect(rowProblem({ include: true, name: 'Tea', price: '-1' })).toBe('price');
    expect(rowProblem({ include: false, name: '', price: '' })).toBeNull();
    expect(rowProblem({ include: true, name: 'Tea', price: '15' })).toBeNull();
  });

  it('sends only ticked rows, tidied', () => {
    const items = toImportItems([
      { include: true, category: ' Starters ', name: ' Paneer Tikka ', price: '220.456', isVeg: true, description: ' ' },
      { include: false, category: 'Sides', name: 'Fries', price: '90', isVeg: true, description: '' },
      { include: true, category: '', name: 'Chicken Curry', price: '₹260', isVeg: false, description: 'Spicy' },
    ]);
    expect(items).toEqual([
      { category: 'Starters', name: 'Paneer Tikka', price: 220.46, isVeg: true, description: null },
      { category: '', name: 'Chicken Curry', price: 260, isVeg: false, description: 'Spicy' },
    ]);
  });

  it('reads CSV with quotes, commas inside quotes, CRLF and a BOM', () => {
    const lines = parseCsvLines('﻿a,"b, c","say ""hi"""\r\n\r\n1,2,3');
    expect(lines).toEqual([['a', 'b, c', 'say "hi"'], ['1', '2', '3']]);
  });

  it('turns a CSV with a header into rows, columns in any order', () => {
    const rows = csvToRows('Name,Price,Category,Veg\nPaneer Tikka,"1,220",Starters,yes\nChicken Curry,260,Mains,Non-Veg\n');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({ include: true, name: 'Paneer Tikka', price: '1220', category: 'Starters', isVeg: true, description: '' });
    expect(rows[1]).toMatchObject({ name: 'Chicken Curry', price: '260', category: 'Mains', isVeg: false });
  });

  it('reads a CSV without a header in the template order, and the template itself', () => {
    expect(csvToRows('Drinks,Masala Chai,20,yes,Hot')[0]).toMatchObject({ category: 'Drinks', name: 'Masala Chai', price: '20', isVeg: true, description: 'Hot' });
    const template = csvToRows(CSV_TEMPLATE);
    expect(template.map((r) => r.name)).toEqual(['Paneer Tikka', 'Chicken Curry']);
    expect(template[1].isVeg).toBe(false);
  });

  it('keeps the original photo when the browser cannot shrink it', async () => {
    const file = new File(['not really a photo'], 'menu.jpg', { type: 'image/jpeg' });
    expect(await shrinkImage(file)).toBe(file);
  });
});
