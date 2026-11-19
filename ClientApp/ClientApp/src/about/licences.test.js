import { describe, it, expect } from 'vitest';
import pkg from '../../package.json';
import { FREE_LICENCES, LICENCES } from './licences';

describe('open-source licences list', () => {
  it('names every library the screen uses', () => {
    const listed = LICENCES.map((l) => l.name);
    for (const dep of Object.keys(pkg.dependencies)) expect(listed).toContain(dep);
  });

  it('has only free licences', () => {
    for (const l of LICENCES) expect(FREE_LICENCES).toContain(l.licence);
  });
});
