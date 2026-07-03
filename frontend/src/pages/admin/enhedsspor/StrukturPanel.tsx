// SPRINT-107 / TASK-10703 (Enhedsspor Phase 3b-1) — the RIGHT detail panel of the
// merged "Organisation & medarbejdere" admin page: the recursive, foldable
// "Struktur" (units → leaders → their employees → child units), the title block,
// breadcrumb, back/forward, and the derived "Refererer opad til" strip.
//
// DATA = the S106 forest (structure + deep counts, already loaded by the page)
// MERGED with the lazy per-Organisation roster (people). The roster is grouped
// CLIENT-SIDE: per unit, its members are split into leaders (userId ∈ the unit's
// leaderIds) and their direct reports (members whose structuralApproverId == that
// leader); cross-unit exceptions, leaderless units and vikar are surfaced
// READ-ONLY.
//
// S108 (Enhedsspor Phase 3b-2a) added the STRUCTURE-editing surface here, gated by
// role (the S91 discipline IN REVERSE — wire-before-render): the title-block action
// row hosts the UNIT mutations (+ <ChildType> / Rediger / Flyt / Slet → LocalHR) and
// the ORG/MAO mutations (Omdøb / Flyt / Slet / + Organisation / + Ministerområde →
// LocalAdmin/GlobalAdmin per the live floors; the FE gate is UX, the backend
// re-checks). The PEOPLE-mutation surface stays READ-ONLY (S109): NO + Medarbejder /
// cross-unit "Ret" / leaderless "Tildel leder" / vikar-edit / per-row Rediger › /
// person-name edit link — person & leader rows render but are NOT clickable-to-edit.
//
// Styling is tokens-not-hardcoded: per-type accent/tint come from the inherited
// --unit-accent-<type> / --unit-tint-<type> page-root vars; the design's amber /
// vikar status colours are declared as scoped CSS vars on .panel.

import { useEffect, useMemo, useState } from 'react'
import { Button, useToast } from '../../../components/ui'
import { useAuth } from '../../../contexts/AuthContext'
import { hasMinRole } from '../../../lib/roles'
import { useUnitMutations } from '../../../hooks/useUnitMutations'
import { useOrgUsers, type WithEtag, type User } from '../../../hooks/useAdmin'
import type { ForestMaoNode } from '../../../hooks/useForest'
import type {
  RosterResponse,
  RosterRow,
  RosterNameResolutionEntry,
} from '../../../hooks/useRoster'
import {
  buildForestIndex,
  descendantUnitIds,
  pathOf,
  unitsInOrg,
  type StrukturNode,
} from './forestIndex'
import type { SelectedNode } from './OrgStructureTree'
import { UnitDeleteConfirm, UnitDrawer, UnitMoveDialog } from './UnitDrawer'
import { useOrgMutations } from '../../../hooks/useOrgMutations'
import { OrgCreateDialog, OrgDeleteDialog, OrgMoveDialog, OrgRenameDialog } from './OrgStructureDialogs'
import { PersonDrawer } from './PersonDrawer'
import { RetLeaderPicker, type RetLeaderOption } from './RetLeaderPicker'
import { orgsFromForest } from './personDrawerData'
import { useReportingLines } from '../../../hooks/useReportingLines'
import { formatVersionAsIfMatch } from '../../../lib/etag'
import type { LifecycleContext } from '../editPerson/LifecycleSections'
import { InlineApproverControl } from '../editPerson/InlineApproverControl'
import { CHILD, LABEL, ORD, type UnitType } from './typeMaps'
import styles from './StrukturPanel.module.css'

// SPRINT-108 / TASK-10801 — the unit-mutation action the title-block action row
// opens. `create` captures the parent (the selected org/unit) + the derived child
// type; the rest carry the target unit (rename / move / delete operate on the
// selected unit). All four are gated to LocalHR (the action row only renders for a
// permitted actor); the backend re-checks the floor + guards on every call.
type UnitAction =
  | { kind: 'create'; parentNode: StrukturNode; childType: UnitType }
  | { kind: 'edit'; unit: StrukturNode }
  | { kind: 'move'; unit: StrukturNode }
  | { kind: 'delete'; unit: StrukturNode }
  | null

// SPRINT-108 / TASK-10802 — the ORG / MAO structure mutation the title-block action
// row opens (a separate concern from the unit mutations above). `create` captures
// the parent MAO (an Organisation is created beneath it); rename / move / delete
// operate on the selected MAO or Organisation. The role gates differ from the unit
// floor: org create/rename = LocalAdmin; org move/delete = GlobalAdmin. The backend
// re-checks the floor + the structural guards on every call.
type OrgAction =
  | { kind: 'create'; parent: StrukturNode }
  | { kind: 'rename'; node: StrukturNode }
  | { kind: 'move'; node: StrukturNode }
  | { kind: 'delete'; node: StrukturNode; branch: 'blocked' | 'empty'; employeeCount: number }
  | null

interface StrukturPanelProps {
  forest: ForestMaoNode[]
  selected: SelectedNode | null
  /** The lazy per-Organisation roster cache (useRoster). */
  rosterByOrg: Record<string, RosterResponse>
  rosterLoading: boolean
  /** Ask the page to ensure the roster for an Organisation is loaded (lazy). */
  onLoadRoster: (organisationId: string) => void
  /** Navigate to a node (pushes the back/forward history in the page). */
  onNavigate: (node: SelectedNode) => void
  canBack: boolean
  canForward: boolean
  onBack: () => void
  onForward: () => void
  /** SPRINT-108 / TASK-10801 — invoked after a successful unit mutation so the
      page can re-pull the forest (+ the affected Organisation's roster). The page
      wires it only for a permitted actor (the gate is also re-checked here via
      useAuth); a read-only actor never reaches a mutation. */
  onMutated?: (organisationId: string | null) => void | Promise<void>
}

const MONTHS_DA = ['jan', 'feb', 'mar', 'apr', 'maj', 'jun', 'jul', 'aug', 'sep', 'okt', 'nov', 'dec']

// SPRINT-109 / TASK-10904 — the ported period-settlement copy (verbatim from the
// retired MedarbejderAdministration). The reporting period is not served this
// phase, so the label is the same hard-coded literal it carried there.
const PERIOD_LABEL = 'Maj 2026'

function formatDate(iso: string | null): string {
  if (!iso) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!m) return iso
  const day = parseInt(m[3], 10)
  const mon = MONTHS_DA[parseInt(m[2], 10) - 1] ?? ''
  return `${day}. ${mon} ${m[1]}`
}

function initials(name: string): string {
  const parts = (name || '').trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[parts.length - 1]?.[0] ?? '')).toUpperCase()
}

function shortName(name: string): string {
  const parts = (name || '').trim().split(/\s+/)
  return parts.length > 1 ? `${parts[0]} ${parts[parts.length - 1]}` : name
}

const ORG_MEMBER_KEY = '__ORG__'

/** The derived per-roster lookups the recursion needs. */
interface RosterIndex {
  /** rows keyed by unitId, plus ORG_MEMBER_KEY for the Organisation-homed
      (unit-less) rows. */
  rowsByUnit: Map<string, RosterRow[]>
  rowById: Map<string, RosterRow>
  /** a unit's aggregated designated leader ids (from any member row). */
  leaderIdsByUnit: Map<string, string[]>
  /** userId → the display names of the leaders this person stands in for
      (the inverse of outgoingVikar within the loaded set). */
  coveringByUser: Map<string, string[]>
  nameResolution: Record<string, RosterNameResolutionEntry>
}

