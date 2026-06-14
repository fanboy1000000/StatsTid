import { useState, useEffect, useMemo } from 'react'
import { useOrganizations } from '../../hooks/useAdmin'
import {
  useMedarbejderRoster,
  type MedarbejderRosterRow,
} from '../../hooks/useMedarbejderRoster'
import {
  indexBy,
  childrenOf,
  orphansOf,
  depthMap,
  collapsedForLevel,
  defaultCollapsed,
  queryHit,
  visibleTreeRows,
} from './medarbejderTree'
import { Card, Badge, Input, Spinner } from '../../components/ui'
import styles from './MedarbejderAdministration.module.css'

// S75 TASK-7502. Medarbejder-administration page — Phase 2 of the program.
// READ-ONLY this phase: every write affordance from the prototype is stripped
// (no "Tilføj medarbejder", no approver picker, no +Vikar/Afslut, no record
// drawer, no orphan assign, no enforcement toggle). The structural ledelseslinje
// tree is rendered from the 7501 roster contract via the pure helpers in
// medarbejderTree.ts. Write flows land in S76. The shell mirrors
// ReportingLineTree.tsx (bare <div>, AppLayout provided by the routing shell;
// Styrelse selector = useOrganizations filtered to MINISTRY/STYRELSE).

// The prototype's period copy is not served this phase — hardcode the literals
// (ledelseslinjer-data.jsx PERIOD_LABEL / PERIOD_DEADLINE).
const PERIOD_LABEL = 'Maj 2026'
const PERIOD_DEADLINE = '5. juni'

type StatusFilter = 'alle' | 'indsend' | 'godkend' | 'vikar'

// ---- da-DK date formatting (mirrors the prototype's fmtDate, short+year) ----
const MONTHS = [
  'januar', 'februar', 'marts', 'april', 'maj', 'juni',
  'juli', 'august', 'september', 'oktober', 'november', 'december',
]
function fmtDate(iso: string): string {
  if (!iso) return ''
  const [y, mo, d] = iso.split('-').map(Number)
  const month = MONTHS[(mo ?? 1) - 1] ?? ''
  return `${d}. ${month.slice(0, 3)} ${y}`
}

function initialsOf(name: string): string {
  if (!name) return '?'
  const parts = String(name).trim().split(/\s+/)
  const first = parts[0]?.[0] ?? ''
  const last = parts.length > 1 ? parts[parts.length - 1][0] : ''
  return (first + last).toUpperCase()
}

/** Display-only avatar chrome (initials token) — not a write flow. */
function Avatar({ name, tone = 'neutral' }: { name: string; tone?: 'neutral' | 'vikar' }) {
  const cls = [styles.avatar, tone === 'vikar' ? styles.avatarVikar : '']
    .filter(Boolean)
    .join(' ')
  return (
    <span className={cls} aria-hidden="true">
      {initialsOf(name)}
    </span>
  )
}

function IconClock() {
  return (
    <svg width={12} height={12} viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="2" />
      <path d="M8 4.5V8.2L10.5 9.6" stroke="currentColor" strokeWidth="2" strokeLinecap="square" />
    </svg>
  )
}

function IconWarn() {
  return (
    <svg width={16} height={16} viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <path d="M8 1.5L15 14H1L8 1.5Z" stroke="currentColor" strokeWidth="2" strokeLinecap="square" strokeLinejoin="miter" />
      <path d="M8 6V9.5" stroke="currentColor" strokeWidth="2" strokeLinecap="square" />
      <path d="M8 11.5V11.6" stroke="currentColor" strokeWidth="2" strokeLinecap="square" />
    </svg>
  )
}

interface PersonRowProps {
  people: MedarbejderRosterRow[]
  byId: Record<string, MedarbejderRosterRow>
  person: MedarbejderRosterRow
  depth: number
  expandable: boolean
  open: boolean
  onToggle: (id: string) => void
  context: boolean
  statusFilter: StatusFilter
  pendingCountByManager: Record<string, number>
}

/** One rendered tree row — DISPLAY-ONLY. Name is plain text (no record drawer),
 *  approver line is read-only, vikar line has no Afslut/+Vikar buttons. */
