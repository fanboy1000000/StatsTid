// SPRINT-140 / TASK-14006 (refinement B4) — the shared tile the HR follow-up
// landing page (`/admin/opfoelgning`) renders once per process: a title, an
// open count, "oldest: …", and an optional second count (e.g. an
// employee-late / approver-late split) — built on the existing `Card`
// container rather than a new one (`docs/FRONTEND.md` component-reuse
// convention).
//
// COLOUR IS GATED BY `decisionReady`, NOT BY THE DATA. S139's HR follow-up
// register (owner ruling OQ-2 (b)) only lets four of the ten processes carry
// an aging colour — the §21 transfer agreement, the leaver's final month,
// past-deadline, and approved-not-exported — because those four are the only
// ones with a RULED "by when". Colouring any of the other six would invent an
// urgency the organisation has never agreed to, so `decisionReady` (a static
// per-tile fact the caller passes, never derived from the response) is the
// single gate every colour cue in this file checks. Passing `decisionReady`
// for a non-ready process is a caller bug this component cannot catch —
// callers are the six/four split in the landing page, reviewed once.
//
// `stateMessage` exists for exactly one caller: the §21 tile outside its
// 1 Nov-31 Dec window. An empty list out of season ("nothing to transfer")
// and an empty list in season ("nobody needs to yet") mean different things,
// so the window-closed state REPLACES the count row with prose ("Åbner 1.
// november …") rather than rendering a misleading "0".
import { type ReactNode } from 'react'
import { Card } from './Card'
import styles from './ProcessTile.module.css'

export interface ProcessTileProps {
  title: string
  /** Root test id; child parts expose `${testId}-count` / `-oldest` / `-secondary` / `-state` / `-footnote`. */
  testId: string
  /** S139 OQ-2 (b) — whether this process may show an aging colour at all. */
  decisionReady: boolean
  onOpen: () => void
  /** Replaces the count/oldest display entirely (the §21 window-closed state). */
  stateMessage?: string
  /** The open-item count. Omit together with `oldestLabel`/`secondary` when `stateMessage` is set. */
  count?: number
  countLabel?: string
  /** Pre-formatted "oldest" text ("8 dage", "afregnet 12.05.2025", …).
      `null` omits the row entirely — some processes have no age dimension
      (e.g. an orphan employee has no "since when" timestamp at all). */
  oldestLabel?: string | null
  /** A second highlighted count — e.g. the approver-late split on the
      past-deadline tile, or the expired-delegation count beside the
      orphan count on the uncovered-approvers tile. */
  secondary?: { label: string; value: number }
  /** Small print under the counts (the rolling lookback floor, the
      eventual-consistency lag note, etc.). */
  footnote?: ReactNode
}

export function ProcessTile({
  title,
  testId,
  decisionReady,
  onOpen,
  stateMessage,
  count,
  countLabel = 'åbne sager',
  oldestLabel,
  secondary,
  footnote,
}: ProcessTileProps) {
  const showingState = stateMessage !== undefined
  const showColour = decisionReady && !showingState && (count ?? 0) > 0

  return (
    <div
      className={`${styles.wrapper} ${showColour ? styles.wrapperReady : ''}`}
      data-testid={`${testId}-tile`}
      data-decision-ready={decisionReady ? 'true' : 'false'}
      data-coloured={showColour ? 'true' : 'false'}
    >
      <Card>
        <button type="button" className={styles.tile} data-testid={testId} onClick={onOpen}>
          <div className={styles.title}>{title}</div>
          {showingState ? (
            <div className={styles.stateMessage} data-testid={`${testId}-state`}>
              {stateMessage}
            </div>
          ) : (
            <>
              <div className={styles.countRow}>
                <span className={`${styles.count} ${showColour ? styles.countReady : ''}`} data-testid={`${testId}-count`}>
                  {count}
                </span>
                <span className={styles.countLabel}>{countLabel}</span>
              </div>
              {oldestLabel !== null && oldestLabel !== undefined && (
                <div className={styles.oldest} data-testid={`${testId}-oldest`}>
                  Ældste: {oldestLabel}
                </div>
              )}
              {secondary && (
                <div
                  className={`${styles.secondary} ${showColour ? styles.secondaryReady : ''}`}
                  data-testid={`${testId}-secondary`}
                >
                  {secondary.label}: {secondary.value}
                </div>
              )}
            </>
          )}
          {footnote && (
            <div className={styles.footnote} data-testid={`${testId}-footnote`}>
              {footnote}
            </div>
          )}
        </button>
      </Card>
    </div>
  )
}
