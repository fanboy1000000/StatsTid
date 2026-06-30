// SPRINT-109 / TASK-10901 (Enhedsspor Phase 3b-2b) — the Person create/edit drawer
// on the merged "Organisation & medarbejdere" admin page.
//
// REUSE, do not re-derive: this wraps the PROVEN editPerson cores —
// StamdataSection (Navn/E-mail/Organisation/Overenskomst), ProfileSection +
// EntitlementSection (HR-gated), and LifecycleSections (the ApproverSection =
// "Nærmeste leder" + VikarSection = "Vikar ved fravær" + DangerSection = Slet).
// The approver/vikar PersonPicker searches SERVER-side (scope-filtered) and
// LifecycleSections self-resolves the current approver/lineETag/vikar in edit
// mode, so the drawer needs NEITHER the merged roster shape for candidates NOR
// cycle-prevention — the host just hands it the fetched `user` (for the edit etag)
// + a tree-derived lifecycle context.
//
// What S109 ADDS on top of the reuse: the design §3 unit fields — **Placering**
// (the unit Select, derived from the S106 forest for the chosen Organisation,
// incl. null = directly under the Organisation; RELOADED when the Organisation
// changes), the **apex** ("Øverste leder — ingen overordnet") toggle, and the
// **promote** ("Er leder af <unit>") checkbox — plus the load-bearing 4-case
// PLACEMENT routing on save (usePlacement, TASK-10902). The reused useEditPerson
// only sends primaryOrgId; usePlacement layers the unit-assign / transfer / the
// version-threading / the move-then-promote ordering on top.

import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Drawer } from '../../../components/ui'
import { useToast } from '../../../components/ui/Toast'
import { useAuth } from '../../../contexts/AuthContext'
import { useEntitlementEligibility } from '../../../hooks/useEntitlementEligibility'
import { usePlacement } from '../../../hooks/usePlacement'
import type { EditLiveState } from '../../../hooks/useEditPerson'
import type { Organization, WithEtag, User } from '../../../hooks/useAdmin'
import type { ForestMaoNode } from '../../../hooks/useForest'
import { fetchEmployeeProfile } from '../editPerson/employeeProfileApi'
import { StamdataSection } from '../editPerson/StamdataSection'
import { ProfileSection } from '../editPerson/ProfileSection'
import { EntitlementSection } from '../editPerson/EntitlementSection'
import { LifecycleSections, type LifecycleContext } from '../editPerson/LifecycleSections'
import {
  isHrCapable,
  INITIAL_SECTION_SAVE,
  type StamdataFields,
  type ProfileFields,
  type EntitlementFields,
  type CreateCredentials,
} from '../editPerson/types'
import { unitOptionsForOrg } from './personDrawerData'
import styles from '../EditPersonDrawer.module.css'

interface PersonDrawerProps {
  open: boolean
  /** Edit mode — the fetched user (carries the users-row version for If-Match).
      null/undefined = CREATE. */
  user?: WithEtag<User> | null
  /** The Organisation option source (derived from the forest by the host). */
  organizations: Organization[]
  /** The S106 forest — the Placering (unit) option source. */
  forest: ForestMaoNode[]
  /** Create — the Organisation to pre-select. */
  defaultOrgId?: string
  /** Create — the unit to pre-select as Placering (the "+ Medarbejder" was
      clicked on a unit). Edit ignores this (it uses `currentUnitId`). */
  defaultUnitId?: string | null
  /** Edit — the person's current unit (null = Organisation-homed). */
  currentUnitId?: string | null
  /** Edit — does the person currently lead `currentUnitId`? (drives the promote
      checkbox's initial state + the demote decision). */
  isLeaderOfCurrentUnit?: boolean
  /** Edit — tree-derived lifecycle context (approver/vikar names + descendants). */
  lifecycleContext?: LifecycleContext
  /** True while the host is fetching the user for edit. */
  loading?: boolean
  onClose: () => void
  /** Fired after a successful save (or an in-place lifecycle mutation) so the host
      refetches the roster (+ forest). The arg is the affected Organisation id. */
  onSaved: (organisationId: string | null) => void
}

