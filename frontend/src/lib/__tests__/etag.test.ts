import {
  parseVersionFromETag,
  formatVersionAsIfMatch,
  resolveEtag,
} from '../etag'

// ─── parseVersionFromETag ────────────────────────────────────────────────

describe('parseVersionFromETag', () => {
  it('parses a quoted integer ETag', () => {
    expect(parseVersionFromETag('"5"')).toBe(5)
  })

  it('parses an unquoted integer ETag', () => {
    expect(parseVersionFromETag('5')).toBe(5)
  })

  it('returns null for null input', () => {
    expect(parseVersionFromETag(null)).toBeNull()
  })

  it('returns null for empty string', () => {
    expect(parseVersionFromETag('')).toBeNull()
  })

  it('returns null for non-integer body', () => {
    expect(parseVersionFromETag('"abc"')).toBeNull()
    expect(parseVersionFromETag('"5.5"')).toBeNull()
  })

  it('accepts uppercase weak validators by stripping the W/ prefix (TASK-2305)', () => {
    expect(parseVersionFromETag('W/"5"')).toBe(5)
  })

  it('accepts lowercase weak validators (case-insensitive W/ strip)', () => {
    // Codex Step 0b Q2 verdict: tolerate `w/` because broken intermediaries
    // occasionally lower-case the prefix. Free robustness.
    expect(parseVersionFromETag('w/"5"')).toBe(5)
  })

  it('accepts unquoted weak validators', () => {
    expect(parseVersionFromETag('W/5')).toBe(5)
  })

  it('returns null for malformed weak prefix without body', () => {
    expect(parseVersionFromETag('W/')).toBeNull()
    expect(parseVersionFromETag('W/""')).toBeNull()
  })

  it('returns null for weak validator with non-integer body', () => {
    expect(parseVersionFromETag('W/"abc"')).toBeNull()
  })
})

// ─── formatVersionAsIfMatch ──────────────────────────────────────────────

describe('formatVersionAsIfMatch', () => {
  it('quotes the integer version', () => {
    expect(formatVersionAsIfMatch(5)).toBe('"5"')
  })
})

// ─── resolveEtag (S23 / TASK-2303) ───────────────────────────────────────

describe('resolveEtag', () => {
  it('uses the header when it is a valid quoted integer', () => {
    const result = resolveEtag('"7"', { version: 99 })
    // Header wins — we do NOT cross-reference it against body.version.
    expect(result).toEqual({ etag: '"7"', version: 7 })
  })

  it('falls back to body.version when header is null', () => {
    const result = resolveEtag(null, { version: 12 })
    expect(result).toEqual({ etag: '"12"', version: 12 })
  })

  it('falls back to body.version when header is unparseable', () => {
    const result = resolveEtag('not-a-version', { version: 3 })
    expect(result).toEqual({ etag: '"3"', version: 3 })
  })

  it('returns nulls when both header and body.version are absent', () => {
    expect(resolveEtag(null, {})).toEqual({ etag: null, version: null })
    expect(resolveEtag(null, null)).toEqual({ etag: null, version: null })
    expect(resolveEtag(null, undefined)).toEqual({ etag: null, version: null })
  })

  it('rejects body.version of wrong type (string, boolean, null)', () => {
    expect(resolveEtag(null, { version: '5' as unknown })).toEqual({
      etag: null,
      version: null,
    })
    expect(resolveEtag(null, { version: true as unknown })).toEqual({
      etag: null,
      version: null,
    })
    expect(resolveEtag(null, { version: null as unknown })).toEqual({
      etag: null,
      version: null,
    })
  })

  it('rejects non-finite body.version (NaN, Infinity)', () => {
    expect(resolveEtag(null, { version: NaN })).toEqual({
      etag: null,
      version: null,
    })
    expect(resolveEtag(null, { version: Infinity })).toEqual({
      etag: null,
      version: null,
    })
  })

  it('rejects non-integer body.version', () => {
    expect(resolveEtag(null, { version: 1.5 })).toEqual({
      etag: null,
      version: null,
    })
  })

  it('rejects body.version below 1 (zero or negative)', () => {
    expect(resolveEtag(null, { version: 0 })).toEqual({
      etag: null,
      version: null,
    })
    expect(resolveEtag(null, { version: -1 })).toEqual({
      etag: null,
      version: null,
    })
  })

  it('rejects unsafe-integer body.version', () => {
    expect(resolveEtag(null, { version: Number.MAX_SAFE_INTEGER + 1 })).toEqual(
      { etag: null, version: null },
    )
  })
})
