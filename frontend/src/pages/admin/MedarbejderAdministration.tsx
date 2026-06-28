import { useState, useEffect, useMemo, useCallback } from 'react'
import { useOrganizations, useOrgUsers, type User } from '../../hooks/useAdmin'
import {
  useMedarbejderRoster,
  type MedarbejderRosterRow,
} from '../../hooks/useMedarbejderRoster'
import {
  indexBy,
  childrenOf,
  orphansOf,
  descendantsOf,
  depthMap,
  collapsedForLevel,
  defaultCollapsed,
  queryHit,
  visibleTreeRows,
} from './medarbejderTree'
import { Card, Badge, Input, Spinner } from '../../components/ui'
import { useToast } from '../../components/ui/Toast'
import { EditPersonDrawer } from './EditPersonDrawer'
import type { LifecycleContext } from './editPerson/LifecycleSections'
import { InlineApproverControl } from './editPerson/InlineApproverControl'
import { InlineVikarControl } from './editPerson/InlineVikarControl'
import styles from './MedarbejderAdministration.module.css'

// Medarbejder-administration page (admin/ledelseslinjer). The structural
// ledelseslinje tree is rendered from the 7501 roster contract via the pure
// helpers in medarbejderTree.ts. The shell mirrors ReportingLineTree.tsx (bare
// <div>, AppLayout provided by the routing shell; Styrelse selector =
// useOrganizations filtered to MAO/ORGANISATION).
//
// WRITE AFFORDANCES (full-edit lives in the EditPersonDrawer — opened by clicking
// a person's name; S76b/7604). S86 ADDED inline QUICK-ACTION write affordances on
// the tree rows + the orphan card (hifi parity): Skift / + Tildel godkender on the
// approver block, + Vikar on a not-away manager, Afslut on an away manager's vikar
// line, and an inline approver-assign on each orphan row. Each lazy-mounts the
// SHARED drawer section components (ApproverSection / VikarSection) — the single
// mutation cores — so the row and the drawer hit ONE save path (no second writer).
// The approver-block shows the info-blue "· pt. <vikar> (vikar)" annotation when
// the assigned approver is currently away.

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
  /** S76b/7604 — open the edit drawer for this person (row-click). */
  onEdit: (person: MedarbejderRosterRow) => void
  /** S86 — open the edit drawer for an arbitrary person by id (the vikar-name link). */
  onEditById: (id: string) => void
  /** S86 — fired after an inline mutation so the page refetches the roster. */
  onChanged: () => void
}

