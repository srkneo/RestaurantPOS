import { describe, it, expect } from 'vitest';
import { readErrors, errorId } from './forms';

const answer = (status, body, headerId) => ({
  status,
  ok: status < 400,
  headers: { get: (name) => (name.toLowerCase() === 'x-request-id' ? headerId ?? null : null) },
  json: async () => {
    if (body === undefined) throw new Error('not json');
    return body;
  },
});

describe('error ID on server errors', () => {
  it('adds the ID to server errors only', async () => {
    const failed = await readErrors(answer(500, { title: 'An error occurred while processing your request.', requestId: 'abc123' }));
    expect(failed.general).toBe('An error occurred while processing your request. [ID: abc123]');

    const conflict = await readErrors(answer(409, { message: 'Table is in use', requestId: 'abc123' }, 'abc123'));
    expect(conflict.general).toBe('Table is in use');
  });

  it('uses the response header when the body has no ID', async () => {
    const failed = await readErrors(answer(503, { message: 'Try again soon' }, 'hdr-9'));
    expect(failed.general).toBe('Try again soon [ID: hdr-9]');
  });

  it('keeps field errors unchanged', async () => {
    const res = await readErrors(answer(400, { errors: { Name: ['Name is required'] }, requestId: 'x1' }, 'x1'));
    expect(res).toEqual({ name: 'Name is required' });
  });

  it('shows the ID even when the answer is not JSON', async () => {
    const res = await readErrors(answer(500, undefined, 'raw-1'));
    expect(res.general).toBe('[ID: raw-1]');
  });

  it('changes nothing when there is no ID', async () => {
    expect((await readErrors(answer(500, { title: 'Oops' }))).general).toBe('Oops');
    expect((await readErrors(answer(500, undefined))).general).toBe('');
  });

  it('ignores IDs that do not look like ours', () => {
    expect(errorId(answer(500, {}, '<script>'), { requestId: 'a b' })).toBeNull();
    expect(errorId(answer(500, {}, 'x'.repeat(65)), null)).toBeNull();
    expect(errorId({ status: 500 }, { requestId: 'ok-1' })).toBe('ok-1');
    expect(errorId(undefined, null)).toBeNull();
  });
});