const EMPTY_STAMDATA: StamdataFields = {
  displayName: '',
  email: '',
  primaryOrgId: '',
  agreementCode: 'AC',
}
const EMPTY_PROFILE: ProfileFields = { partTimeFraction: '1.000', position: '' }
const EMPTY_ENTITLEMENT: EntitlementFields = {
  birthDate: '',
  employmentStartDate: '',
  childSickEligible: false,
}
const EMPTY_CREDS: CreateCredentials = { userId: '', username: '', password: '' }

export function PersonDrawer({
  open,
  user,
  organizations,
  forest,
  defaultOrgId,
  defaultUnitId,
  currentUnitId,
  isLeaderOfCurrentUnit = false,
  lifecycleContext,
  loading: hostLoading = false,
  onClose,
  onSaved,
}: PersonDrawerProps) {
  const isNew = !user
  const { toast } = useToast()
  const { role } = useAuth()
  const isHr = isHrCapable(role)

  const { savePlacement } = usePlacement()
  const { fetchBirthDate, fetchEmploymentStartDate, fetchChildSickEligibility } =
    useEntitlementEligibility()

  // ── form state ────────────────────────────────────────────────────────────────
  const [stamdata, setStamdata] = useState<StamdataFields>(EMPTY_STAMDATA)
  const [profile, setProfile] = useState<ProfileFields>(EMPTY_PROFILE)
  const [entitlement, setEntitlement] = useState<EntitlementFields>(EMPTY_ENTITLEMENT)
  const [creds, setCreds] = useState<CreateCredentials>(EMPTY_CREDS)
  const [childSickDirty, setChildSickDirty] = useState(false)
  // S109 — the unit fields.
  const [placementUnitId, setPlacementUnitId] = useState<string | null>(null)
  const [apex, setApex] = useState(false)
  const [promote, setPromote] = useState(false)
  // Create-mode draft approver (threaded into the atomic create POST).
  const [draftApproverId, setDraftApproverId] = useState<string | null>(null)
  const [draftApproverName, setDraftApproverName] = useState<string | null>(null)

  const [live, setLive] = useState<EditLiveState | null>(null)
  const [hydrating, setHydrating] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // The Placering options reload whenever the chosen Organisation changes (a unit
  // belongs to exactly one Organisation, so an org change invalidates the unit set).
  const placementOptions = useMemo(
    () => unitOptionsForOrg(forest, stamdata.primaryOrgId),
    [forest, stamdata.primaryOrgId],
  )

  // ── hydrate on open ─────────────────────────────────────────────────────────────
  useEffect(() => {
    if (!open) return
    let cancelled = false
    setFormError(null)
    setChildSickDirty(false)
    setDraftApproverId(null)
    setDraftApproverName(null)

    if (isNew) {
      const orgId = defaultOrgId ?? organizations[0]?.orgId ?? ''
      setStamdata({
        ...EMPTY_STAMDATA,
        primaryOrgId: orgId,
        agreementCode: organizations.find((o) => o.orgId === orgId)?.agreementCode ?? 'AC',
      })
      setProfile(EMPTY_PROFILE)
      setEntitlement(EMPTY_ENTITLEMENT)
      setCreds(EMPTY_CREDS)
      setPlacementUnitId(defaultUnitId ?? null)
      setApex(false)
      setPromote(false)
      setLive(null)
      return
    }

    const target = user as WithEtag<User>
    setStamdata({
      displayName: target.displayName,
      email: target.email ?? '',
      primaryOrgId: target.primaryOrgId,
      agreementCode: target.agreementCode,
    })
    setProfile(EMPTY_PROFILE)
    setEntitlement(EMPTY_ENTITLEMENT)
    setPlacementUnitId(currentUnitId ?? null)
    setApex(lifecycleContext?.isRoot ?? false)
    setPromote(isLeaderOfCurrentUnit)
    setHydrating(true)

    async function hydrate() {
      try {
        const [profileSnap, dob, employmentStart, elig] = await Promise.all([
          isHr ? fetchEmployeeProfile(target.userId).catch(() => null) : Promise.resolve(null),
          isHr ? fetchBirthDate(target.userId).catch(() => null) : Promise.resolve(null),
          isHr ? fetchEmploymentStartDate(target.userId).catch(() => null) : Promise.resolve(null),
          isHr ? fetchChildSickEligibility(target.userId).catch(() => null) : Promise.resolve(null),
        ])
        if (cancelled) return
        if (profileSnap) {
          setProfile({
            partTimeFraction: profileSnap.partTimeFraction.toFixed(3),
            position: profileSnap.position ?? '',
          })
        }
        setEntitlement({
          birthDate: dob?.birthDate ?? '',
          employmentStartDate: employmentStart?.employmentStartDate ?? '',
          childSickEligible: elig?.eligible ?? false,
        })
        setLive({
          user: { ...target, etag: `"${target.version}"`, version: target.version },
          profile: profileSnap,
          birthDateVersion: dob?.version ?? null,
          birthDateInitial: dob?.birthDate ?? '',
          employmentStartVersion: employmentStart?.version ?? null,
          employmentStartInitial: employmentStart?.employmentStartDate ?? '',
          childSickRowExists: elig?.rowExists ?? false,
          childSickVersion: elig?.version ?? null,
        })
      } catch (err) {
        if (!cancelled) setFormError(err instanceof Error ? err.message : String(err))
      } finally {
        if (!cancelled) setHydrating(false)
      }
    }
    void hydrate()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, user])

  const patchStamdata = useCallback((patch: Partial<StamdataFields>) => {
    setStamdata((s) => {
      const next = { ...s, ...patch }
      // When the Organisation changes the Placering set is invalid → reset to
      // org-home + clear the promote (a unit in the old org is gone).
      if (patch.primaryOrgId && patch.primaryOrgId !== s.primaryOrgId) {
        setPlacementUnitId(null)
        setPromote(false)
        // Adopt the new org's default agreement (mirrors the create defaulting).
        const ag = organizations.find((o) => o.orgId === patch.primaryOrgId)?.agreementCode
        if (ag) next.agreementCode = ag
      }
      return next
    })
  }, [organizations])
  const patchProfile = useCallback((patch: Partial<ProfileFields>) => {
    setProfile((p) => ({ ...p, ...patch }))
  }, [])
  const patchEntitlement = useCallback((patch: Partial<EntitlementFields>) => {
    setEntitlement((e) => ({ ...e, ...patch }))
  }, [])
  const onChildSickToggle = useCallback((next: boolean) => {
    setEntitlement((e) => ({ ...e, childSickEligible: next }))
    setChildSickDirty(true)
  }, [])

  const orgChanged = !isNew && !!user && stamdata.primaryOrgId !== user.primaryOrgId
  const unitChanged = !isNew && placementUnitId !== (currentUnitId ?? null)
  const placementOrgId = stamdata.primaryOrgId || null

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)
    setSaving(true)
    try {
      if (isNew) {
        const okVersion =
          organizations.find((o) => o.orgId === stamdata.primaryOrgId)?.okVersion ?? ''
        const result = await savePlacement({
          mode: 'create',
          createBody: {
            userId: creds.userId,
            username: creds.username,
            password: creds.password,
            displayName: stamdata.displayName,
            email: stamdata.email || undefined,
            primaryOrgId: stamdata.primaryOrgId,
            agreementCode: stamdata.agreementCode,
            okVersion,
            // apex → no approver; else the draft approver plants the PRIMARY edge
            // in the same create tx (S74 R9 atomic create+assign).
            approverId: apex ? undefined : draftApproverId ?? undefined,
          },
          targetUnitId: placementUnitId,
          designateUnitId: promote && placementUnitId ? placementUnitId : null,
        })
        if (result.ok) {
          toast({ title: 'Oprettet', description: 'Medarbejder oprettet', variant: 'success' })
          onSaved(placementOrgId)
          onClose()
        } else {
          setFormError(result.error)
        }
        return
      }

      if (!live || !user) return
      // Promote/demote decisions. After an Org change or a unit change the person is
      // NOT a leader of the resulting unit (the transfer/move strips leadership), so
      // a checked promote always designates; an unchanged unit keys off the current
      // leadership flag.
      const alreadyLeaderOfResultUnit =
        !orgChanged && !unitChanged && isLeaderOfCurrentUnit
      const designateUnitId =
        promote && placementUnitId && !alreadyLeaderOfResultUnit ? placementUnitId : null
      const removeLeaderUnitId =
        !promote && isLeaderOfCurrentUnit && !unitChanged && !orgChanged
          ? currentUnitId ?? null
          : null

      const result = await savePlacement({
        mode: 'edit',
        userId: user.userId,
        editInput: { stamdata, profile, entitlement, childSickDirty, isHr },
        live,
        orgChanged,
        targetUnitId: placementUnitId,
        unitChanged,
        designateUnitId,
        removeLeaderUnitId,
      })
      if (result.ok) {
        toast({ title: 'Gemt', description: 'Medarbejder opdateret', variant: 'success' })
        onSaved(placementOrgId)
        // S109 Step-7a (Codex): a cross-Org transfer also leaves the SOURCE Organisation's
        // cached roster stale (it still lists the moved person) → refetch it too.
        if (orgChanged && user.primaryOrgId && user.primaryOrgId !== placementOrgId) {
          onSaved(user.primaryOrgId)
        }
        onClose()
      } else {
        setFormError(result.error)
      }
    } finally {
      setSaving(false)
    }
  }

  // S109 Step-7a (both lenses): the apex toggle is a CREATE-time concept (apex ⇒ no
  // approverId in the POST). In EDIT mode it must NOT drive the ApproverSection's read-only
  // "Øverste godkendelseslinje" view — that hid the "Fjern" control AND `Gem` never removed
  // the edge (a silent no-op + a dead-end). In edit, isRoot=false so the ApproverSection
  // self-resolves and always exposes the real assign / Skift / Fjern controls (Fjern is how
  // you demote-to-apex; the assign control is how you give an apex person an approver).
  const effectiveContext: LifecycleContext = { ...lifecycleContext, isRoot: isNew ? apex : false }

  const placementLabel =
    placementOptions.find((o) => o.unitId === placementUnitId)?.name ?? 'enheden'
  const title = isNew ? 'Opret medarbejder' : `Redigér ${user?.displayName ?? ''}`.trim()
  const submitLabel = isNew ? 'Opret medarbejder' : 'Gem ændringer'
  const busy = saving || hostLoading || hydrating

  return (
    <Drawer open={open} onClose={onClose} ariaLabel={title}>
      <form className={styles.drawerForm} onSubmit={handleSubmit}>
        <div className={styles.header}>
          <div>
            <h2 className={styles.title} data-testid="person-drawer-title">
              {title}
            </h2>
            <div className={styles.subtitle}>{isNew ? 'Opret medarbejder' : 'Redigér medarbejder'}</div>
          </div>
          <button type="button" className={styles.closeBtn} onClick={onClose} aria-label="Luk">
            ✕
          </button>
        </div>

        <div className={styles.body}>
          {(hostLoading || hydrating) && (
            <div className={styles.loading} data-testid="person-drawer-loading">
              Indlæser...
            </div>
          )}

          {/* Create-only credentials — the backend REQUIRES username+password. */}
          {isNew && (
            <section className={styles.section} aria-labelledby="pd-creds-heading">
              <h3 id="pd-creds-heading" className={styles.sectionLabel}>
                Bruger
              </h3>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="pd-userId">
                  Bruger-ID <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="pd-userId"
                  type="text"
                  required
                  value={creds.userId}
                  onChange={(e) => setCreds((c) => ({ ...c, userId: e.target.value }))}
                  placeholder="f.eks. EMP010"
                  data-testid="pd-create-user-id"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="pd-username">
                  Brugernavn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="pd-username"
                  type="text"
                  required
                  value={creds.username}
                  onChange={(e) => setCreds((c) => ({ ...c, username: e.target.value }))}
                  placeholder="Brugernavn"
                  data-testid="pd-create-username"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="pd-password">
                  Adgangskode <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="pd-password"
                  type="password"
                  required
                  value={creds.password}
                  onChange={(e) => setCreds((c) => ({ ...c, password: e.target.value }))}
                  placeholder="Adgangskode"
                  data-testid="pd-create-password"
                />
              </div>
            </section>
          )}

          <StamdataSection
            fields={stamdata}
            onChange={patchStamdata}
            organizations={organizations}
            userId={isNew ? undefined : user?.userId}
            disabled={busy}
          />

          {/* S109 — Placering (the unit Select, reloaded on Organisation change). */}
          <section className={styles.section} aria-labelledby="pd-placement-heading">
            <h3 id="pd-placement-heading" className={styles.sectionLabel}>
              Placering
            </h3>
            <div className={styles.formField}>
              <label className={styles.formLabel} htmlFor="pd-placement">
                Enhed
              </label>
              <select
                className={styles.select}
                id="pd-placement"
                value={placementUnitId ?? ''}
                onChange={(e) => setPlacementUnitId(e.target.value || null)}
                disabled={busy}
                data-testid="pd-placement"
              >
                {placementOptions.map((o) => (
                  <option key={o.unitId ?? '__org__'} value={o.unitId ?? ''}>
                    {`${'  '.repeat(Math.max(0, o.depth - 1))}${o.name}`}
                  </option>
                ))}
              </select>
              <div className={styles.helperText}>
                Vælg enheden medarbejderen hører til, eller “Direkte under organisationen”.
              </div>
            </div>
          </section>

          {/* HR-gated sections — hidden for a non-HR actor + at create. */}
          {isHr && !isNew && (
            <>
              <ProfileSection
                fields={profile}
                onChange={patchProfile}
                hasProfile={live?.profile != null}
                saveState={INITIAL_SECTION_SAVE}
                disabled={busy}
              />
              <EntitlementSection
                fields={entitlement}
                onChange={patchEntitlement}
                onChildSickToggle={onChildSickToggle}
                hasDateVersions={live?.birthDateVersion != null}
                birthDateSave={INITIAL_SECTION_SAVE}
                employmentStartSave={INITIAL_SECTION_SAVE}
                childSickSave={INITIAL_SECTION_SAVE}
                disabled={busy}
              />
            </>
          )}

          {/* S109 — Ledelse: the apex + promote toggles. */}
          <section className={styles.section} aria-labelledby="pd-ledelse-heading">
            <h3 id="pd-ledelse-heading" className={styles.sectionLabel}>
              Ledelse
            </h3>
            {/* S109 Step-7a: apex is a CREATE-only toggle (no approverId in the POST). In
                EDIT mode it is omitted — approver-removal (→ apex) flows through the
                ApproverSection's "Fjern" so it is never a no-op or a hidden control. */}
            {isNew && (
              <label className={styles.checkboxRow}>
                <input
                  type="checkbox"
                  checked={apex}
                  onChange={(e) => setApex(e.target.checked)}
                  disabled={busy}
                  data-testid="pd-apex"
                />
                Øverste leder — ingen overordnet
              </label>
            )}
            <label className={styles.checkboxRow}>
              <input
                type="checkbox"
                checked={promote}
                onChange={(e) => setPromote(e.target.checked)}
                disabled={busy || placementUnitId === null}
                data-testid="pd-promote"
              />
              Er leder af {placementLabel}
            </label>
            {placementUnitId === null && (
              <div className={styles.helperText}>
                Vælg en enhed under Placering for at gøre medarbejderen til leder.
              </div>
            )}
          </section>

          {/* Reused lifecycle cores: Nærmeste leder (ApproverSection) + Vikar ved
              fravær (VikarSection) + Slet (DangerSection). */}
          <LifecycleSections
            mode={isNew ? 'create' : 'edit'}
            employeeId={isNew ? undefined : user?.userId}
            personName={isNew ? stamdata.displayName : user?.displayName ?? ''}
            context={effectiveContext}
            draftApproverId={draftApproverId}
            draftApproverName={draftApproverName}
            onDraftApproverChange={(id, name) => {
              setDraftApproverId(id)
              setDraftApproverName(name)
            }}
            onMutated={() => onSaved(placementOrgId)}
            onPersonRemoved={() => {
              onSaved(placementOrgId)
              onClose()
            }}
            disabled={busy}
          />

          {formError && (
            <div className={styles.alert} role="alert" data-testid="person-drawer-error">
              {formError}
            </div>
          )}
        </div>

        <div className={styles.footer}>
          <button type="button" className={styles.cancelBtn} onClick={onClose} disabled={busy}>
            Annullér
          </button>
          <button type="submit" className={styles.submitBtn} disabled={busy}>
            {saving ? 'Gemmer...' : submitLabel}
          </button>
        </div>
      </form>
    </Drawer>
  )
}