/** One rendered tree row. The name opens the unified EditPersonDrawer in EDIT
 *  mode (full edit). S86 ADDS inline quick-action write affordances on the row —
 *  Skift / + Tildel godkender (approver block), + Vikar (not-away manager), Afslut
 *  (away manager's vikar line) — each lazy-mounting the SHARED ApproverSection /
 *  VikarSection mutation cores (no second save path; the drawer is unchanged). */
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
  onEdit,
  onEditById,
  onChanged,
}: PersonRowProps) {
  const reports = childrenOf(people, person.employeeId).length
  const manager = reports > 0
  const away = person.outgoingVikar != null
  const pend = pendingCountByManager[person.employeeId] ?? 0
  const mgr = person.structuralApproverId ? byId[person.structuralApproverId] : null
  // S86 — is THIS person's assigned approver currently away? (O(1) via byId; the
  // roster carries outgoingVikar on the away person's own row → look it up on the
  // approver). Drives the info-blue "· pt. <vikar> (vikar)" approver annotation.
  const approverAwayVikarName = mgr?.outgoingVikar?.vikarDisplayName ?? null
  // S86 — the cycle-prevention forbidden set for this person's inline pickers
  // (self + descendants), computed lazily inside the control via a small helper.
  const forbiddenFor = (id: string): Set<string> => {
    const set = descendantsOf(people, id)
    set.add(id)
    return set
  }

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
                <button
                  type="button"
                  className={`${styles.onleaveName} ${styles.nameBtn}`}
                  onClick={() => onEdit(person)}
                  data-testid={`person-edit-${person.employeeId}`}
                >
                  {person.displayName}
                </button>
                <span className={styles.onleaveTag}>
                  {(person.outgoingVikar?.reason || 'fravær').toLowerCase()} · til{' '}
                  {fmtDate(person.outgoingVikar?.untilDate ?? '')}
                </span>
              </span>
            ) : (
              <span className={styles.prowName}>
                <button
                  type="button"
                  className={styles.nameBtn}
                  onClick={() => onEdit(person)}
                  data-testid={`person-edit-${person.employeeId}`}
                >
                  {person.displayName}
                </button>
              </span>
            )}
            {away && person.outgoingVikar && (
              <span className={styles.vikarline}>
                <span className={styles.vikarlineK}>
                  <IconClock /> Vikar
                </span>
                {/* S86 — the vikar NAME is now a link → opens that person's record
                    (hifi ledelseslinjer-tree.jsx:70), with the "til <date>" after. */}
                <button
                  type="button"
                  className={`${styles.nameBtn} ${styles.vikarlineName}`}
                  onClick={() => onEditById(person.outgoingVikar!.vikarUserId)}
                  data-testid={`vikar-link-${person.employeeId}`}
                  title={`${person.outgoingVikar.vikarDisplayName} varetager godkendelser til ${fmtDate(person.outgoingVikar.untilDate)}`}
                >
                  {person.outgoingVikar.vikarDisplayName}
                </button>
                <span className={styles.muted}>
                  til {fmtDate(person.outgoingVikar.untilDate)}
                </span>
                <span className={styles.muted}>·</span>
                {/* S86 — inline "Afslut": lazy-mounts the shared VikarSection in its
                    active-vikar state (it owns the endVikar call). */}
                <InlineVikarControl
                  managerId={person.employeeId}
                  managerName={person.displayName}
                  computeForbidden={() => forbiddenFor(person.employeeId)}
                  mode="end"
                  activeVikar={{
                    vikarUserId: person.outgoingVikar.vikarUserId,
                    vikarDisplayName: person.outgoingVikar.vikarDisplayName,
                    untilDate: person.outgoingVikar.untilDate,
                    reason: person.outgoingVikar.reason,
                  }}
                  onChanged={onChanged}
                  className={styles.link}
                />
              </span>
            )}
            {/* S86 — a not-away manager gets the inline "+ Vikar" affordance (lazy
                mounts the shared VikarSection with its create form). */}
            {manager && !away && (
              <InlineVikarControl
                managerId={person.employeeId}
                managerName={person.displayName}
                computeForbidden={() => forbiddenFor(person.employeeId)}
                mode="create"
                onChanged={onChanged}
                className={`${styles.link} ${styles.addvikar}`}
              />
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
        ) : person.structuralApproverId ? (
          <span className={styles.appr}>
            <span className={styles.rolesK}>Godkendes af</span>
            <span className={styles.apprName}>
              {mgr ? mgr.displayName : person.structuralApproverId}
            </span>
            {/* S86 — info-blue away annotation when the assigned approver is away. */}
            {approverAwayVikarName && (
              <em className={styles.apprVikar} data-testid={`approver-away-${person.employeeId}`}>
                {' '}· pt. {approverAwayVikarName} (vikar)
              </em>
            )}
            {/* S86 — inline "Skift": lazy-mounts the shared ApproverSection
                (ETag-resolved) → its picker reassigns. */}
            <InlineApproverControl
              employeeId={person.employeeId}
              personName={person.displayName}
              currentApproverId={person.structuralApproverId}
              currentApproverName={mgr ? mgr.displayName : person.structuralApproverId}
              computeForbidden={() => forbiddenFor(person.employeeId)}
              trigger="change"
              onChanged={onChanged}
              className={styles.link}
            />
          </span>
        ) : (
          /* S86 — no approver (a non-root, non-orphan line break): inline assign. */
          <InlineApproverControl
            employeeId={person.employeeId}
            personName={person.displayName}
            currentApproverId={null}
            currentApproverName={null}
            computeForbidden={() => forbiddenFor(person.employeeId)}
            trigger="assign"
            onChanged={onChanged}
            className={styles.assignEmpty}
          />
        )}
      </div>
    </div>
  )
}