function buildRosterIndex(roster: RosterResponse | undefined): RosterIndex {
  const rowsByUnit = new Map<string, RosterRow[]>()
  const rowById = new Map<string, RosterRow>()
  const leaderIdsByUnit = new Map<string, string[]>()
  const coveringByUser = new Map<string, string[]>()

  for (const row of roster?.employees ?? []) {
    const key = row.unitId ?? ORG_MEMBER_KEY
    const bucket = rowsByUnit.get(key)
    if (bucket) bucket.push(row)
    else rowsByUnit.set(key, [row])

    rowById.set(row.employeeId, row)
    if (row.unitId) leaderIdsByUnit.set(row.unitId, row.leaderIds)

    if (row.outgoingVikar) {
      const covered = coveringByUser.get(row.outgoingVikar.vikarUserId)
      if (covered) covered.push(row.displayName)
      else coveringByUser.set(row.outgoingVikar.vikarUserId, [row.displayName])
    }
  }

  return { rowsByUnit, rowById, leaderIdsByUnit, coveringByUser, nameResolution: roster?.nameResolution ?? {} }
}

// ── The flat ordered render-node list (the design's walkUnit) ──────────────────
type RenderNode =
  | { t: 'unit'; key: string; node: StrukturNode; depth: number; open: boolean; hasContent: boolean; leaderNames: string; count: number }
  | { t: 'medHeader'; key: string; unitId: string; depth: number; count: number; closed: boolean }
  | { t: 'leader'; key: string; row: RosterRow; depth: number; reportCount: number; collapsed: boolean; hasReports: boolean; coveringNames: string[] }
  | { t: 'employee'; key: string; row: RosterRow; depth: number; variant: 'report' | 'flat' | 'external'; externalLeaderName?: string; coveringNames: string[] }
  | { t: 'note'; key: string; depth: number; text: string; unit: StrukturNode }

function indent(depth: number, extra = 0): number {
  return 14 + depth * 20 + extra
}

// SPRINT-109 / TASK-10903 — the cross-unit "Ret" failure messages. The reassign
// hits POST /api/admin/reporting-lines (the same endpoint the drawer's
// ApproverSection uses); the backend re-checks the scope/cycle/concurrency, so we
// map the honest statuses (412 stale edge / 409 already-assigned / 422+400
// scope-or-cycle) to user-facing Danish.
function retMessageFor(status: number): string {
  switch (status) {
    case 412:
      return 'Ledelseslinjen er ændret af en anden. Genindlæs og prøv igen.'
    case 409:
      return 'Der findes allerede en godkender. Genindlæs og prøv igen.'
    case 400:
    case 422:
      return 'Godkenderen skal være i samme organisation og må ikke skabe en cyklus.'
    case 403:
      return 'Du har ikke rettigheder til denne handling.'
    case 404:
      return 'Medarbejderen findes ikke længere. Genindlæs siden.'
    default:
      return 'Noget gik galt. Prøv igen.'
  }
}