function PersonRow({
  people,
  byId,
  person,
  depth,
  expandable,
  open,
  onToggle,
  context,
  statusFilter,
  pendingCountByManager,
}: PersonRowProps) {
  const reports = childrenOf(people, person.employeeId).length
  const manager = reports > 0
  const away = person.outgoingVikar != null
  const pend = pendingCountByManager[person.employeeId] ?? 0
  const mgr = person.structuralApproverId ? byId[person.structuralApproverId] : null

  const rowCls = [
    styles.prow,
    manager ? styles.prowMgr : '',
    context ? styles.prowContext : '',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <div className={rowCls}>
      <div className={styles.prowLead}>
        {Array.from({ length: depth }).map((_, i) => (
          <span key={i} className={styles.rail} aria-hidden="true" />
        ))}
        <button
          type="button"
          className={`${styles.toggle} ${expandable ? '' : styles.toggleLeaf}`}
          onClick={() => expandable && onToggle(person.employeeId)}
          aria-label={open ? 'Skjul' : 'Vis'}
          aria-expanded={open}
          disabled={!expandable}
        >
          {expandable ? (open ? '–' : '+') : ''}
        </button>
        <Avatar name={person.displayName} tone={away ? 'vikar' : 'neutral'} />
        <div className={styles.prowId}>
          <span className={styles.prowNameline}>
            {away ? (
              <span className={styles.onleave}>
                <span className={styles.onleaveName}>{person.displayName}</span>
                <span className={styles.onleaveTag}>
                  {(person.outgoingVikar?.reason || 'fravær').toLowerCase()} · til{' '}
                  {fmtDate(person.outgoingVikar?.untilDate ?? '')}
                </span>
              </span>
            ) : (
              <span className={styles.prowName}>{person.displayName}</span>
            )}
            {away && person.outgoingVikar && (
              <span className={styles.vikarline}>
                <span className={styles.vikarlineK}>
                  <IconClock /> Vikar
                </span>
                <span className={styles.vikarlineName}>
                  {person.outgoingVikar.vikarDisplayName} til{' '}
                  {fmtDate(person.outgoingVikar.untilDate)}
                </span>
              </span>
            )}
            {statusFilter === 'indsend' && person.periodStatus === 'OPEN' && !person.isOrphan && (
              <Badge variant="warning">Ikke indsendt</Badge>
            )}
            {statusFilter === 'godkend' && pend > 0 && (
              <Badge variant="error">Ikke godkendt</Badge>
            )}
          </span>
          <span className={styles.prowTitle}>
            {person.position}
            <span className={styles.orgtag}>{person.enhedLabel}</span>
            {manager && (
              <span className={styles.muted}>
                {' '}
                · godkender {reports}{' '}
                {reports === 1 ? 'medarbejder' : 'medarbejdere'}
              </span>
            )}
          </span>
        </div>
      </div>

      <div className={styles.prowAppr}>
        {person.isRoot ? (
          <span className={styles.muted} style={{ fontSize: 13 }}>
            Øverste godkendelseslinje
          </span>
        ) : (
          <span className={styles.appr}>
            <span className={styles.rolesK}>Godkendes af</span>
            <span className={styles.apprName}>
              {mgr ? mgr.displayName : person.structuralApproverId}
            </span>
          </span>
        )}
      </div>
    </div>
  )
}

