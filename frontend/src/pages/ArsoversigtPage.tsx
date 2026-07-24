import { useState, useMemo, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useYearOverview, type YearOverview, type YearOverviewCategory } from '../hooks/useYearOverview'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Spinner } from '../components/ui/Spinner'
import { formatDanishNumber } from '../lib/locale'
import styles from './ArsoversigtPage.module.css'

const MONTH_ABBR = ['Jan', 'Feb', 'Mar', 'Apr', 'Maj', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dec']
const EM_DASH = '–'

/** Signed da-DK number, e.g. +2,3 / -8. */
function formatSigned(value: number): string {
  const formatted = formatDanishNumber(value)
  return value > 0 ? `+${formatted}` : formatted
}

/** Parse the server `today` (yyyy-MM-dd) into year + 0-based month index. */
function parseToday(today: string): { year: number; monthIndex: number } {
  const [y, m] = today.split('-')
  return { year: Number(y), monthIndex: Number(m) - 1 }
}

/** Per-tile descriptor. `kind` selects the days/hours display model (S123):
 *  - 'flex'          : value is HOURS (over/under-norm) → hours-first `H (D dage)`
 *  - 'hoursFirstDays': value is DAYS, hours-addable (ferie, barns sygedag) → `H (D dage)`
 *  - 'daysOnly'      : value is DAYS, full-day-only (omsorg, senior, sygedage) → `X dage`
 * Ineligible entitlements are OMITTED from the list entirely (owner OQ-1: show
 * nothing) — a remaining `value: null` is only a defensive graceful em-dash. */
type TileKind = 'flex' | 'hoursFirstDays' | 'daysOnly'

interface TileSpec {
  label: string
  value: number | null
  kind: TileKind
  sub: string
}

interface TileDisplay {
  primary: string
  unit: string
  /** hours-first day-equivalent, e.g. "(22 dage)"; null when no conversion. */
  paren: string | null
}

/** The authoritative weekday norm (hours per full day) enables the days↔hours
 * conversion; it is null (ANNUAL_ACTIVITY/no-profile) or 0 (0% part-time) when
 * conversion is impossible — guarded on `> 0` (mirrors the Skema norm guards). */
function canConvert(norm: number | null): norm is number {
  return norm != null && norm > 0
}

/** Hours-first-or-days-only tile value per the display model + the >0 guard. */
function tileDisplay(tile: TileSpec, norm: number | null): TileDisplay | null {
  if (tile.value == null) return null
  if (tile.kind === 'flex') {
    // native HOURS; day-equivalent = hours ÷ norm.
    const paren = canConvert(norm) ? `(${formatDanishNumber(tile.value / norm)} dage)` : null
    return { primary: formatDanishNumber(tile.value), unit: 't', paren }
  }
  if (tile.kind === 'hoursFirstDays' && canConvert(norm)) {
    // native DAYS; hours-equivalent = days × norm.
    return {
      primary: formatDanishNumber(tile.value * norm),
      unit: 't',
      paren: `(${formatDanishNumber(tile.value)} dage)`,
    }
  }
  // daysOnly, OR an hours-addable balance with no usable norm → native days, no parens.
  return { primary: formatDanishNumber(tile.value), unit: 'dage', paren: null }
}

// Grid-cell value per the category display unit + the >0 norm guard. Hours-addable
// (VACATION) cells STACK — hours on the top line,
// the day-equivalent below in a smaller muted font (S123 owner polish; the inline
// `H (D dage)` was too cramped in the dense monthly matrix). Days-only categories
// stay a single `X dage` line. (Tiles keep the inline `H (D dage)` — they have room.)
function formatCategoryValue(days: number, hoursFirst: boolean, norm: number | null) {
  if (hoursFirst && canConvert(norm)) {
    return (
      <>
        <span className={styles.cellHours}>{formatDanishNumber(days * norm)}</span>
        <span className={styles.cellDays}>{formatDanishNumber(days)} dage</span>
      </>
    )
  }
  return `${formatDanishNumber(days)} dage`
}

function buildTiles(data: YearOverview): TileSpec[] {
  const t = data.tiles
  const tiles: TileSpec[] = [
    { label: 'Difference fra norm tid - år', value: t.flexBalance, kind: 'flex', sub: 'optjent overtid' },
    { label: 'Ferie', value: t.ferieRemaining, kind: 'hoursFirstDays', sub: 'saldo' },
    { label: 'Omsorgsdage', value: t.careDayRemaining, kind: 'daysOnly', sub: 'saldo' },
  ]
  // Owner OQ-1: show NOTHING for ineligible entitlements — omit the tile entirely
  // (no em-dash placeholder). Seniordage needs senior eligibility; Barns sygedag
  // needs the stored child-sick opt-in.
  if (t.seniorDayEligible) {
    tiles.push({ label: 'Seniordage', value: t.seniorDayRemaining, kind: 'daysOnly', sub: 'saldo' })
  }
  tiles.push({ label: 'Sygedage', value: t.sickDaysYtd, kind: 'daysOnly', sub: 'i år' })
  if (t.childSickEligible) {
    tiles.push({ label: 'Barns sygedag', value: t.childSickRemaining, kind: 'hoursFirstDays', sub: 'saldo' })
  }
  return tiles
}

export function ArsoversigtPage() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''
  const navigate = useNavigate()

  // The selected year only seeds from the client clock for the INITIAL view; all
  // past/current/future + "Nu" classification comes from the server `today`.
  const [year, setYear] = useState(() => new Date().getFullYear())

  const { data, loading, error } = useYearOverview(employeeId, year)

  const goPrevYear = useCallback(() => setYear((y) => y - 1), [])
  const goNextYear = useCallback(() => setYear((y) => y + 1), [])

  // Drill-in must target the year actually DISPLAYED (data.year), not the `year`
  // state: a failed year switch keeps the old `data` while `year` advances, so
  // anchoring to `year` would land the user in a month of the wrong year. The
  // displayed year is passed in from the call site (data.year), so the label
  // ("Gå til Mar {data.year}") and the navigation target always agree.
  const goToMonth = useCallback(
    (displayedYear: number, monthOneBased: number) => {
      navigate(`/tid/registrering?year=${displayedYear}&month=${monthOneBased}`)
    },
    [navigate],
  )

  // Server-today authority: which calendar position are we at, and is the
  // currently-viewed year the live year (so "Nu" highlights apply)?
  const todayInfo = useMemo(() => (data ? parseToday(data.today) : null), [data])
  const isCurrentYear = !!todayInfo && data?.year === todayInfo.year
  const nowIndex = isCurrentYear ? todayInfo!.monthIndex : -1

  /** A month is future iff it is strictly after the server's current month in the live year. */
  const isFuture = useCallback(
    (i: number): boolean => {
      if (!todayInfo || !data) return false
      if (data.year < todayInfo.year) return false
      if (data.year > todayInfo.year) return true
      return i > todayInfo.monthIndex
    },
    [todayInfo, data],
  )

  if (loading && !data) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="lg" />
        <p>Indlæser årsoversigt…</p>
      </div>
    )
  }

  if (error && !data) {
    return (
      <Card>
        <p className={styles.errorText}>Kunne ikke indlæse årsoversigt: {error}</p>
      </Card>
    )
  }

  if (!data) return null

  const tiles = buildTiles(data)
  const norm = data.header.weeklyNormHours
  // Authoritative weekday norm (hours per full day) for the days↔hours model.
  const fullDayNorm = data.header.fullDayNormHours
  const subLine =
    `${data.header.employeeName} · ${data.header.agreementCode}` +
    ` · Norm: ${norm != null ? formatDanishNumber(norm) : EM_DASH} t/uge`

  // cell class for a month index: now-tint > future-projected.
  const cellClass = (i: number): string => {
    if (i === nowIndex) return `${styles.num} ${styles.now}`
    if (isFuture(i)) return `${styles.num} ${styles.proj}`
    return styles.num
  }

  return (
    <div className={styles.page}>
      {/* Page header row */}
      <div className={styles.header}>
        <div className={styles.headerText}>
          <h1 className={styles.title}>Årsoversigt {data.year}</h1>
          <p className={styles.sub}>{subLine}</p>
        </div>
        <div className={styles.yearSwitch}>
          <Button variant="ghost" size="sm" onClick={goPrevYear} aria-label="Forrige år">
            &larr;
          </Button>
          <span className={styles.yearLabel}>{data.year}</span>
          <Button variant="ghost" size="sm" onClick={goNextYear} aria-label="Næste år">
            &rarr;
          </Button>
        </div>
      </div>

      {/* Current-balance tiles (ineligible entitlements are omitted, not em-dashed) */}
      <div className={styles.statRow}>
        {tiles.map((tile) => {
          const disp = tileDisplay(tile, fullDayNorm)
          return (
            <div className={styles.stat} key={tile.label}>
              <p className={styles.statLabel}>{tile.label}</p>
              <p className={styles.statValue}>
                {disp ? (
                  <>
                    {disp.primary} <small>{disp.unit}</small>
                    {disp.paren ? ` ${disp.paren}` : ''}
                  </>
                ) : (
                  <span className={styles.dash}>{EM_DASH}</span>
                )}
              </p>
              <p className={styles.statSub}>{tile.sub}</p>
            </div>
          )
        })}
      </div>

      {/* Stale-data banner: a year switch failed; we keep showing the last good
          year. Names BOTH the failed year (the `year` state) and the year still
          on screen (data.year) so the user understands the mismatch. */}
      {error && (
        <div className={styles.staleBanner} role="alert">
          Kunne ikke indlæse {year}: viser {data.year}
        </div>
      )}

      {/* Year matrix */}
      <Card>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <colgroup>
              <col className={styles.colLabel} />
              {MONTH_ABBR.map((m) => (
                <col key={m} />
              ))}
            </colgroup>
            <thead>
              <tr>
                <th scope="col" className={styles.labelHead}>
                  {data.year}
                </th>
                {MONTH_ABBR.map((m, i) => (
                  <th
                    key={m}
                    scope="col"
                    className={i === nowIndex ? styles.nowHead : undefined}
                  >
                    {i === nowIndex && <span className={styles.nowTag}>Nu</span>}
                    <button
                      type="button"
                      className={styles.monthButton}
                      onClick={() => goToMonth(data.year, i + 1)}
                      aria-label={`Gå til ${MONTH_ABBR[i]} ${data.year}`}
                    >
                      {m}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {/* Arbejdstid group */}
              <tr className={`${styles.group} ${styles.groupFirst}`}>
                <td colSpan={13}>Arbejdstid</td>
              </tr>
              <tr className={styles.row}>
                <th scope="row" className={styles.labelCell}>
                  Arbejdstid
                </th>
                {data.months.map((mo, i) => {
                  // past/current → workedHours; future → normHours (projected).
                  const value = isFuture(i) ? mo.normHours : mo.workedHours
                  return (
                    <td key={i} className={cellClass(i)}>
                      {value != null ? (
                        formatDanishNumber(value)
                      ) : (
                        <span className={styles.dash}>{EM_DASH}</span>
                      )}
                    </td>
                  )
                })}
              </tr>
              <tr className={`${styles.row} ${styles.rowSub}`}>
                <th scope="row" className={styles.labelCell}>
                  Diff. fra norm
                </th>
                {data.months.map((mo, i) => {
                  // Signed diff for past/current; "–" for future (diff is null there).
                  const diff = isFuture(i) ? null : mo.diff
                  let cls = cellClass(i)
                  if (diff != null && diff > 0) cls += ` ${styles.pos}`
                  else if (diff != null && diff < 0) cls += ` ${styles.neg}`
                  return (
                    <td key={i} className={cls}>
                      {diff != null ? (
                        formatSigned(diff)
                      ) : (
                        <span className={styles.dash}>{EM_DASH}</span>
                      )}
                    </td>
                  )
                })}
              </tr>

              {/* Absence-category groups. The backend already excludes SENIOR_DAY
                  for the age-ineligible (S123); the filter is a defensive mirror
                  so a stale/buggy wire never shows a senior row without a tile. */}
              {data.categories
                .filter((cat) => !(cat.type === 'SENIOR_DAY' && !data.tiles.seniorDayEligible))
                .map((cat) => (
                  <CategoryGroup
                    key={cat.type}
                    category={cat}
                    cellClass={cellClass}
                    fullDayNorm={fullDayNorm}
                  />
                ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  )
}

interface CategoryGroupProps {
  category: YearOverviewCategory
  cellClass: (i: number) => string
  /** Authoritative weekday norm for the days↔hours conversion (null/0 → days-only). */
  fullDayNorm: number | null
}

/** One leave group: header + Saldo / Afholdt / disposition (Til udløb / Til
 * udbetaling) rows. Every row renders in the category's display unit (S123):
 * VACATION is hours-first `H (D dage)`; the full-day + special-holiday
 * categories are days-only `X dage`. */
function CategoryGroup({ category, cellClass, fullDayNorm }: CategoryGroupProps) {
  const boundaryIndex = category.boundaryMonth - 1
  // Only VACATION is hours-addable; CARE_DAY/SENIOR_DAY/SPECIAL_HOLIDAY are days-only.
  const hoursFirst = category.type === 'VACATION'
  const fmt = (v: number) => formatCategoryValue(v, hoursFirst, fullDayNorm)
  // Period-end disposition label keys off the category type: untaken særlige
  // feriedage convert to the 2½% godtgørelse (money → "Til udbetaling"); every
  // other type genuinely lapses ("Til udløb").
  const dispositionLabel =
    category.type === 'SPECIAL_HOLIDAY' ? 'Til udbetaling' : 'Til udløb'
  return (
    <>
      <tr className={styles.group}>
        <td colSpan={13}>{category.label}</td>
      </tr>

      <tr className={`${styles.row} ${styles.rowSub}`}>
        <th scope="row" className={styles.labelCell}>
          Saldo
        </th>
        {category.saldo.map((v, i) => (
          <td key={i} className={cellClass(i)}>
            {/* null (no-config graceful row) and 0 both render the em-dash. */}
            {v != null && v !== 0 ? (
              fmt(v)
            ) : (
              <span className={styles.dash}>{EM_DASH}</span>
            )}
          </td>
        ))}
      </tr>

      <tr className={`${styles.row} ${styles.rowSub}`}>
        <th scope="row" className={styles.labelCell}>
          Afholdt
        </th>
        {category.afholdt.map((v, i) => (
          <td key={i} className={cellClass(i)}>
            {v !== 0 ? (
              fmt(v)
            ) : (
              <span className={styles.dash}>{EM_DASH}</span>
            )}
          </td>
        ))}
      </tr>

      <tr className={`${styles.row} ${styles.rowSub}`}>
        <th scope="row" className={styles.labelCell}>
          {dispositionLabel}
        </th>
        {MONTH_ABBR.map((_, i) => {
          // The period-end disposition (expiring-beyond-cap) renders ONLY in the
          // boundaryMonth column when > 0.
          const show = i === boundaryIndex && category.expiring > 0
          const cls = show ? `${cellClass(i)} ${styles.keep}` : cellClass(i)
          return (
            <td key={i} className={cls}>
              {show ? (
                fmt(category.expiring)
              ) : (
                <span className={styles.dash}>{EM_DASH}</span>
              )}
            </td>
          )
        })}
      </tr>
    </>
  )
}
