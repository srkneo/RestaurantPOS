import { describe, it, expect } from 'vitest';
import { formatMoney, kitchenStage, minutesSince, newClientId, parseServerDate } from './format';

describe('format helpers', () => {
  it('formats rupees with two decimals', () => {
    expect(formatMoney(6)).toBe('₹6.00');
    expect(formatMoney('7.5')).toBe('₹7.50');
  });

  it('reads server times without a zone as UTC', () => {
    expect(parseServerDate('2026-11-14T07:00:00').toISOString()).toBe('2026-11-14T07:00:00.000Z');
    expect(parseServerDate('2026-11-14T07:00:00Z').toISOString()).toBe('2026-11-14T07:00:00.000Z');
    expect(parseServerDate('2026-11-14T12:30:00+05:30').toISOString()).toBe('2026-11-14T07:00:00.000Z');
    expect(parseServerDate('not a date')).toBeNull();
  });

  it('counts whole minutes since a time', () => {
    const now = Date.parse('2026-11-14T07:12:30Z');
    expect(minutesSince('2026-11-14T07:00:00Z', now)).toBe(12);
    expect(minutesSince('2026-11-14T07:20:00Z', now)).toBe(0);
  });

  it('labels kitchen tickets new, waiting and late', () => {
    expect(kitchenStage(0)).toBe('new');
    expect(kitchenStage(4)).toBe('new');
    expect(kitchenStage(5)).toBe('waiting');
    expect(kitchenStage(9)).toBe('waiting');
    expect(kitchenStage(10)).toBe('late');
  });

  it('makes a new id each time', () => {
    expect(newClientId()).not.toBe(newClientId());
  });
});
