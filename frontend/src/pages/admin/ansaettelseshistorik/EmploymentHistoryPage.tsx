// S141 / TASK-14109 (refinement C2) — the employment history screen.
//
// WHAT THIS ANSWERS, IN PLAIN TERMS. HR asks "what has changed for this
// employee, and when did it take effect?" The audit log cannot answer that:
// it has no subject filter, and it orders by when a change was RECORDED
// rather than when it took EFFECT — a correction to March typed in
// September files under September, and the question means March. This
// screen reads `GET /api/hr/employees/{employeeId}/history`
// (TASK-14113, wave 2), which already answers by EFFECTIVE date.
//
// TWO PARALLEL TRACKS, INTERLEAVED FOR READING ONLY. The server returns the
// profile track (job title, part-time fraction, employment category) and
// the agreement track SEPARATELY — merging them server-side would invent
// combined periods no stored record corresponds to. This screen sorts both
// lists together into ONE chronological table so a reader is not forced to
// cross-reference two separate lists by eye, but every row keeps EXACTLY
// its own server-given `effectiveFrom`/`effectiveTo` — no boundary is
// invented here either (`combineTracks` below only reorders).
//
// SCHEDULED IS THE INTERESTING STATUS. Since S141 wave 1, a future-dated
// profile or agreement-code change is legal (ADR-040 D8), and the owner's
// standing requirement this sprint is that a scheduled change must be
// visible wherever a profile is read — a history view would be the most
// surprising possible place to hide one. A SCHEDULED row therefore gets
// its own warning-coloured status badge, a distinct row tint, AND wording
// that says "not yet in force" rather than merely a different colour — a
// colour-blind reader or a screenshot in black-and-white must still be able
// to tell it apart from something that already happened.
//
// A MALFORMED RANGE IS A REFUSAL, NOT AN EMPTY RESULT. The endpoint answers
// 422 when `from >= to` (an inverted or empty window), deliberately, so a
// mistyped filter cannot read as "this employee has never changed". This
// screen renders that 422 as its own warning Alert — never as the empty
// state — and does not attempt to construct any list from it.
//
// AN UNKNOWN EMPLOYEE ANSWERS 403, LIKE AN OUT-OF-SCOPE ONE, ON PURPOSE. The
// backend cannot tell "no such employee" apart from "not yours to see"
// without becoming an existence oracle for an out-of-scope caller — see
// `EmploymentHistoryEndpoints.cs`'s own note. This screen's 403 message
// preserves that ambiguity rather than resolving it one way or the other.
import { useCallback, useMemo, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { Alert, Badge, Button, Spinner, Table } from '../../../components/ui'
import { useSearch } from '../../../hooks/useSearch'
import {
  useEmploymentHistory,
  type AgreementCodeHistoryInterval,
  type EmploymentProfileHistoryInterval,
  type HistoryIntervalStatus,
} from '../../../hooks/useEmploymentHistory'
import styles from './EmploymentHistoryPage.module.css'

const STATUS_LABEL: Record<HistoryIntervalStatus, string> = {
  PAST: 'Tidligere',
  CURRENT: 'Gældende nu',
  SCHEDULED: 'Planlagt – endnu ikke i kraft',
}

const STATUS_VARIANT: Record<HistoryIntervalStatus, 'default' | 'info' | 'warning'> = {
  PAST: 'default',
  CURRENT: 'info',
  SCHEDULED: 'warning',
}

const TRACK_LABEL = {
  PROFILE: 'Profil',
  AGREEMENT: 'Overenskomst',
} as const

/** `EmploymentHistoryFields` on the wire (`EmploymentHistoryResponses.cs`) — kept in sync by hand
    since the field-name STRINGS themselves are not part of the generated type (only their
    container, `string[]`, is). A mismatch here would show as a changed field falling back to its
    raw wire name instead of Danish — visible in review, not a silent wrong answer. */
const FIELD_LABEL: Record<string, string> = {
  partTimeFraction: 'Deltidsbrøk',
  position: 'Stilling',
  employmentCategory: 'Ansættelseskategori',
  agreementCode: 'Overenskomstkode',
}

function isKnownStatus(value: string): value is HistoryIntervalStatus {
  return value === 'PAST' || value === 'CURRENT' || value === 'SCHEDULED'
}

function formatDaDate(iso: string): string {
  try {
    return new Date(`${iso}T00:00:00Z`).toLocaleDateString('da-DK', { timeZone: 'UTC' })
  } catch {
    return iso
  }
}

function formatPeriod(from: string, to: string | null): string {
  // `to` is END-EXCLUSIVE (ADR-018 D9): the interval's last covered day is the day BEFORE `to`.
  // Rendered as an open-ended dash rather than an inclusive "til <to>" so nobody reads the
  // boundary as one day later than it is.
  return to ? `${formatDaDate(from)} – ${formatDaDate(to)}` : `${formatDaDate(from)} –`
}

function formatPercent(fraction: number): string {
  return `${Math.round(fraction * 100)} %`
}

/** Parses the endpoint's `{ error, reason }` refusal body (`Results.UnprocessableEntity` /
    `Results.Json(..., 403)`); falls back to `null` for a non-JSON or differently-shaped body so the
    caller can supply its own static Danish text rather than showing a raw parse failure. */
function parseReason(raw: string): string | null {
  try {
    const body = JSON.parse(raw) as { error?: string; reason?: string }
    return body.reason ?? body.error ?? null
  } catch {
    return null
  }
}

type CombinedRow =
  | ({ track: 'PROFILE'; trackIndex: number } & EmploymentProfileHistoryInterval)
  | ({ track: 'AGREEMENT'; trackIndex: number } & AgreementCodeHistoryInterval)

/** Interleaves the two tracks into ONE chronological list for display. This is display order
    ONLY — every row keeps its own server-given interval untouched; nothing here computes or
    infers a boundary neither track's own rows carry. `trackIndex` is the row's position in ITS
    OWN track's original (already effective-date-ordered) list — kept alongside the interleaved
    position so a row's identity does not shift if the other track gains or loses rows. */
function combineTracks(
  profile: readonly EmploymentProfileHistoryInterval[],
  agreement: readonly AgreementCodeHistoryInterval[],
): CombinedRow[] {
  const rows: CombinedRow[] = [
    ...profile.map((r, trackIndex) => ({ track: 'PROFILE' as const, trackIndex, ...r })),
    ...agreement.map((r, trackIndex) => ({ track: 'AGREEMENT' as const, trackIndex, ...r })),
  ]
  // `effectiveFrom` is a plain `YYYY-MM-DD` date string, so lexical order IS chronological order.
  // `Array.prototype.sort` is stable (ES2019+), so same-date rows keep profile-before-agreement
  // insertion order rather than an arbitrary one.
  rows.sort((a, b) => (a.effectiveFrom < b.effectiveFrom ? -1 : a.effectiveFrom > b.effectiveFrom ? 1 : 0))
  return rows
}

function describeChanged(row: CombinedRow): string {
  if (row.isInitial) return 'Første registrering'
  if (row.changedFields.length === 0) return 'Ingen registreret feltændring ved denne dato'
  return row.changedFields.map((f) => FIELD_LABEL[f] ?? f).join(', ')
}

function describeDetail(row: CombinedRow): string {
  if (row.track === 'PROFILE') {
    return [
      `Stilling: ${row.position ?? '—'}`,
      `Deltid: ${formatPercent(row.partTimeFraction)}`,
      `Kategori: ${row.employmentCategory}`,
    ].join(' · ')
  }
  return `Overenskomst: ${row.agreementCode}`
}

export function EmploymentHistoryPage() {
  const { employeeId } = useParams<{ employeeId?: string }>()
  const location = useLocation()
  const navigate = useNavigate()
  const displayName = (location.state as { displayName?: string } | null)?.displayName

  const handleSelect = useCallback(
    (id: string, name: string) => {
      navigate(`/admin/ansaettelseshistorik/${id}`, { state: { displayName: name } })
    },
    [navigate],
  )

  return (
    <div className={styles.root}>
      <h1 className={styles.pageTitle}>Ansættelseshistorik</h1>
      <p className={styles.pageIntro}>
        Se hvornår en medarbejders stilling, deltid, ansættelseskategori eller overenskomst er
        ændret — og hvornår ændringen trådte, eller træder, i kraft. En planlagt ændring, der
        endnu ikke er trådt i kraft, vises tydeligt markeret som planlagt, aldrig som en
        gennemført ændring.
      </p>

      {!employeeId && <EmployeeSearchPanel onSelect={handleSelect} />}

      {employeeId && (
        <>
          <div className={styles.employeeBar}>
            <div>
              <span className={styles.employeeLabel}>Medarbejder</span>
              <div className={styles.employeeName} data-testid="history-employee-name">
                {displayName ?? `Medarbejder ${employeeId}`}
              </div>
            </div>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate('/admin/ansaettelseshistorik')}
            >
              Skift medarbejder
            </Button>
          </div>
          <HistoryView employeeId={employeeId} />
        </>
      )}
    </div>
  )
}