export function MedarbejderAdministration() {
  const { organizations, loading: orgsLoading } = useOrganizations()
  const { fetchRoster } = useMedarbejderRoster()
  // `fetchUser` is org-independent — pass '' as the placeholder orgId (the hook
  // only uses it for the auto-list fetch, which we don't consume here).
  const { fetchUser } = useOrgUsers('')
  const { toast } = useToast()

  const treeRootOrgs = useMemo(
    () =>
      organizations.filter(
        (o) => o.orgType === 'MAO' || o.orgType === 'ORGANISATION',
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

  // S76b/7604 — the unified EditPersonDrawer state.
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [drawerUser, setDrawerUser] = useState<User | null>(null)
  const [drawerContext, setDrawerContext] = useState<LifecycleContext | undefined>(undefined)
  const [drawerLoading, setDrawerLoading] = useState(false)

  // Default to the first tree root org once loaded
  useEffect(() => {
    if (treeRootOrgs.length > 0 && !selectedTreeRoot) {
      setSelectedTreeRoot(treeRootOrgs[0].orgId)
    }
  }, [treeRootOrgs, selectedTreeRoot])

  // The roster load — extracted so a write's onSaved can re-run it without a
  // Styrelse switch. RESETs the view state (mirrors the original effect).
  const loadRoster = useCallback(
    async (treeRoot: string, resetView: boolean) => {
      if (!treeRoot) return
      setLoading(true)
      setError(null)
      if (resetView) {
        setStatusFilter('alle')
        setQuery('')
        setLevelSel(null)
      }
      const result = await fetchRoster(treeRoot)
      if (result.ok) {
        setPeople(result.data.employees)
        setPendingCountByManager(result.data.pendingCountByManager)
        if (resetView) setCollapsed(defaultCollapsed(result.data.employees))
      } else {
        setPeople([])
        setPendingCountByManager({})
        if (resetView) setCollapsed(new Set())
        setError(result.error)
      }
      setLoading(false)
    },
    [fetchRoster],
  )

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

  // ---- S76b/7604 — drawer wiring ----

  /** Build the tree-derived lifecycle context for the edit drawer so names +
   *  the cycle-forbidden descendant set render without an extra lookup. */
  const buildLifecycleContext = useCallback(
    (person: MedarbejderRosterRow): LifecycleContext => {
      const approver = person.structuralApproverId
        ? byId[person.structuralApproverId]
        : null
      // self + descendants — the PersonPicker forbidden set (cycle prevention).
      const descendantIds = descendantsOf(people, person.employeeId)
      descendantIds.add(person.employeeId)
      return {
        isRoot: person.isRoot,
        currentApproverId: person.structuralApproverId,
        currentApproverName: approver ? approver.displayName : null,
        // S86 — is the assigned approver currently away? (O(1) via the byId index;
        // the roster carries outgoingVikar on the AWAY person's own row, so look it
        // up on the approver). Drives the drawer's "· pt. <vikar> (vikar)".
        currentApproverAwayVikarName: approver?.outgoingVikar?.vikarDisplayName ?? null,
        approvesOthers: childrenOf(people, person.employeeId).length > 0,
        activeVikar: person.outgoingVikar
          ? {
              vikarUserId: person.outgoingVikar.vikarUserId,
              vikarDisplayName: person.outgoingVikar.vikarDisplayName,
              untilDate: person.outgoingVikar.untilDate,
              reason: person.outgoingVikar.reason,
            }
          : null,
        descendantIds,
      }
    },
    [people, byId],
  )

  const handleOpenCreate = () => {
    setDrawerUser(null)
    setDrawerContext(undefined)
    setDrawerOpen(true)
  }

  /** Row-click → open the drawer in EDIT mode. The roster row is not a full
   *  `User` (no version/username/email) — fetch it (mirrors UserManagement's
   *  `fetchUser`) so the drawer's stamdata PUT has its If-Match token, while the
   *  tree-derived `lifecycleContext` lets the approver/vikar names render
   *  immediately. */
  const handleOpenEdit = useCallback(
    async (person: MedarbejderRosterRow) => {
      setDrawerContext(buildLifecycleContext(person))
      setDrawerLoading(true)
      setDrawerOpen(true)
      setDrawerUser(null)
      try {
        const fresh = await fetchUser(person.employeeId)
        setDrawerUser(fresh)
      } catch (err) {
        toast({
          title: 'Fejl',
          description: err instanceof Error ? err.message : String(err),
          variant: 'error',
        })
        setDrawerOpen(false)
      } finally {
        setDrawerLoading(false)
      }
    },
    [buildLifecycleContext, fetchUser, toast],
  )

  /** S86 — open the edit drawer for a person by id (the inline vikar-name link
   *  opens the covering vikar's record). When the id is a roster row we get the
   *  full tree context; otherwise we fetch the User and open with no context. */
  const handleOpenEditById = useCallback(
    async (id: string) => {
      const roster = byId[id]
      if (roster) {
        await handleOpenEdit(roster)
        return
      }
      setDrawerContext(undefined)
      setDrawerLoading(true)
      setDrawerOpen(true)
      setDrawerUser(null)
      try {
        const fresh = await fetchUser(id)
        setDrawerUser(fresh)
      } catch (err) {
        toast({
          title: 'Fejl',
          description: err instanceof Error ? err.message : String(err),
          variant: 'error',
        })
        setDrawerOpen(false)
      } finally {
        setDrawerLoading(false)
      }
    },
    [byId, handleOpenEdit, fetchUser, toast],
  )

  const handleDrawerClose = () => {
    setDrawerOpen(false)
    setDrawerUser(null)
    setDrawerContext(undefined)
  }

  /** A drawer mutation (create / edit / approver / vikar / delete) succeeded —
   *  refetch the roster so the tree + tiles reflect the write. View state is
   *  preserved (no reset) so the admin keeps their place. */
  const handleDrawerSaved = useCallback(() => {
    void loadRoster(selectedTreeRoot, false)
  }, [loadRoster, selectedTreeRoot])

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
        <div className={styles.headActions}>
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
          <button
            type="button"
            className={styles.addBtn}
            onClick={handleOpenCreate}
            data-testid="medarbejder-add"
          >
            Tilføj medarbejder
          </button>
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
                          <button
                            type="button"
                            className={styles.nameBtn}
                            onClick={() => handleOpenEdit(person)}
                            data-testid={`person-edit-${person.employeeId}`}
                          >
                            {person.displayName}
                          </button>
                        </span>
                        <span className={styles.prowTitle}>
                          {person.position}
                          <span className={styles.orgtag}>
                            {person.enhedLabel}
                          </span>
                        </span>
                      </div>
                    </div>
                    {/* S86 — the orphan card's ONE write affordance: inline assign
                        an approver (fixes the broken line) via the shared core. */}
                    <div className={styles.prowAppr}>
                      <InlineApproverControl
                        employeeId={person.employeeId}
                        personName={person.displayName}
                        currentApproverId={null}
                        currentApproverName={null}
                        computeForbidden={() => {
                          const set = descendantsOf(people, person.employeeId)
                          set.add(person.employeeId)
                          return set
                        }}
                        trigger="assign"
                        onChanged={handleDrawerSaved}
                        className={styles.assignEmpty}
                      />
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
                      onEdit={handleOpenEdit}
                      onEditById={handleOpenEditById}
                      onChanged={handleDrawerSaved}
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

      {/* S76b/7604 — the unified EditPersonDrawer (create + edit). In edit mode
          we open the drawer immediately and fetch the full User; the drawer is
          only mounted with a user (or in create mode) once that resolves, so its
          create-vs-edit mode keys correctly off `user` presence. */}
      {drawerOpen && drawerLoading && (
        <div className={styles.spinner} data-testid="drawer-loading">
          <Spinner size="lg" />
        </div>
      )}
      {drawerOpen && !drawerLoading && (
        <EditPersonDrawer
          open={drawerOpen}
          user={drawerUser}
          organizations={organizations}
          defaultOrgId={selectedTreeRoot}
          lifecycleContext={drawerContext}
          onClose={handleDrawerClose}
          onSaved={handleDrawerSaved}
        />
      )}
    </div>
  )
}
