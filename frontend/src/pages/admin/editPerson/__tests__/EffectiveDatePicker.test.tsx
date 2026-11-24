// S148 / TASK-14850 FU-1 (C1-R1 c): on a self record with a block, the date input points at both the
// self-lock note and the block.
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { EffectiveDatePicker } from '../EffectiveDatePicker'

describe('EffectiveDatePicker — aria-describedby', () => {
  it('joins the self-lock note and the block id when both are present', () => {
    render(
      <EffectiveDatePicker
        value="2026-12-01"
        onChange={() => {}}
        today="2026-11-10"
        selfLockNoteId="pd-self-lock-note"
        blockedReason="Blokeret."
      />,
    )

    expect(screen.getByTestId('pd-effective-from').getAttribute('aria-describedby')).toBe(
      'pd-self-lock-note pd-effective-blocked',
    )
  })
})