export function MedarbejderAdministration() {
  const { organizations, loading: orgsLoading } = useOrganizations()
  const { fetchRoster } = useMedarbejderRoster()

  const treeRootOrgs = useMemo(
    () =>
      organizations.filter(
        (o) => o.orgType === 'MINISTRY' || o.orgType === 'STYRELSE',
      ),
    [organizations],
  )

  const [selectedTreeRoot, setSelectedTreeRoot] = useState('')
  const [people, setPeople] = useState<MedarbejderRosterRow[]>([])
  const [pendingCountByManager, setPendingCountByManager] = useState<
    Record<string, number>
  >({})
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // tree view state — RESET on Styrelse switch (R6)
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [levelSel, setLevelSel] = useState<number | null>(null)
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('alle')
  const [query, setQuery] = useState('')

  // Default to the first tree root org once loaded
  useEffect(() => {
    if (treeRootOrgs.length > 0 && !selectedTreeRoot) {
      setSelectedTreeRoot(treeRootOrgs[0].orgId)
    }
  }, [treeRootOrgs, selectedTreeRoot])

  // Load the roster whenever the selected Styrelse changes, and RESET view state.
  useEffect(() => {
    if (!selectedTreeRoot) return
    let cancelled = false
    setLoading(true)
    setError(null)
    setStatusFilter('alle')
    setQuery('')
    setLevelSel(null)
    fetchRoster(selectedTreeRoot).then((result) => {
      if (cancelled) return
      if (result.ok) {
        setPeople(result.data.employees)
        setPendingCountByManager(result.data.pendingCountByManager)
        setCollapsed(defaultCollapsed(result.data.employees))
      } else {
        setPeople([])
        setPendingCountByManager({})
        setCollapsed(new Set())
        setError(result.error)
      }
      setLoading(false)
    })
    return () => {
      cancelled = true
    }
  }, [selectedTreeRoot, fetchRoster])

  const byId = useMemo(() => indexBy(people), [people])

  // ---- Tile counts (mapped to the served contract) ----
  const indsendCount = useMemo(
    () =>
      people.filter((p) => p.periodStatus === 'OPEN' && !p.isOrphan).length,
    [people],
  )
  const godkendCount = useMemo(
    () => Object.keys(pendingCountByManager).length,
    [pendingCountByManager],
  )
  const vikarCount = useMemo(
    () => people.filter((p) => p.outgoingVikar != null).length,
    [people],
  )

  const toggleFilter = (f: StatusFilter) =>
    setStatusFilter((cur) => (cur === f ? 'alle' : f))

  const toggleCollapse = (id: string) => {
    setLevelSel(null)
    setCollapsed((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })
  }

  const applyLevel = (L: number) => {
    setLevelSel(L)
    setStatusFilter('alle')
    setCollapsed(collapsedForLevel(people, L))
  }

  // ---- Level segmented control options ----
  const totalLevels = useMemo(() => {
    const depths = Object.values(depthMap(people))
    return (depths.length > 0 ? Math.max(...depths) : 0) + 1
  }, [people])
  const levelOptions = useMemo(
    () =>
      Array.from({ length: Math.max(0, totalLevels - 1) }, (_, i) => i + 1),
    [totalLevels],
  )

  // ---- Status-filter → matchIds → visibleSet (matches + ancestor chain) ----
  const q = query.trim().toLowerCase()

  const { matchIds, visibleSet, orphanMatches } = useMemo(() => {
    let ids: Set<string> | null = null
    if (statusFilter === 'indsend') {
      ids = new Set(
        people
          .filter((p) => p.periodStatus === 'OPEN' && !p.isOrphan)
          .map((p) => p.employeeId),
      )
    } else if (statusFilter === 'godkend') {
      ids = new Set(Object.keys(pendingCountByManager))
    } else if (statusFilter === 'vikar') {
      ids = new Set(
        people.filter((p) => p.outgoingVikar != null).map((p) => p.employeeId),
      )
    }

    if (q) {
      ids = new Set(
        people
          .filter((p) => queryHit(p, q) && (!ids || ids.has(p.employeeId)))
          .map((p) => p.employeeId),
      )
    }

    const orphanIds = new Set(orphansOf(people).map((p) => p.employeeId))
    const orphanMatchCount = ids
      ? [...ids].filter((id) => orphanIds.has(id)).length
      : 0

    let vSet: Set<string> | null = null
    if (ids) {
      vSet = new Set<string>()
      ids.forEach((id) => {
        let cur: MedarbejderRosterRow | undefined = byId[id]
        const guard = new Set<string>()
        while (cur && !guard.has(cur.employeeId)) {
          guard.add(cur.employeeId)
          vSet!.add(cur.employeeId)
          cur = cur.structuralApproverId ? byId[cur.structuralApproverId] : undefined
        }
      })
    }

    return {
      matchIds: ids,
      visibleSet: vSet,
      orphanMatches: orphanMatchCount,
    }
  }, [statusFilter, q, people, pendingCountByManager, byId])

  const rows = useMemo(
    () => visibleTreeRows(people, collapsed, visibleSet),
    [people, collapsed, visibleSet],
  )

  // ---- Orphan card list — narrows in lockstep with the active filter/search via
  //      the SHARED matchIds (which already folds in statusFilter + query). So a
  //      status filter whose classification excludes orphans (indsend/godkend/vikar
  //      all do) hides the card, instead of showing filter-unrelated broken lines;
  //      search still narrows it by query; the unfiltered "alle" state shows all. ----
  const allOrphans = useMemo(() => orphansOf(people), [people])
  const orphansFiltered = useMemo(
    () => (matchIds ? allOrphans.filter((p) => matchIds.has(p.employeeId)) : allOrphans),
    [allOrphans, matchIds],
  )

  const showLevelActive = (L: number | typeof Infinity) =>
    levelSel === L && statusFilter === 'alle' && !q

  return (
    <div className={styles.page}>
      <div className={styles.pagehead}>
        <h1 className={styles.title}>Medarbejder administration</h1>
        <div className={styles.instpick}>
          <label className={styles.rolesK} htmlFor="medarbejderInst">
            Styrelse
          </label>
          {orgsLoading ? (
            <div className={styles.spinner}>
              <Spinner size="md" />
            </div>
          ) : (
            <select
              id="medarbejderInst"
              className={styles.select}
              value={selectedTreeRoot}
              onChange={(e) => setSelectedTreeRoot(e.target.value)}
            >
              {treeRootOrgs.map((org) => (
                <option key={org.orgId} value={org.orgId}>
                  {org.orgName}
                </option>
              ))}
            </select>
          )}
        </div>
      </div>

      {/* Filter tiles */}
      <div className={styles.stats}>
        <button
          type="button"
          className={`${styles.stat} ${styles.statWarn} ${statusFilter === 'indsend' ? styles.statOn : ''}`}
          onClick={() => toggleFilter('indsend')}
          aria-pressed={statusFilter === 'indsend'}
        >
          <p className={styles.statLabel}>Ikke indsendt</p>
          <p className={styles.statValue}>{indsendCount}</p>
          <p className={styles.statDetail}>måned efter frist · {PERIOD_LABEL}</p>
        </button>
        <button
          type="button"
          className={`${styles.stat} ${styles.statAlert} ${statusFilter === 'godkend' ? styles.statOn : ''}`}
          onClick={() => toggleFilter('godkend')}
          aria-pressed={statusFilter === 'godkend'}
        >
          <p className={styles.statLabel}>Ikke godkendt</p>
          <p className={styles.statValue}>{godkendCount}</p>
          <p className={styles.statDetail}>godkendere efter frist</p>
        </button>
        <button
          type="button"
          className={`${styles.stat} ${styles.statVikar} ${statusFilter === 'vikar' ? styles.statOn : ''}`}
          onClick={() => toggleFilter('vikar')}
          aria-pressed={statusFilter === 'vikar'}
        >
          <p className={styles.statLabel}>Vikar</p>
          <p className={styles.statValue}>{vikarCount}</p>
          <p className={styles.statDetail}>aktive vikarieringer</p>
        </button>
      </div>

      {error && <div className={styles.alert}>{error}</div>}

      {loading && (
        <div className={styles.spinner}>
          <Spinner size="lg" />
        </div>
      )}

      {!loading && !error && people.length === 0 && selectedTreeRoot && (
        <div className={styles.emptyState}>
          Ingen medarbejdere fundet for denne styrelse
        </div>
      )}

      {!loading && !error && people.length > 0 && (
        <div className={styles.stack}>
          {/* Orphan list — DISPLAY-ONLY */}
          {orphansFiltered.length > 0 && (
            <Card
              className={styles.orphans}
              header={
                <span className={styles.orphansTitle}>
                  <IconWarn />{' '}
                  {q
                    ? `${orphansFiltered.length} af ${allOrphans.length} mangler godkender`
                    : `${allOrphans.length} mangler godkender`}
                </span>
              }
            >
              <div className={styles.tree}>
                {orphansFiltered.map((person) => (
                  <div
                    key={person.employeeId}
                    className={`${styles.prow} ${styles.prowOrphan}`}
                  >
                    <div className={styles.prowLead}>
                      <span className={`${styles.toggle} ${styles.toggleLeaf}`} />
                      <Avatar name={person.displayName} />
                      <div className={styles.prowId}>
                        <span className={styles.prowName}>
                          {person.displayName}
                        </span>
                        <span className={styles.prowTitle}>
                          {person.position}
                          <span className={styles.orgtag}>
                            {person.enhedLabel}
                          </span>
                        </span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </Card>
          )}

          {/* Level bar + search */}
          <div>
            <div className={styles.levelbar}>
              <span className={styles.rolesK} id="medarbejderLevellabel">
                Vis niveau
              </span>
              <div
                className={styles.seg}
                role="group"
                aria-labelledby="medarbejderLevellabel"
              >
                {levelOptions.map((L) => (
                  <button
                    key={L}
                    type="button"
                    className={`${styles.segBtn} ${showLevelActive(L) ? styles.segBtnActive : ''}`}
                    onClick={() => applyLevel(L)}
                  >
                    {L}
                  </button>
                ))}
                <button
                  type="button"
                  className={`${styles.segBtn} ${showLevelActive(Infinity) ? styles.segBtnActive : ''}`}
                  onClick={() => applyLevel(Infinity)}
                >
                  Alle
                </button>
              </div>
              {q && matchIds && (
                <span className={styles.muted} style={{ fontSize: 12 }}>
                  {matchIds.size}{' '}
                  {matchIds.size === 1 ? 'resultat' : 'resultater'}
                  {orphanMatches > 0
                    ? ` · ${orphanMatches} under ”mangler godkender”`
                    : ''}
                </span>
              )}
              {!q &&
                (statusFilter === 'indsend' || statusFilter === 'godkend') &&
                matchIds && (
                  <span className={styles.muted} style={{ fontSize: 12 }}>
                    {PERIOD_LABEL} · frist {PERIOD_DEADLINE} er overskredet ·{' '}
                    {matchIds.size}{' '}
                    {matchIds.size === 1 ? 'linje' : 'linjer'}
                  </span>
                )}
              {!q && statusFilter === 'vikar' && matchIds && (
                <span className={styles.muted} style={{ fontSize: 12 }}>
                  {matchIds.size}{' '}
                  {matchIds.size === 1
                    ? 'aktiv vikariering'
                    : 'aktive vikarieringer'}
                </span>
              )}
              <div className={styles.search}>
                <Input
                  id="medarbejderSearch"
                  type="search"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Søg navn, stilling eller enhed…"
                  aria-label="Søg medarbejder, stilling eller enhed"
                />
                {q && (
                  <button
                    type="button"
                    className={styles.link}
                    onClick={() => setQuery('')}
                  >
                    Ryd
                  </button>
                )}
              </div>
            </div>

            <Card>
              <div className={styles.tree}>
                {rows.map(({ row, depth }) => {
                  const expandable =
                    !visibleSet &&
                    childrenOf(people, row.employeeId).length > 0
                  const open = !collapsed.has(row.employeeId)
                  const isContext =
                    !!visibleSet && !!matchIds && !matchIds.has(row.employeeId)
                  return (
                    <PersonRow
                      key={row.employeeId}
                      people={people}
                      byId={byId}
                      person={row}
                      depth={depth}
                      expandable={expandable}
                      open={open}
                      onToggle={toggleCollapse}
                      context={isContext}
                      statusFilter={statusFilter}
                      pendingCountByManager={pendingCountByManager}
                    />
                  )
                })}
                {rows.length === 0 && (
                  <div className={styles.prow}>
                    <span className={styles.muted} style={{ padding: '4px 0' }}>
                      {q && orphanMatches > 0
                        ? `${orphanMatches} ${orphanMatches === 1 ? 'match findes' : 'matcher findes'} i listen ”mangler godkender” ovenfor.`
                        : q
                          ? 'Ingen medarbejdere matcher søgningen.'
                          : 'Ingen linjer matcher filteret.'}
                    </span>
                  </div>
                )}
              </div>
            </Card>
          </div>
        </div>
      )}
    </div>
  )
}
