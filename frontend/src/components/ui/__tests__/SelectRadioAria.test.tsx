// The optional invalid / describedBy pass-through on Select and Radio (S148 TASK-14834 FU-3).
// Red before the change: neither component accepts the props, so no attribute is rendered.
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Select } from '../Select'
import { Radio } from '../Radio'

vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })

describe('Select and Radio aria pass-through', () => {
  it('Select puts aria-invalid and aria-describedby on its trigger only when given', () => {
    const { rerender } = render(<Select id="s" options={[{ value: 'a', label: 'A' }]} value="" onValueChange={() => {}} />)
    expect(screen.getByRole('combobox')).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('combobox')).not.toHaveAttribute('aria-describedby')
    rerender(<Select id="s" options={[{ value: 'a', label: 'A' }]} value="" onValueChange={() => {}} invalid describedBy="why" />)
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-describedby', 'why')
  })

  it('Radio puts aria-invalid and aria-describedby on its input only when given', () => {
    const { rerender } = render(<Radio id="r" name="g" value="a" label="A" checked={false} onChange={() => {}} />)
    expect(screen.getByRole('radio')).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('radio')).not.toHaveAttribute('aria-describedby')
    rerender(<Radio id="r" name="g" value="a" label="A" checked={false} onChange={() => {}} invalid describedBy="why" />)
    expect(screen.getByRole('radio')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('radio')).toHaveAttribute('aria-describedby', 'why')
  })
})
