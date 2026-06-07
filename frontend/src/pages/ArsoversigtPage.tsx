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

/** Per-tile descriptor for the 6 designed balance tiles. */
interface TileSpec {
  label: string
  /** null → ineligible → renders an em-dash value with unchanged layout. */
  value: number | null
  unit: string
  sub: string
}

function buildTiles(data: YearOverview): TileSpec[] {
  const t = data.tiles
  return [
    { label: 'Flex saldo', value: t.flexBalance, unit: 't', sub: 'optjent overtid' },
    { label: 'Ferie', value: t.ferieRemaining, unit: 'dage', sub: 'saldo' },
    { label: 'Omsorgsdage', value: t.careDayRemaining, unit: 'dage', sub: 'rest' },
    {
      label: 'Seniordage',
      value: t.seniorDayEligible ? t.seniorDayRemaining : null,
      unit: 'dage',
      sub: 'rest',
    },
    { label: 'Sygedage', value: t.sickDaysYtd, unit: 'dage', sub: 'i år' },
    {
      label: 'Barns sygedag',
      value: t.childSickEligible ? t.childSickRemaining : null,
      unit: 'dage',
      sub: 'rest',
    },
  ]
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

      {/* Current-balance tiles */}
      <div className={styles.statRow}>
        {tiles.map((tile) => (
          <div className={styles.stat} key={tile.label}>
            <p className={styles.statLabel}>{tile.label}</p>
            <p className={styles.statValue}>
              {tile.value != null ? (
                <>
                  {formatDanishNumber(tile.value)} <small>{tile.unit}</small>
                </>
              ) : (
                <span className={styles.dash}>{EM_DASH}</span>
              )}
            </p>
            <p className={styles.statSub}>{tile.sub}</p>
          </div>
        ))}
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

              {/* Absence-category groups */}
              {data.categories.map((cat) => (
                <CategoryGroup
                  key={cat.type}
                  category={cat}
                  cellClass={cellClass}
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
}

/** One leave group: header + Saldo (rest) / Afholdt / disposition (Til udløb /
 * Til udbetaling) rows. */
function CategoryGroup({ category, cellClass }: CategoryGroupProps) {
  const boundaryIndex = category.boundaryMonth - 1
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
          Saldo (rest)
        </th>
        {category.saldo.map((v, i) => (
          <td key={i} className={cellClass(i)}>
            {/* null (no-config graceful row) and 0 both render the em-dash. */}
            {v != null && v !== 0 ? (
              formatDanishNumber(v)
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
              formatDanishNumber(v)
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
                formatDanishNumber(category.expiring)
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
