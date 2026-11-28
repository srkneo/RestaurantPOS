import { describe, it, expect } from 'vitest';
import { describeDetails, typeName } from './activityDetails';

describe('activity details', () => {
  it('shows a change as before → after', () => {
    expect(describeDetails('{"changes":{"Price":[18,20]}}')).toBe('Price: 18 → 20');
    expect(describeDetails('{"changes":{"Price":[18,20],"Name":["Tea","Masala tea"]}}')).toBe('Price: 18 → 20; Name: Tea → Masala tea');
  });

  it('shows only the new value for a created row and the old value for a deleted row', () => {
    expect(describeDetails('{"changes":{"Name":[null,"Snacks"]}}')).toBe('Name: Snacks');
    expect(describeDetails('{"changes":{"Name":["Snacks",null]}}')).toBe('Name: Snacks');
  });

  it('shows empty texts and objects readably', () => {
    expect(describeDetails('{"changes":{"Note":["",null]}}')).toBe('Note: –');
    expect(describeDetails('{"changes":{"Options":[null,{"size":"L"}]}}')).toBe('Options: {"size":"L"}');
  });

  it('shows support and approval texts as they are', () => {
    expect(describeDetails('{"support":"Support session started by mahesh@vyom.in"}')).toBe('Support session started by mahesh@vyom.in');
    expect(describeDetails('{"approval":"Discount 20% approved by Ravi"}')).toBe('Discount 20% approved by Ravi');
  });

  it('shows other objects as key: value', () => {
    expect(describeDetails('{"reason":"Customer left","table":4}')).toBe('reason: Customer left; table: 4');
  });

  it('shows text that is not JSON as it is, and nothing for no details', () => {
    expect(describeDetails('plain old text')).toBe('plain old text');
    expect(describeDetails('{broken')).toBe('{broken');
    expect(describeDetails(null)).toBe('');
    expect(describeDetails(undefined)).toBe('');
    expect(describeDetails('')).toBe('');
    expect(describeDetails('42')).toBe('42');
  });

  it('turns type names into words', () => {
    expect(typeName('MenuItem')).toBe('Menu item');
    expect(typeName('KitchenOrder')).toBe('Kitchen order');
    expect(typeName('Support')).toBe('Support');
    expect(typeName('')).toBe('');
    expect(typeName(null)).toBe('');
  });
});
