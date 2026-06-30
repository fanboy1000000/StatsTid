// SPRINT-109 / TASK-10903 (Enhedsspor Phase 3b-2b) — the cross-unit "Ret" leader
// picker. A cross-unit-exception member (their PRIMARY leader sits OUTSIDE their
// own unit) is corrected by reassigning the edge to a leader of THEIR OWN unit.
// When that unit has EXACTLY ONE leader, "Ret" is one-click (the host does it
// directly); when it has SEVERAL peer (sideordnede) leaders, "Ret" opens THIS
// minimal picker — pre-filtered to the unit's OWN leaders, so the actor never
// silently lands on an arbitrary "first" pick. The chosen leader becomes the
// member's PRIMARY reporting edge (POST /api/admin/reporting-lines; the host owns
// the create-vs-supersede etag on the row's nullable primaryReportingLineVersion).
//
// Presentational: the host (StrukturPanel) owns the action state, fires the
// reporting-line POST, and surfaces the real errors (412 stale / 409 / 422) inline
// via the `error` prop. Square corners, hairline borders, IBM Plex Sans — tokens.

import { useState } from 'react'
import { Button } from '../../../components/ui'
import styles from './RetLeaderPicker.module.css'

export interface RetLeaderOption {
  id: string
  name: string
}

interface RetLeaderPickerProps {
  /** The cross-unit-exception member being corrected (the dialog copy). */
  personName: string
  /** The member's own unit name (null = Organisation-homed). */
  unitName: string | null
  /** The unit's OWN leaders — the ONLY valid targets (never an arbitrary pick). */
  leaders: RetLeaderOption[]
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (managerId: string) => void
}

export function RetLeaderPicker({
  personName,
  unitName,
  leaders,
  busy,
  error,
  onClose,
  onSubmit,
}: RetLeaderPickerProps) {
  const [managerId, setManagerId] = useState<string>(leaders[0]?.id ?? '')

  return (
    <div className={styles.scrim} role="presentation" onClick={onClose} data-testid="ret-picker-scrim">
      <div
        className={styles.dialog}
        role="dialog"
        aria-modal="true"
        aria-label="Ret leder"
        onClick={(e) => e.stopPropagation()}
      >
        <div className={styles.dialogHead}>Ret leder</div>
        <div className={styles.dialogBody}>
          <p className={styles.dialogText}>
            Vælg en leder i {unitName ? <strong>{unitName}</strong> : 'enheden'} som{' '}
            <strong>{personName}</strong> skal referere til.
          </p>
          <label className={styles.dialogLabel} htmlFor="ret-leader-select">
            Nærmeste leder
          </label>
          <select
            id="ret-leader-select"
            data-testid="ret-leader-select"
            className={styles.dialogSelect}
            value={managerId}
            onChange={(e) => setManagerId(e.target.value)}
          >
            {leaders.map((l) => (
              <option key={l.id} value={l.id} data-testid={`ret-leader-option-${l.id}`}>
                {l.name}
              </option>
            ))}
          </select>
          {error && (
            <div className={styles.error} role="alert" data-testid="ret-picker-error">
              {error}
            </div>
          )}
          <div className={styles.dialogFooter}>
            <Button type="button" variant="ghost" size="md" onClick={onClose}>
              Annuller
            </Button>
            <Button
              type="button"
              variant="primary"
              size="md"
              data-testid="ret-leader-submit"
              disabled={busy || !managerId}
              onClick={() => managerId && onSubmit(managerId)}
            >
              {busy ? 'Retter…' : 'Ret'}
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