export function StrukturPanel({
  forest,
  selected,
  rosterByOrg,
  rosterLoading,
  onLoadRoster,
  onNavigate,
  canBack,
  canForward,
  onBack,
  onForward,
  onMutated,
}: StrukturPanelProps) {
  const index = useMemo(() => buildForestIndex(forest), [forest])
  const selectedNode = selected ? index.byId.get(selected.id) ?? null : null
  const organisationId = selectedNode?.organisationId ?? null

  // Per-view UI state (independent pieces — the design's treeOpen / medClosed /
  // lCollapse / showPeople). All pure view-state, never mutations.
  const [treeOpen, setTreeOpen] = useState<Record<string, boolean>>({})
  const [medClosed, setMedClosed] = useState<Record<string, boolean>>({})
  const [lCollapse, setLCollapse] = useState<Record<string, boolean>>({})
  const [showPeople, setShowPeople] = useState(true)
  // SPRINT-109 / TASK-10904 — the ported period-settlement filter (the
  // MedarbejderAdministration status tiles). 'none' = no filter; 'indsend' = OPEN
  // (not-submitted, non-orphan) people; 'godkend' = managers with a pending
  // (SUBMITTED-awaiting-approval) period. Scoped to the selected Organisation's
  // loaded roster; click-to-filter narrows the Struktur people.
  const [settlementFilter, setSettlementFilter] = useState<'none' | 'indsend' | 'godkend'>('none')

  // ── SPRINT-108 / TASK-10803 — the capability-gating spine. The unit
  // affordances render ONLY for LocalHR+ (the LIVE S104 floor); the backend is
  // the enforcer, this gate is UX. useAuth throws outside an AuthProvider — the
  // S107 suites that render this panel now mock contexts/AuthContext.
  const { role } = useAuth()
  const canEditUnits = hasMinRole(role, 'LocalHR')
  const { toast } = useToast()
  const mutations = useUnitMutations()
  const [action, setAction] = useState<UnitAction>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  // ── SPRINT-108/109 — the ORG / MAO mutation gates + state. PER-NODE-KIND
  // (S109 / TASK-10904): the MAO-node "+ Organisation" create + the MAO-node "Omdøb"
  // both scope-check the MAO on the backend (ValidateOrgAccessAsync(MAO)) → a scoped
  // LocalAdmin has NO MAO scope → those would be DEAD buttons. So they gate by
  // GlobalAdmin. The Organisation-node "Omdøb" stays LocalAdmin (a scoped LocalAdmin
  // owns their Organisation); org move/delete = GlobalAdmin. The node-aware split
  // (canCreateOrg / canRenameOrg) is finalised once selectedNode is known (below,
  // after `isMao`). The FE gate is UX — the backend re-checks the floor on every call.
  const isGlobalAdminRole = hasMinRole(role, 'GlobalAdmin')
  const isLocalAdminRole = hasMinRole(role, 'LocalAdmin')
  const canMoveOrg = isGlobalAdminRole
  const canDeleteOrg = isGlobalAdminRole
  const orgMutations = useOrgMutations()
  const [orgAction, setOrgAction] = useState<OrgAction>(null)
  const [orgBusy, setOrgBusy] = useState(false)
  const [orgError, setOrgError] = useState<string | null>(null)

  // ── SPRINT-109 / TASK-10901/10902 — the PEOPLE-mutation surface (the S107/S108
  // inversion completes here). The people-edit floor is LocalHR (== canEditUnits);
  // the backend re-checks every person mutation. The drawer routes its save through
  // the 4-case usePlacement wrapper. The org list + Placering options derive from
  // the forest; a RosterRow is not a full User → fetchUser supplies the edit etag.
  const organizations = useMemo(() => orgsFromForest(forest), [forest])
  const { fetchUser } = useOrgUsers('')
  const { assignManager } = useReportingLines()
  const [personDrawer, setPersonDrawer] = useState<
    | { mode: 'create'; orgId: string; unitId: string | null }
    | { mode: 'edit'; row: RosterRow }
    | null
  >(null)
  const [editUser, setEditUser] = useState<WithEtag<User> | null>(null)
  const [personLoading, setPersonLoading] = useState(false)

  // ── SPRINT-109 / TASK-10903 — cross-unit "Ret" picker state. Opened only when a
  // cross-unit-exception member's OWN unit has MORE THAN ONE peer leader (the
  // one-leader case is one-click; the picker NEVER auto-picks an arbitrary first).
  const [retPicker, setRetPicker] = useState<
    { row: RosterRow; unitName: string | null; leaders: RetLeaderOption[] } | null
  >(null)
  const [retBusy, setRetBusy] = useState(false)
  const [retError, setRetError] = useState<string | null>(null)

  // Lazily ensure the selected node's Organisation roster is loaded.
  useEffect(() => {
    if (organisationId) onLoadRoster(organisationId)
  }, [organisationId, onLoadRoster])

  // SPRINT-109 / TASK-10904 — reset the settlement filter when the Organisation
  // changes (a stale filter from another org could otherwise hide every row).
  useEffect(() => {
    setSettlementFilter('none')
  }, [organisationId])

  const roster = organisationId ? rosterByOrg[organisationId] : undefined
  const rosterIndex = useMemo(() => buildRosterIndex(roster), [roster])

  if (!selectedNode) {
    return (
      <div className={styles.panel} data-testid="struktur-panel">
        <p className={styles.empty}>Vælg en enhed i strukturen til venstre.</p>
      </div>
    )
  }

  // ── resolvers ───────────────────────────────────────────────────────────────
  const resolveName = (id: string): string =>
    rosterIndex.rowById.get(id)?.displayName ?? rosterIndex.nameResolution[id]?.displayName ?? '—'

  const resolveWhere = (id: string): string => {
    const r = rosterIndex.rowById.get(id)
    if (r) return [r.position, r.unitName].filter(Boolean).join(' · ')
    const nr = rosterIndex.nameResolution[id]
    if (nr) return [nr.position, nr.unitName].filter(Boolean).join(' · ')
    return ''
  }

  const membersOf = (node: StrukturNode): RosterRow[] =>
    rosterIndex.rowsByUnit.get(node.kind === 'unit' ? node.id : ORG_MEMBER_KEY) ?? []

  const leaderIdsOf = (node: StrukturNode): string[] =>
    node.kind === 'unit' ? rosterIndex.leaderIdsByUnit.get(node.id) ?? [] : []

  const leaderNamesOf = (node: StrukturNode): string => {
    const lset = new Set(leaderIdsOf(node))
    const names = membersOf(node)
      .filter((m) => lset.has(m.employeeId))
      .map((m) => shortName(m.displayName))
    return names.length ? names.join(', ') : '—'
  }

  const toSelected = (node: StrukturNode): SelectedNode => ({
    id: node.id,
    kind: node.kind,
    name: node.name,
    type: node.type,
  })

  // ── SPRINT-108 / TASK-10801 — the gated unit-mutation surface ─────────────────
  // The "+ <ChildType>" derivation: an Organisation creates a top-level `direktion`
  // (parent_unit_id NULL); a unit creates CHILD[type]; a MAO (would create an
  // Organisation — an ORG mutation, a later task) and a leaf `enhed` (no child) are
  // DISABLED. The label always shows so the row keeps its shape.
  const createInfo: { type: UnitType | null; label: string; canCreate: boolean } =
    selectedNode.kind === 'mao'
      ? { type: 'organisation', label: LABEL.organisation, canCreate: false }
      : selectedNode.kind === 'organisation'
        ? { type: 'direktion', label: LABEL.direktion, canCreate: true }
        : (() => {
            const child = CHILD[selectedNode.type]
            return {
              type: child,
              label: child ? LABEL[child] : LABEL[selectedNode.type],
              canCreate: child !== null,
            }
          })()

  const closeAction = () => {
    setAction(null)
    setActionError(null)
  }

  const afterMutation = async (successTitle: string, successBody: string) => {
    setBusy(false)
    setAction(null)
    setActionError(null)
    toast({ title: successTitle, description: successBody, variant: 'success' })
    await onMutated?.(organisationId)
  }

  const openCreate = () => {
    if (!createInfo.canCreate || !createInfo.type) return
    setActionError(null)
    setAction({ kind: 'create', parentNode: selectedNode, childType: createInfo.type })
  }
  // Open the unit edit drawer (rename + Ledere checkboxes) for ANY unit node — the
  // title-block "Rediger" targets the selected unit; the leaderless "Tildel leder"
  // targets the child unit that has no leader (TASK-10903).
  const openEditUnit = (unit: StrukturNode) => {
    setActionError(null)
    setAction({ kind: 'edit', unit })
  }
  const openEdit = () => openEditUnit(selectedNode)
  const openMove = () => {
    setActionError(null)
    setAction({ kind: 'move', unit: selectedNode })
  }
  const openDelete = () => {
    setActionError(null)
    setAction({ kind: 'delete', unit: selectedNode })
  }

  const submitCreate = async (name: string) => {
    if (action?.kind !== 'create' || !action.parentNode.organisationId) return
    setBusy(true)
    setActionError(null)
    const res = await mutations.createUnit({
      organisationId: action.parentNode.organisationId,
      parentUnitId: action.parentNode.kind === 'unit' ? action.parentNode.id : null,
      type: action.childType,
      name,
    })
    if (res.ok) await afterMutation('Oprettet', 'Enheden er oprettet')
    else {
      setBusy(false)
      setActionError(res.error)
    }
  }

  const submitEdit = async (name: string, addLeaderIds: string[], removeLeaderIds: string[]) => {
    if (action?.kind !== 'edit') return
    const unit = action.unit
    setBusy(true)
    setActionError(null)
    // S108 Step-7a (Reviewer): these are separate non-atomic calls. To avoid a
    // committed-rename-then-stale-version-retry-412 trap, do the leader ops FIRST
    // (they write `unit_leaders`, NOT `units` → they never bump `units.version`, so
    // they cannot invalidate the rename's If-Match) and the version-bumping rename
    // LAST. On ANY mid-sequence failure, if something already committed, refetch so
    // the committed change is reflected and a retry re-derives against fresh state.
    let committed = false
    const failWith = async (msg: string) => {
      setBusy(false)
      if (committed) await onMutated?.(organisationId)
      setActionError(msg)
    }
    // 1) leader designations (the diff of the checkboxes; path-param remove).
    for (const userId of addLeaderIds) {
      const res = await mutations.designateLeader(unit.id, userId)
      if (!res.ok) return failWith(res.error)
      committed = true
    }
    for (const userId of removeLeaderIds) {
      const res = await mutations.removeLeader(unit.id, userId)
      if (!res.ok) return failWith(res.error)
      committed = true
    }
    // 2) rename LAST (If-Match) only if the name actually changed.
    if (name !== unit.name) {
      if (unit.version == null) return failWith('Kan ikke omdøbe enheden. Genindlæs siden.')
      const res = await mutations.renameUnit(unit.id, name, unit.version)
      if (!res.ok) return failWith(res.error)
    }
    await afterMutation('Gemt', 'Enheden er opdateret')
  }

  const submitMove = async (newParentUnitId: string | null) => {
    if (action?.kind !== 'move') return
    const unit = action.unit
    if (unit.version == null) {
      setActionError('Kan ikke flytte enheden. Genindlæs siden.')
      return
    }
    setBusy(true)
    setActionError(null)
    const res = await mutations.moveUnit(unit.id, newParentUnitId, unit.version)
    if (res.ok) await afterMutation('Flyttet', 'Enheden er flyttet')
    else {
      setBusy(false)
      setActionError(res.error)
    }
  }

  const submitDelete = async () => {
    if (action?.kind !== 'delete') return
    const unit = action.unit
    if (unit.version == null) {
      setActionError('Kan ikke slette enheden. Genindlæs siden.')
      return
    }
    setBusy(true)
    setActionError(null)
    const res = await mutations.deleteUnit(unit.id, unit.version)
    if (res.ok) await afterMutation('Slettet', 'Enheden er slettet')
    else {
      setBusy(false)
      setActionError(res.error)
    }
  }

  // The move-picker candidates: every unit in the SAME Organisation, minus self,
  // minus descendants, minus same-or-deeper TYPE-RANK targets (a valid parent must
  // be strictly shallower — the S104 partial-rank rule). "→ Rod" is added in the
  // dialog itself.
  const moveTargets = (() => {
    if (action?.kind !== 'move') return []
    const movingUnit = action.unit
    const orgId = movingUnit.organisationId
    if (!orgId) return []
    const movingOrd = ORD[movingUnit.type]
    const excluded = new Set([movingUnit.id, ...descendantUnitIds(movingUnit)])
    return unitsInOrg(index, orgId)
      .filter((u) => !excluded.has(u.id) && ORD[u.type] < movingOrd)
      .map((u) => ({ id: u.id, name: u.name, type: u.type }))
  })()

  // ── SPRINT-108 / TASK-10802 — the gated ORG / MAO mutation surface ────────────
  const closeOrgAction = () => {
    setOrgAction(null)
    setOrgError(null)
  }

  const afterOrgMutation = async (successTitle: string, successBody: string) => {
    setOrgBusy(false)
    setOrgAction(null)
    setOrgError(null)
    toast({ title: successTitle, description: successBody, variant: 'success' })
    // Org structure changes the forest (placement + roll-up counts), not roster
    // membership → refetch the forest only (null skips the per-org roster re-pull).
    await onMutated?.(null)
  }

  const openOrgCreate = () => {
    setOrgError(null)
    // Only reachable on a MAO node (the "+ Organisation" create button) — a MAO's
    // child is always an Organisation.
    setOrgAction({ kind: 'create', parent: selectedNode })
  }
  const openOrgRename = () => {
    setOrgError(null)
    setOrgAction({ kind: 'rename', node: selectedNode })
  }
  const openOrgMove = () => {
    setOrgError(null)
    setOrgAction({ kind: 'move', node: selectedNode })
  }
  const openOrgDelete = () => {
    setOrgError(null)
    // Open optimistically on the forest roll-up count; the DELETE is the gate — a
    // 422 flips the dialog to its BLOCKED branch with the authoritative count.
    setOrgAction({
      kind: 'delete',
      node: selectedNode,
      branch: selectedNode.memberCount > 0 ? 'blocked' : 'empty',
      employeeCount: selectedNode.memberCount,
    })
  }

  // The org move-target candidates: every visible MAO except the org's current
  // parent (the target must be an active MAO — the backend re-checks).
  const orgMoveTargets =
    orgAction?.kind === 'move'
      ? forest
          .filter((m) => m.orgId !== orgAction.node.parentId)
          .map((m) => ({ orgId: m.orgId, orgName: m.orgName }))
      : []

  const submitOrgCreate = async (name: string) => {
    if (orgAction?.kind !== 'create') return
    setOrgBusy(true)
    setOrgError(null)
    const res = await orgMutations.createOrg({
      orgName: name,
      orgType: 'ORGANISATION',
      parentOrgId: orgAction.parent.id,
    })
    if (res.ok) await afterOrgMutation('Oprettet', 'Organisation oprettet')
    else {
      setOrgBusy(false)
      setOrgError(res.error)
    }
  }

  const submitOrgRename = async (name: string) => {
    if (orgAction?.kind !== 'rename') return
    setOrgBusy(true)
    setOrgError(null)
    const res = await orgMutations.renameOrg(orgAction.node.id, name)
    if (res.ok) await afterOrgMutation('Gemt', 'Navn opdateret')
    else {
      setOrgBusy(false)
      setOrgError(res.error)
    }
  }

  const submitOrgMove = async (newParentOrgId: string) => {
    if (orgAction?.kind !== 'move') return
    setOrgBusy(true)
    setOrgError(null)
    const res = await orgMutations.moveOrg(orgAction.node.id, newParentOrgId)
    if (res.ok) await afterOrgMutation('Flyttet', 'Organisation flyttet')
    else {
      setOrgBusy(false)
      setOrgError(res.error)
    }
  }

  const submitOrgDelete = async () => {
    if (orgAction?.kind !== 'delete') return
    const node = orgAction.node
    setOrgBusy(true)
    setOrgError(null)
    const res = await orgMutations.deleteOrg(node.id)
    if (res.ok) {
      await afterOrgMutation('Slettet', node.kind === 'mao' ? 'Ministerområde slettet' : 'Organisation slettet')
    } else if (res.status === 422) {
      // The server is the gate: a 422 flips to the BLOCKED branch with the
      // authoritative count (the optimistic forest count may have been stale).
      setOrgBusy(false)
      setOrgAction({
        kind: 'delete',
        node,
        branch: 'blocked',
        employeeCount: res.employeeCount ?? node.memberCount,
      })
    } else {
      setOrgBusy(false)
      setOrgError(res.error)
    }
  }

  // ── SPRINT-109 — the gated PEOPLE-mutation surface ────────────────────────────
  // Self + descendants (the cycle-prevention forbidden set), derived from the
  // loaded roster's structuralApproverId graph (mirrors the old page's
  // descendantsOf; the server ALSO enforces cycle prevention via excludeEmployeeId).
  const descendantsOfPerson = (employeeId: string): Set<string> => {
    const out = new Set<string>([employeeId])
    const all = roster?.employees ?? []
    let frontier = [employeeId]
    while (frontier.length) {
      const next: string[] = []
      for (const r of all) {
        if (r.structuralApproverId && frontier.includes(r.structuralApproverId) && !out.has(r.employeeId)) {
          out.add(r.employeeId)
          next.push(r.employeeId)
        }
      }
      frontier = next
    }
    return out
  }

  const buildLifecycleContext = (row: RosterRow): LifecycleContext => {
    const approverRow = row.structuralApproverId ? rosterIndex.rowById.get(row.structuralApproverId) : null
    const approvesOthers = (roster?.employees ?? []).some((r) => r.structuralApproverId === row.employeeId)
    return {
      isRoot: row.isRoot,
      currentApproverId: row.structuralApproverId,
      currentApproverName: row.structuralApproverId ? resolveName(row.structuralApproverId) : null,
      currentApproverAwayVikarName: approverRow?.outgoingVikar?.vikarDisplayName ?? null,
      approvesOthers,
      activeVikar: row.outgoingVikar
        ? {
            vikarUserId: row.outgoingVikar.vikarUserId,
            vikarDisplayName: row.outgoingVikar.vikarDisplayName,
            untilDate: row.outgoingVikar.untilDate,
            reason: row.outgoingVikar.reason,
          }
        : null,
      descendantIds: descendantsOfPerson(row.employeeId),
    }
  }

  const openCreatePerson = () => {
    if (selectedNode.kind !== 'unit' || !selectedNode.organisationId) return
    setEditUser(null)
    setPersonDrawer({ mode: 'create', orgId: selectedNode.organisationId, unitId: selectedNode.id })
  }

  const openEditPerson = async (row: RosterRow) => {
    setEditUser(null)
    setPersonLoading(true)
    setPersonDrawer({ mode: 'edit', row })
    try {
      const fresh = await fetchUser(row.employeeId)
      setEditUser(fresh)
    } catch (err) {
      toast({ title: 'Fejl', description: err instanceof Error ? err.message : String(err), variant: 'error' })
      setPersonDrawer(null)
    } finally {
      setPersonLoading(false)
    }
  }

  const closePersonDrawer = () => {
    setPersonDrawer(null)
    setEditUser(null)
  }

  const onPersonSaved = async (orgId: string | null) => {
    await onMutated?.(orgId)
  }

  // ── SPRINT-109 / TASK-10903 — cross-unit "Ret" + leaderless "Tildel leder" ─────
  // "Ret" reassigns a cross-unit-exception member's PRIMARY reporting edge to a
  // leader of THEIR OWN unit (the roster row carries that unit's leaderIds + the
  // active edge's version). EXACTLY ONE own-unit leader → one-click; several peer
  // leaders → the pre-filtered picker (NEVER an arbitrary first pick). The
  // create-vs-supersede etag (S99): a non-null primaryReportingLineVersion →
  // If-Match (supersede the active edge); null → If-None-Match:* (create — a
  // root/orphan with no active PRIMARY edge). This hits the SAME
  // POST /api/admin/reporting-lines the drawer's ApproverSection uses (P7).
  const todayIso = new Date().toISOString().slice(0, 10)

  const submitRet = async (row: RosterRow, managerId: string) => {
    setRetBusy(true)
    setRetError(null)
    const ifMatch =
      row.primaryReportingLineVersion != null
        ? formatVersionAsIfMatch(row.primaryReportingLineVersion)
        : undefined
    const result = await assignManager(
      { employeeId: row.employeeId, managerId, effectiveFrom: todayIso },
      ifMatch,
    )
    setRetBusy(false)
    if (result.ok) {
      setRetPicker(null)
      setRetError(null)
      toast({
        title: 'Leder rettet',
        description: 'Medarbejderen refererer nu til en leder i egen enhed.',
        variant: 'success',
      })
      await onMutated?.(organisationId)
    } else {
      const msg = retMessageFor(result.status)
      setRetError(msg)
      // The one-click path has no inline surface → surface via toast; the picker
      // shows the message inline (and stays open for a retry).
      if (!retPicker) toast({ title: 'Handlingen mislykkedes', description: msg, variant: 'error' })
    }
  }

  const onRetClick = (row: RosterRow) => {
    setRetError(null)
    const leaders: RetLeaderOption[] = row.leaderIds.map((id) => ({ id, name: resolveName(id) }))
    if (leaders.length === 1) {
      void submitRet(row, leaders[0].id)
    } else {
      setRetPicker({ row, unitName: row.unitName, leaders })
    }
  }

  const closeRetPicker = () => {
    setRetPicker(null)
    setRetError(null)
  }

  // ── SPRINT-109 / TASK-10904 — the ported period-settlement overview (status
  // tiles + the "mangler godkender" orphan card), scoped to THIS Organisation's
  // loaded roster. Shown on an Organisation node (the design's per-Organisation
  // settlement view); the tiles click-to-filter the Struktur people. ──────────────
  const showSettlement = selectedNode.kind === 'organisation' && !!roster && roster.employees.length > 0
  const settlementPending = roster?.pendingCountByManager ?? {}
  const indsendCount = (roster?.employees ?? []).filter((p) => p.periodStatus === 'OPEN' && !p.isOrphan).length
  const godkendCount = Object.keys(settlementPending).length
  const orphanPeople = (roster?.employees ?? []).filter((p) => p.isOrphan)

  // The active filter's per-person predicate (narrows the Struktur people in walkUnit).
  const matchesSettlement = (row: RosterRow): boolean => {
    if (settlementFilter === 'indsend') return row.periodStatus === 'OPEN' && !row.isOrphan
    if (settlementFilter === 'godkend') return Object.prototype.hasOwnProperty.call(settlementPending, row.employeeId)
    return true
  }

  const toggleSettlement = (f: 'indsend' | 'godkend') => {
    const next = settlementFilter === f ? 'none' : f
    setSettlementFilter(next)
    if (next !== 'none') {
      // Reveal nested people so the filter is visible across the whole Struktur.
      setShowPeople(true)
      setTreeOpen((prev) => {
        const out = { ...prev }
        for (const id of descendantUnitIds(selectedNode)) out[id] = true
        return out
      })
    }
  }

  // "Vis medarbejdere" must REVEAL people, not merely un-hide member rows inside
  // already-expanded units: since S114 every person is unit-homed, an Organisation/
  // MAO node has zero direct members, so without the descendant expansion the
  // toggle appears dead at the top levels (owner report). Same reveal the
  // settlement filters perform above; "Skjul" only hides member rows and leaves
  // the unit expansion state alone ("Vis org./Skjul org." owns that).
  const togglePeople = () => {
    const next = !showPeople
    if (next) {
      setTreeOpen((prev) => {
        const out = { ...prev }
        for (const id of descendantUnitIds(selectedNode)) out[id] = true
        return out
      })
    }
    setShowPeople(next)
  }

  // ── the recursive node list ──────────────────────────────────────────────────
  const nodes: RenderNode[] = []
  const walkUnit = (node: StrukturNode, depth: number) => {
    const members = settlementFilter === 'none' ? membersOf(node) : membersOf(node).filter(matchesSettlement)
    const leaderIdSet = new Set(leaderIdsOf(node))
    const leaderRows = members.filter((m) => leaderIdSet.has(m.employeeId))
    const nonLeaders = members.filter((m) => !leaderIdSet.has(m.employeeId))

    if (showPeople && members.length) {
      const closed = !!medClosed[node.id]
      nodes.push({ t: 'medHeader', key: `med:${node.id}`, unitId: node.id, depth, count: members.length, closed })

      if (!closed) {
        const reported = new Set<string>()
        for (const L of leaderRows) {
          const reps = nonLeaders.filter((m) => m.structuralApproverId === L.employeeId)
          reps.forEach((r) => reported.add(r.employeeId))
          const collapsed = !!lCollapse[L.employeeId]
          nodes.push({
            t: 'leader',
            key: `l:${L.employeeId}`,
            row: L,
            depth: depth + 1,
            reportCount: reps.length,
            collapsed,
            hasReports: reps.length > 0,
            coveringNames: rosterIndex.coveringByUser.get(L.employeeId) ?? [],
          })
          if (!collapsed) {
            reps.forEach((r) =>
              nodes.push({
                t: 'employee',
                key: `e:${r.employeeId}`,
                row: r,
                depth: depth + 1,
                variant: 'report',
                coveringNames: rosterIndex.coveringByUser.get(r.employeeId) ?? [],
              }),
            )
          }
        }

        const remaining = nonLeaders.filter((m) => !reported.has(m.employeeId))
        if (leaderRows.length === 0) {
          // Leaderless unit (members but no designated leaders) → READ-ONLY amber
          // note (NO "Tildel leder" — S108). Only for real units, not the
          // Organisation-homed group.
          if (node.kind === 'unit') {
            const upNames = [
              ...new Set(
                remaining
                  .map((m) => m.structuralApproverId)
                  .filter((id): id is string => !!id)
                  .map(resolveName),
              ),
            ]
            const n = remaining.length
            const refers = n === 1 ? 'medarbejder refererer' : 'medarbejdere refererer'
            const til = upNames.length ? ` til ${upNames.join(', ')}` : ''
            nodes.push({
              t: 'note',
              key: `note:${node.id}`,
              depth: depth + 1,
              text: `Ingen leder i enheden — ${n} ${refers} opad${til}.`,
              unit: node,
            })
          }
          remaining.forEach((r) =>
            nodes.push({
              t: 'employee',
              key: `e:${r.employeeId}`,
              row: r,
              depth: depth + 1,
              variant: 'flat',
              coveringNames: rosterIndex.coveringByUser.get(r.employeeId) ?? [],
            }),
          )
        } else {
          remaining.forEach((m) => {
            const external = m.structuralApproverId != null && !leaderIdSet.has(m.structuralApproverId)
            nodes.push({
              t: 'employee',
              key: `e:${m.employeeId}`,
              row: m,
              depth: depth + 1,
              variant: external ? 'external' : 'flat',
              externalLeaderName: external ? resolveName(m.structuralApproverId!) : undefined,
              coveringNames: rosterIndex.coveringByUser.get(m.employeeId) ?? [],
            })
          })
        }
      }
    }

    for (const child of node.childUnits) {
      const hasContent = child.childUnits.length > 0 || child.memberCount > 0
      const open = !!treeOpen[child.id]
      nodes.push({
        t: 'unit',
        key: `u:${child.id}`,
        node: child,
        depth,
        open,
        hasContent,
        leaderNames: leaderNamesOf(child),
        count: child.memberCount,
      })
      if (open) walkUnit(child, depth + 1)
    }
  }
  walkUnit(selectedNode, 0)

  // ── derived header bits ──────────────────────────────────────────────────────
  const path = pathOf(index, selectedNode.id)
  const isMao = selectedNode.kind === 'mao'

  // PER-NODE-KIND org gates (S109 / TASK-10904): the MAO-node create ("+ Organisation")
  // + rename ("Omdøb") scope the MAO → GlobalAdmin; the Organisation-node rename is
  // owned by the scoped LocalAdmin → LocalAdmin.
  const canCreateOrg = isGlobalAdminRole
  const canRenameOrg = isMao ? isGlobalAdminRole : isLocalAdminRole

  // "Refererer opad til" — the selected unit's leaders' distinct upward refs.
  const selLeaderIdSet = new Set(leaderIdsOf(selectedNode))
  const upRefIds = [
    ...new Set(
      membersOf(selectedNode)
        .filter((m) => selLeaderIdSet.has(m.employeeId))
        .map((m) => m.structuralApproverId)
        .filter((id): id is string => !!id),
    ),
  ]

  const childCount = selectedNode.childUnits.length
  const strTitle = isMao ? 'Organisationer' : 'Struktur'
  const strCountLabel = isMao
    ? `${childCount} ${childCount === 1 ? 'organisation' : 'organisationer'}`
    : `${selectedNode.memberCount} ${selectedNode.memberCount === 1 ? 'medarbejder' : 'medarbejdere'} · ` +
      `${childCount} ${childCount === 1 ? 'underenhed' : 'underenheder'}`

  const descendants = descendantUnitIds(selectedNode)
  const anyOpen = descendants.some((id) => treeOpen[id])
  const expandLabel = anyOpen ? 'Skjul org.' : 'Vis org.'
  const expandDisabled = descendants.length === 0
  const peopleLabel = showPeople ? 'Skjul medarbejdere' : 'Vis medarbejdere'

  const toggleExpandAll = () => {
    setTreeOpen((prev) => {
      const next = { ...prev }
      for (const id of descendants) {
        if (anyOpen) delete next[id]
        else next[id] = true
      }
      return next
    })
  }

  const waitingForRoster = !!organisationId && !roster && rosterLoading

  const chipStyle = (type: UnitType) => ({
    color: `var(--unit-accent-${type})`,
    background: `var(--unit-tint-${type})`,
  })

  // ── render ───────────────────────────────────────────────────────────────────
  return (
    <div className={styles.panel} data-testid="struktur-panel">
      {/* a. back / forward */}
      <div className={styles.navRow}>
        <Button variant="ghost" size="sm" disabled={!canBack} onClick={onBack} data-testid="nav-back">
          ‹ Tilbage
        </Button>
        <Button variant="ghost" size="sm" disabled={!canForward} onClick={onForward} data-testid="nav-forward">
          Frem ›
        </Button>
      </div>

      {/* b. breadcrumb path (navigation only) */}
      <nav className={styles.breadcrumb} aria-label="Sti" data-testid="breadcrumb">
        {path.map((node, i) => {
          const isLast = i === path.length - 1
          return (
            <span key={node.id} className={styles.crumbWrap}>
              {i > 0 && <span className={styles.crumbSep} aria-hidden="true"> › </span>}
              {isLast ? (
                <span className={styles.crumbCurrent} aria-current="page">{node.name}</span>
              ) : (
                <button
                  type="button"
                  className={styles.crumb}
                  data-testid={`crumb-${node.id}`}
                  onClick={() => onNavigate(toSelected(node))}
                >
                  {node.name}
                </button>
              )}
            </span>
          )
        })}
      </nav>

      {/* c. title block (type chip + name) */}
      <div className={styles.titleBlock}>
        <span className={styles.typeChip} style={chipStyle(selectedNode.type)} data-testid="title-type-chip">
          {LABEL[selectedNode.type]}
        </span>
        <h1 className={styles.title} data-testid="title-name">{selectedNode.name}</h1>
      </div>

      {/* c2. action row — the gated UNIT structure mutations (TASK-10801/10803).
          Rendered ONLY for LocalHR+. "+ <ChildType>" creates a child unit (an
          Organisation seeds a top-level direktion); Rediger / Flyt / Slet operate
          on the selected unit. People mutations (+ Medarbejder / Ret / …) are S109.
          The org/MAO structure mutations (rename/move/delete) are a separate task. */}
      {canEditUnits && (
        <div className={styles.actionRow} data-testid="unit-action-row">
          {/* The create button. On an Organisation / unit it is the UNIT create
              (createInfo, LocalHR — TASK-10801). On a MAO it becomes the ORG create
              ("+ Organisation", LocalAdmin — TASK-10802): disabled below that floor,
              so a LocalHR sees the same disabled placeholder as before. */}
          <Button
            variant="secondary"
            size="sm"
            disabled={isMao ? !canCreateOrg : !createInfo.canCreate}
            onClick={isMao ? openOrgCreate : openCreate}
            data-testid="unit-action-create"
          >
            + {createInfo.label}
          </Button>
          {selectedNode.kind === 'unit' && (
            <>
              {/* TASK-10901 — the people-create entry (a person homes at the unit's
                  Organisation with this unit as the default Placering). LocalHR floor
                  (== canEditUnits); the backend re-checks. */}
              <Button variant="primary" size="sm" onClick={openCreatePerson} data-testid="person-action-create">
                + Medarbejder
              </Button>
              <span className={styles.actionDivider} aria-hidden="true" />
              <Button variant="secondary" size="sm" onClick={openEdit} data-testid="unit-action-edit">
                Rediger
              </Button>
              <Button variant="secondary" size="sm" onClick={openMove} data-testid="unit-action-move">
                Flyt
              </Button>
              <Button variant="ghost" size="sm" onClick={openDelete} data-testid="unit-action-delete">
                Slet
              </Button>
            </>
          )}
          {/* TASK-10802 — the gated ORG / MAO structure mutations. On a MAO: Omdøb
              (LocalAdmin) + Slet (GlobalAdmin) — a MAO is a root → no Flyt; the
              create above is the org-create. On an Organisation: Omdøb (LocalAdmin)
              + Flyt (GlobalAdmin) + Slet (GlobalAdmin); the create above stays the
              UNIT "+ Direktion". */}
          {(selectedNode.kind === 'mao' || selectedNode.kind === 'organisation') &&
            (canRenameOrg || canMoveOrg || canDeleteOrg) && (
              <>
                <span className={styles.actionDivider} aria-hidden="true" />
                {canRenameOrg && (
                  <Button variant="secondary" size="sm" onClick={openOrgRename} data-testid="org-action-rename">
                    Omdøb
                  </Button>
                )}
                {selectedNode.kind === 'organisation' && canMoveOrg && (
                  <Button variant="secondary" size="sm" onClick={openOrgMove} data-testid="org-action-move">
                    Flyt
                  </Button>
                )}
                {canDeleteOrg && (
                  <Button variant="ghost" size="sm" onClick={openOrgDelete} data-testid="org-action-delete">
                    Slet
                  </Button>
                )}
              </>
            )}
        </div>
      )}

      {/* c3. SPRINT-109 / TASK-10904 — the ported period-settlement overview: the
          "Ikke indsendt" / "Ikke godkendt" status tiles (click-to-filter the
          Struktur people) + the aggregated "mangler godkender" orphan card with an
          inline approver-assign (the SAME POST /reporting-lines the drawer / "Ret"
          use, via the shared InlineApproverControl / ApproverSection core). Shown on
          an Organisation node, scoped to its loaded roster. */}
      {showSettlement && (
        <div className={styles.settlement} data-testid="settlement-overview">
          <div className={styles.settleTiles}>
            <button
              type="button"
              className={`${styles.settleTile} ${styles.settleTileWarn} ${settlementFilter === 'indsend' ? styles.settleTileOn : ''}`}
              data-testid="settle-tile-indsend"
              aria-pressed={settlementFilter === 'indsend'}
              onClick={() => toggleSettlement('indsend')}
            >
              <span className={styles.settleLabel}>Ikke indsendt</span>
              <span className={styles.settleValue} data-testid="settle-count-indsend">{indsendCount}</span>
              <span className={styles.settleDetail}>{PERIOD_LABEL}</span>
            </button>
            <button
              type="button"
              className={`${styles.settleTile} ${styles.settleTileAlert} ${settlementFilter === 'godkend' ? styles.settleTileOn : ''}`}
              data-testid="settle-tile-godkend"
              aria-pressed={settlementFilter === 'godkend'}
              onClick={() => toggleSettlement('godkend')}
            >
              <span className={styles.settleLabel}>Ikke godkendt</span>
              <span className={styles.settleValue} data-testid="settle-count-godkend">{godkendCount}</span>
              <span className={styles.settleDetail}>godkendere efter frist</span>
            </button>
          </div>

          {orphanPeople.length > 0 && (
            <div className={styles.orphanCard} data-testid="orphan-overview">
              <div className={styles.orphanHead} data-testid="orphan-count">
                ⚠ {orphanPeople.length} mangler godkender
              </div>
              <div className={styles.orphanList}>
                {orphanPeople.map((p) => (
                  <div key={p.employeeId} className={styles.orphanRow} data-testid={`orphan-${p.employeeId}`}>
                    <span className={styles.employeeAvatar} aria-hidden="true">{initials(p.displayName)}</span>
                    <span className={styles.personBody}>
                      <span className={styles.personName}>{p.displayName}</span>
                      {(p.position || p.unitName) && (
                        <span className={styles.personTitle}>
                          {[p.position, p.unitName].filter(Boolean).join(' · ')}
                        </span>
                      )}
                    </span>
                    {canEditUnits && (
                      <InlineApproverControl
                        employeeId={p.employeeId}
                        personName={p.displayName}
                        currentApproverId={null}
                        currentApproverName={null}
                        computeForbidden={() => descendantsOfPerson(p.employeeId)}
                        trigger="assign"
                        onChanged={() => {
                          void onMutated?.(organisationId)
                        }}
                        className={styles.orphanAssign}
                      />
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* d. "Refererer opad til" — READ-ONLY chips */}
      {upRefIds.length > 0 && (
        <div className={styles.upRef} data-testid="up-ref">
          <span className={styles.upRefLabel}>Refererer opad til</span>
          {upRefIds.map((id) => (
            <div key={id} className={styles.upRefChip} data-testid={`up-ref-${id}`}>
              <span className={styles.upRefAvatar} aria-hidden="true">{initials(resolveName(id))}</span>
              <span className={styles.upRefBody}>
                <span className={styles.upRefName}>{resolveName(id)}</span>
                <span className={styles.upRefWhere}>{resolveWhere(id)}</span>
              </span>
            </div>
          ))}
        </div>
      )}

      {/* e. the recursive Struktur */}
      <section className={styles.struktur}>
        <div className={styles.toolbar}>
          <div className={styles.toolbarTitle}>
            <span className={styles.strTitle}>{strTitle}</span>
            <span className={styles.strCount} data-testid="str-count">{strCountLabel}</span>
          </div>
          <div className={styles.toolbarActions}>
            <Button
              variant="ghost"
              size="sm"
              disabled={expandDisabled}
              onClick={toggleExpandAll}
              data-testid="toggle-expand-all"
            >
              {expandLabel}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={togglePeople}
              data-testid="toggle-people"
            >
              {peopleLabel}
            </Button>
          </div>
        </div>

        <div className={styles.nodeList}>
          {waitingForRoster && (
            <div className={styles.status} data-testid="struktur-loading">Indlæser medarbejdere…</div>
          )}
          {!waitingForRoster && nodes.length === 0 && (
            <div className={styles.status} data-testid="struktur-empty">
              {isMao ? 'Ingen organisationer i afgrænsningen.' : 'Ingen medarbejdere eller underenheder endnu.'}
            </div>
          )}

          {nodes.map((n) => {
            if (n.t === 'unit') {
              return (
                <div
                  key={n.key}
                  className={styles.unitRow}
                  style={{ paddingLeft: `${indent(n.depth)}px` }}
                  data-testid={`unit-row-${n.node.id}`}
                >
                  {n.hasContent ? (
                    <button
                      type="button"
                      className={styles.caret}
                      aria-label={n.open ? 'Skjul' : 'Udvid'}
                      data-testid={`caret-unit-${n.node.id}`}
                      onClick={() => setTreeOpen((p) => ({ ...p, [n.node.id]: !n.open }))}
                    >
                      {n.open ? '▾' : '▸'}
                    </button>
                  ) : (
                    <span className={styles.caretSpacer} aria-hidden="true" />
                  )}
                  <span className={styles.dot} aria-hidden="true" style={{ background: `var(--unit-accent-${n.node.type})` }} />
                  <span className={styles.unitName}>{n.node.name}</span>
                  <span className={styles.typeChipSm} style={chipStyle(n.node.type)}>{LABEL[n.node.type]}</span>
                  <span className={styles.unitLeaders}>{n.leaderNames}</span>
                  <span className={styles.count}>{n.count}</span>
                  <button
                    type="button"
                    className={styles.openLink}
                    data-testid={`open-unit-${n.node.id}`}
                    onClick={() => onNavigate(toSelected(n.node))}
                  >
                    Åbn ›
                  </button>
                </div>
              )
            }

            if (n.t === 'medHeader') {
              return (
                <button
                  key={n.key}
                  type="button"
                  className={styles.medRow}
                  style={{ paddingLeft: `${indent(n.depth, 6)}px` }}
                  data-testid={`caret-med-${n.unitId}`}
                  onClick={() => setMedClosed((p) => ({ ...p, [n.unitId]: !n.closed }))}
                >
                  <span className={styles.caret} aria-hidden="true">{n.closed ? '▸' : '▾'}</span>
                  <span className={styles.medLabel}>Medarbejdere</span>
                  <span className={styles.count}>{n.count}</span>
                </button>
              )
            }

            if (n.t === 'leader') {
              const v = n.row.outgoingVikar
              return (
                <div
                  key={n.key}
                  className={styles.leaderRow}
                  style={{ paddingLeft: `${indent(n.depth, 6)}px` }}
                  data-testid={`leader-${n.row.employeeId}`}
                >
                  {n.hasReports ? (
                    <button
                      type="button"
                      className={styles.caret}
                      aria-label={n.collapsed ? 'Vis medarbejdere' : 'Skjul medarbejdere'}
                      data-testid={`caret-leader-${n.row.employeeId}`}
                      onClick={() => setLCollapse((p) => ({ ...p, [n.row.employeeId]: !n.collapsed }))}
                    >
                      {n.collapsed ? '▸' : '▾'}
                    </button>
                  ) : (
                    <span className={styles.caretSpacer} aria-hidden="true" />
                  )}
                  <span className={styles.leaderAvatar} aria-hidden="true">{initials(n.row.displayName)}</span>
                  <span className={styles.personBody}>
                    <span className={styles.personNameRow}>
                      <span className={styles.personName}>{n.row.displayName}</span>
                      {v && <span className={styles.fravBadge} data-testid={`fravaerende-${n.row.employeeId}`}>Fraværende</span>}
                    </span>
                    {n.row.position && <span className={styles.personTitle}>{n.row.position}</span>}
                    {v && (
                      <span className={styles.vikarLine} data-testid={`vikar-line-${n.row.employeeId}`}>
                        Vikar: {v.vikarDisplayName} · til {formatDate(v.untilDate)}
                      </span>
                    )}
                  </span>
                  <span className={styles.lederBadge}>Leder</span>
                  {n.coveringNames.length > 0 && (
                    <span className={styles.vikarForTag} data-testid={`vikar-for-${n.row.employeeId}`}>
                      Vikar for {n.coveringNames.join(', ')}
                    </span>
                  )}
                  <span className={styles.reportCount}>{n.reportCount} medarb.</span>
                  {canEditUnits && (
                    <button
                      type="button"
                      className={styles.personEdit}
                      data-testid={`person-edit-${n.row.employeeId}`}
                      onClick={() => openEditPerson(n.row)}
                    >
                      Rediger ›
                    </button>
                  )}
                </div>
              )
            }

            if (n.t === 'employee') {
              return (
                <div
                  key={n.key}
                  className={n.variant === 'external' ? `${styles.employeeRow} ${styles.employeeExternal}` : styles.employeeRow}
                  style={{ paddingLeft: `${indent(n.depth, n.variant === 'report' ? 34 : 6)}px` }}
                  data-testid={`employee-${n.row.employeeId}`}
                >
                  <span className={styles.employeeAvatar} aria-hidden="true">{initials(n.row.displayName)}</span>
                  <span className={styles.personBody}>
                    <span className={styles.personName}>{n.row.displayName}</span>
                    {n.row.position && <span className={styles.personTitle}>{n.row.position}</span>}
                  </span>
                  {n.variant === 'external' && (
                    <span className={styles.externalTag} data-testid={`external-${n.row.employeeId}`}>
                      Leder uden for enheden: {n.externalLeaderName}
                    </span>
                  )}
                  {/* TASK-10903 — the cross-unit "Ret": reassign the PRIMARY edge to
                      a leader in the member's OWN unit (one-click for a single
                      leader; the pre-filtered picker for several). LocalHR floor. */}
                  {n.variant === 'external' && canEditUnits && (
                    <button
                      type="button"
                      className={styles.retLink}
                      data-testid={`ret-${n.row.employeeId}`}
                      onClick={() => onRetClick(n.row)}
                    >
                      Ret
                    </button>
                  )}
                  {n.coveringNames.length > 0 && (
                    <span className={styles.vikarForTag} data-testid={`vikar-for-${n.row.employeeId}`}>
                      Vikar for {n.coveringNames.join(', ')}
                    </span>
                  )}
                  {canEditUnits && (
                    <button
                      type="button"
                      className={styles.personEdit}
                      data-testid={`person-edit-${n.row.employeeId}`}
                      onClick={() => openEditPerson(n.row)}
                    >
                      Rediger ›
                    </button>
                  )}
                </div>
              )
            }

            // note (leaderless) — TASK-10903: the amber note re-enables as an
            // action ("Tildel leder" → the S108 UnitDrawer edit, focused on the
            // Ledere checkboxes for THIS unit). LocalHR floor.
            return (
              <div
                key={n.key}
                className={styles.noteRow}
                style={{ paddingLeft: `${indent(n.depth, 6)}px` }}
                data-testid="leaderless-note"
              >
                <span className={styles.noteText}>{n.text}</span>
                {canEditUnits && (
                  <button
                    type="button"
                    className={styles.noteAction}
                    data-testid={`assign-leader-${n.unit.id}`}
                    onClick={() => openEditUnit(n.unit)}
                  >
                    Tildel leder
                  </button>
                )}
              </div>
            )
          })}
        </div>
      </section>

      {/* ── the gated unit-mutation drawer / dialogs (TASK-10801) ── */}
      {action?.kind === 'create' && (
        <UnitDrawer
          mode="create"
          typeLabel={LABEL[action.childType]}
          initialName=""
          busy={busy}
          error={actionError}
          onClose={closeAction}
          onSubmitCreate={submitCreate}
        />
      )}
      {action?.kind === 'edit' && (
        <UnitDrawer
          mode="edit"
          typeLabel={LABEL[action.unit.type]}
          initialName={action.unit.name}
          members={membersOf(action.unit).map((m) => ({
            employeeId: m.employeeId,
            displayName: m.displayName,
            position: m.position,
          }))}
          currentLeaderIds={leaderIdsOf(action.unit)}
          busy={busy}
          error={actionError}
          onClose={closeAction}
          onSubmitEdit={submitEdit}
        />
      )}
      {action?.kind === 'move' && (
        <UnitMoveDialog
          unitName={action.unit.name}
          targets={moveTargets}
          busy={busy}
          error={actionError}
          onClose={closeAction}
          onSubmit={submitMove}
        />
      )}
      {action?.kind === 'delete' && (
        <UnitDeleteConfirm
          unitName={action.unit.name}
          busy={busy}
          error={actionError}
          onClose={closeAction}
          onConfirm={submitDelete}
        />
      )}

      {/* ── the gated ORG / MAO structure dialogs (TASK-10802) ── */}
      {orgAction?.kind === 'create' && (
        <OrgCreateDialog
          orgType="ORGANISATION"
          parentName={orgAction.parent.name}
          busy={orgBusy}
          error={orgError}
          onClose={closeOrgAction}
          onSubmit={submitOrgCreate}
        />
      )}
      {orgAction?.kind === 'rename' && (
        <OrgRenameDialog
          orgType={orgAction.node.kind === 'mao' ? 'MAO' : 'ORGANISATION'}
          currentName={orgAction.node.name}
          busy={orgBusy}
          error={orgError}
          onClose={closeOrgAction}
          onSubmit={submitOrgRename}
        />
      )}
      {orgAction?.kind === 'move' && (
        <OrgMoveDialog
          orgName={orgAction.node.name}
          targets={orgMoveTargets}
          busy={orgBusy}
          error={orgError}
          onClose={closeOrgAction}
          onSubmit={submitOrgMove}
        />
      )}
      {orgAction?.kind === 'delete' && (
        <OrgDeleteDialog
          orgType={orgAction.node.kind === 'mao' ? 'MAO' : 'ORGANISATION'}
          name={orgAction.node.name}
          branch={orgAction.branch}
          employeeCount={orgAction.employeeCount}
          busy={orgBusy}
          error={orgError}
          onClose={closeOrgAction}
          onConfirm={submitOrgDelete}
        />
      )}

      {/* ── the gated Person create/edit drawer (TASK-10901/10902) ── */}
      {personDrawer?.mode === 'create' && (
        <PersonDrawer
          open
          organizations={organizations}
          forest={forest}
          defaultOrgId={personDrawer.orgId}
          defaultUnitId={personDrawer.unitId}
          onClose={closePersonDrawer}
          onSaved={onPersonSaved}
        />
      )}
      {personDrawer?.mode === 'edit' && (
        <PersonDrawer
          open
          user={editUser}
          loading={personLoading}
          organizations={organizations}
          forest={forest}
          currentUnitId={personDrawer.row.unitId}
          isLeaderOfCurrentUnit={
            !!personDrawer.row.unitId && personDrawer.row.leaderIds.includes(personDrawer.row.employeeId)
          }
          lifecycleContext={buildLifecycleContext(personDrawer.row)}
          onClose={closePersonDrawer}
          onSaved={onPersonSaved}
        />
      )}

      {/* ── the cross-unit "Ret" leader picker (TASK-10903; multiple peer leaders) ── */}
      {retPicker && (
        <RetLeaderPicker
          personName={retPicker.row.displayName}
          unitName={retPicker.unitName}
          leaders={retPicker.leaders}
          busy={retBusy}
          error={retError}
          onClose={closeRetPicker}
          onSubmit={(managerId) => submitRet(retPicker.row, managerId)}
        />
      )}
    </div>
  )
}
