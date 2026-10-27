import { describe, expect, it } from 'vitest';
import { describeUpload } from './packages';
import type { UploadResponse } from './types';

const base: UploadResponse = { status: 'Applied', batchId: 'b', inserted: 0, updated: 0, deleted: 0, notes: [], error: null };

describe('the upload answer', () => {
  it('says what went in', () => {
    expect(describeUpload({ ...base, inserted: 1380, updated: 30, deleted: 2 }, 'en-US')).toEqual({
      failed: false,
      message: 'The package went in: 1,380 rows added, 30 changed, 2 deleted.',
    });
    expect(describeUpload({ ...base, inserted: 4 }, 'en-US').message).toBe('The package went in: 4 rows added.');
    expect(describeUpload(base).message).toBe('The package went in; it changed nothing.');
  });

  it('says when it was sent before', () => {
    expect(describeUpload({ ...base, status: 'Duplicate' })).toEqual({ failed: false, message: 'This package was sent before; nothing changed.' });
  });

  it("gives the server's reason when it failed", () => {
    expect(describeUpload({ ...base, status: 'Failed', error: 'Signed with another key.' })).toEqual({ failed: true, message: 'Signed with another key.' });
    expect(describeUpload({ ...base, status: 'Failed', error: null }).message).toBe('The server refused the package.');
  });
});