function EmployeeSearchPanel({ onSelect }: { onSelect: (id: string, displayName: string) => void }) {
  const { query, setQuery, results, loading, error } = useSearch()
  const people = results.people
  const trimmed = query.trim()

  return (
    <div className={styles.searchPanel} data-testid="history-search-panel">
      <label className={styles.filterLabel} htmlFor="history-employee-search">
        Find medarbejder
      </label>
      <input
        id="history-employee-search"
        className={styles.filterInput}
        type="text"
        placeholder="Søg på navn…"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        autoFocus
      />

      {loading && (
        <div className={styles.spinnerRow}>
          <Spinner size="sm" />
        </div>
      )}
      {!loading && error && <Alert variant="error">Kunne ikke søge efter medarbejdere. {error}</Alert>}
      {!loading && !error && trimmed !== '' && people.length === 0 && (
        <div className={styles.emptyState} data-testid="history-search-empty">
          Ingen medarbejdere matcher &quot;{trimmed}&quot;.
        </div>
      )}
      {people.length > 0 && (
        <ul className={styles.resultList} data-testid="history-search-results">
          {people.map((p) => (
            <li key={p.userId}>
              <button
                type="button"
                className={styles.resultRow}
                data-testid={`history-search-result-${p.userId}`}
                onClick={() => onSelect(p.userId, p.displayName)}
              >
                <span className={styles.resultName}>{p.displayName}</span>
                {p.position && <span className={styles.resultMeta}>{p.position}</span>}
                <span className={styles.resultPath}>{p.path.join(' › ')}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {trimmed === '' && (
        <p className={styles.hint}>Skriv et navn for at søge blandt medarbejdere i din organisation.</p>
      )}
    </div>
  )
}

function HistoryView({ employeeId }: { employeeId: string }) {
  const [fromDraft, setFromDraft] = useState('')
  const [toDraft, setToDraft] = useState('')
  const [fromApplied, setFromApplied] = useState<string | undefined>(undefined)
  const [toApplied, setToApplied] = useState<string | undefined>(undefined)

  const { data, loading, error, errorStatus } = useEmploymentHistory(employeeId, fromApplied, toApplied)

  const applyFilter = () => {
    setFromApplied(fromDraft || undefined)
    setToApplied(toDraft || undefined)
  }
  const resetFilter = () => {
    setFromDraft('')
    setToDraft('')
    setFromApplied(undefined)
    setToApplied(undefined)
  }

  // A proactive HINT, not a client-side re-implementation of the server's rule: the request still
  // goes out if the reader clicks through anyway, and the 422 that comes back is what is shown —
  // this is only here so most readers never need to see that refusal at all.
  const rangeLooksInverted = fromDraft !== '' && toDraft !== '' && fromDraft >= toDraft
  const isWindowed = (fromApplied ?? toApplied) !== undefined

  const combined = useMemo(() => {
    if (!data) return []
    return combineTracks(data.profileHistory, data.agreementCodeHistory)
  }, [data])

  return (
    <div className={styles.historyView}>
      <div className={styles.filterBar}>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="historyFrom">
            Fra dato
          </label>
          <input
            id="historyFrom"
            className={styles.filterInput}
            type="date"
            value={fromDraft}
            onChange={(e) => setFromDraft(e.target.value)}
          />
        </div>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="historyTo">
            Til dato
          </label>
          <input
            id="historyTo"
            className={styles.filterInput}
            type="date"
            value={toDraft}
            onChange={(e) => setToDraft(e.target.value)}
          />
        </div>
        <Button size="sm" onClick={applyFilter} disabled={loading}>
          Vis periode
        </Button>
        {isWindowed && (
          <Button variant="ghost" size="sm" onClick={resetFilter} disabled={loading}>
            Vis hele historikken
          </Button>
        )}
      </div>

      {rangeLooksInverted && (
        <p className={styles.inlineHint} data-testid="history-range-hint">
          &quot;Fra dato&quot; skal være tidligere end &quot;Til dato&quot;.
        </p>
      )}

      {loading && (
        <div className={styles.spinnerRow}>
          <Spinner size="lg" />
        </div>
      )}

      {!loading && error && errorStatus === 422 && (
        <Alert variant="warning">
          {parseReason(error) ??
            'Det angivne datointerval er ugyldigt: "Fra dato" skal være tidligere end "Til dato".'}
        </Alert>
      )}
      {!loading && error && errorStatus === 403 && (
        <Alert variant="error">
          Du har ikke adgang til at se denne medarbejders historik, eller også findes
          medarbejderen ikke.
        </Alert>
      )}
      {!loading && error && errorStatus !== 422 && errorStatus !== 403 && (
        <Alert variant="error">Historikken kunne ikke hentes. Prøv igen.</Alert>
      )}

      {!loading && !error && data && (
        <p className={styles.windowCaption} data-testid="history-window-caption">
          {data.windowFrom || data.windowTo
            ? `Viser perioden ${data.windowFrom ? formatDaDate(data.windowFrom) : 'starten'} – ${
                data.windowTo ? formatDaDate(data.windowTo) : 'nu'
              }.`
            : 'Viser hele den registrerede historik.'}
        </p>
      )}

      {!loading && !error && data && combined.length === 0 && (
        <div className={styles.emptyState} data-testid="history-empty">
          {isWindowed
            ? 'Ingen ændringer fundet i den valgte periode.'
            : 'Denne medarbejder har ingen registreret historik.'}
        </div>
      )}

      {!loading && !error && data && combined.length > 0 && (
        <Table headers={['Periode', 'Spor', 'Status', 'Ændringer', 'Detaljer']}>
          {combined.map((row) => {
            const status = isKnownStatus(row.status) ? row.status : null
            return (
              <tr
                key={`${row.track}-${row.trackIndex}`}
                className={status === 'SCHEDULED' ? styles.scheduledRow : undefined}
                data-testid={`history-row-${row.track.toLowerCase()}-${row.trackIndex}`}
              >
                <td>{formatPeriod(row.effectiveFrom, row.effectiveTo)}</td>
                <td>{TRACK_LABEL[row.track]}</td>
                <td>
                  <Badge variant={status ? STATUS_VARIANT[status] : 'default'}>
                    {status ? STATUS_LABEL[status] : row.status}
                  </Badge>
                </td>
                <td>{describeChanged(row)}</td>
                <td>{describeDetail(row)}</td>
              </tr>
            )
          })}
        </Table>
      )}
    </div>
  )
}
