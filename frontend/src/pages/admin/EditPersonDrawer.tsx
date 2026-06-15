// S76b / TASK-7602 — the unified EditPersonDrawer SHELL + the STAMDATA re-house.
//
// THE single create/edit surface for a person (re-housing UserManagement's
// create + multi-PUT edit). Built MODULARLY (Codex/Reviewer W: do NOT recreate
// the god-component): a kit-Drawer shell + per-section field modules
// (StamdataSection / ProfileSection / EntitlementSection) + the multi-PUT save
// orchestration in `useEditPerson`. The Profile + Entitlement sections are
// HR-gated (rendered only for an HROrAbove actor; OQ-5b accepts an honest 403).
//
// 7603 SLOT: the Ledelseslinje / Vikariering / Fjern-medarbejder sections plug
// in via the `extraSections` render-region below the entitlement section (a
// clearly-marked `<div data-ep-slot="lifecycle">`); 7602 leaves it empty.
//
// 7604 wires this into the tree/page and retires UserManagement's dialogs.
import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { Drawer } from '../../components/ui'
import { useToast } from '../../components/ui/Toast'
import { useAuth } from '../../contexts/AuthContext'
import { useEntitlementEligibility } from '../../hooks/useEntitlementEligibility'
import { useEditPerson, type EditLiveState } from '../../hooks/useEditPerson'
import type { Organization, User } from '../../hooks/useAdmin'
import { fetchEmployeeProfile } from './editPerson/employeeProfileApi'
import { StamdataSection } from './editPerson/StamdataSection'
import { ProfileSection } from './editPerson/ProfileSection'
import { EntitlementSection } from './editPerson/EntitlementSection'
import {
  isHrCapable,
  type StamdataFields,
  type ProfileFields,
  type EntitlementFields,
  type CreateCredentials,
} from './editPerson/types'
import { LifecycleSections, type LifecycleContext } from './editPerson/LifecycleSections'
import styles from './EditPersonDrawer.module.css'

interface EditPersonDrawerProps {
  open: boolean
  /** The user to edit; null/undefined opens the CREATE form. */
  user?: User | null
  organizations: Organization[]
  /** Default org for the create form (the currently-selected styrelse/org). */
  defaultOrgId?: string
  onClose: () => void
  /** Fired after a successful create / edit save so the caller can refetch. */
  onSaved?: () => void
  /**
   * 7603 SLOT — the ledelseslinje / vikar / delete sections render here (below
   * the HR sections, above the footer). 7603 supplies the approver/vikar/delete
   * UI keyed off the editing user via the internal LifecycleSections. A caller
   * (7604, the tree) MAY pass tree-derived `lifecycleContext` so names render
   * immediately + the forbidden set is the exact descendant set. `extraSections`
   * remains an escape hatch for any additional page-specific content.
   */
  extraSections?: ReactNode
  /** 7604 — tree-derived display context for the lifecycle sections (optional). */
  lifecycleContext?: LifecycleContext
  /** True to suppress the internal lifecycle sections (e.g. a host that renders
      its own). Defaults to false → the drawer renders them. */
  hideLifecycleSections?: boolean
}

const EMPTY_STAMDATA: StamdataFields = {
  displayName: '',
  email: '',
  primaryOrgId: '',
  agreementCode: 'AC',
}
const EMPTY_PROFILE: ProfileFields = { partTimeFraction: '1.000', position: '', enhedLabel: '' }
const EMPTY_ENTITLEMENT: EntitlementFields = {
  birthDate: '',
  employmentStartDate: '',
  childSickEligible: false,
}
const EMPTY_CREDS: CreateCredentials = { userId: '', username: '', password: '' }

export function EditPersonDrawer({
  open,
  user,
  organizations,
  defaultOrgId,
  onClose,
  onSaved,
  extraSections,
  lifecycleContext,
  hideLifecycleSections = false,
}: EditPersonDrawerProps) {
  const isNew = !user
  const { toast } = useToast()
  const { role } = useAuth()
  const isHr = isHrCapable(role)

  const { sections, saving, saveEdit, createPerson, fetchUser, resetSections } = useEditPerson()
  const {
    fetchBirthDate,
    fetchEmploymentStartDate,
    fetchChildSickEligibility,
  } = useEntitlementEligibility()

  // --- Form state ---
  const [stamdata, setStamdata] = useState<StamdataFields>(EMPTY_STAMDATA)
  const [profile, setProfile] = useState<ProfileFields>(EMPTY_PROFILE)
  const [entitlement, setEntitlement] = useState<EntitlementFields>(EMPTY_ENTITLEMENT)
  const [creds, setCreds] = useState<CreateCredentials>(EMPTY_CREDS)
  const [childSickDirty, setChildSickDirty] = useState(false)
  // 7603 — the create-mode draft approver (threaded into the atomic create POST).
  const [draftApproverId, setDraftApproverId] = useState<string | null>(null)
  const [draftApproverName, setDraftApproverName] = useState<string | null>(null)

  // --- Live concurrency state (ETags / versions captured at open) ---
  const [live, setLive] = useState<EditLiveState | null>(null)
  const [loading, setLoading] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [staleConflict, setStaleConflict] = useState<{ expected?: number; actual?: number } | null>(
    null,
  )

  // Hydrate on open: edit → parallel fetch (user is supplied; profile/DOB/
  // employment-start/CHILD_SICK only when HR); create → defaults.
  useEffect(() => {
    if (!open) return
    let cancelled = false
    setFormError(null)
    setStaleConflict(null)
    resetSections()
    setChildSickDirty(false)
    setDraftApproverId(null)
    setDraftApproverName(null)

    if (isNew) {
      setStamdata({
        ...EMPTY_STAMDATA,
        primaryOrgId: defaultOrgId ?? organizations[0]?.orgId ?? '',
        agreementCode:
          organizations.find((o) => o.orgId === (defaultOrgId ?? organizations[0]?.orgId))
            ?.agreementCode ?? 'AC',
      })
      setProfile(EMPTY_PROFILE)
      setEntitlement(EMPTY_ENTITLEMENT)
      setCreds(EMPTY_CREDS)
      setLive(null)
      return
    }

    const target = user as User
    setStamdata({
      displayName: target.displayName,
      email: target.email ?? '',
      primaryOrgId: target.primaryOrgId,
      agreementCode: target.agreementCode,
    })
    setProfile(EMPTY_PROFILE)
    setEntitlement(EMPTY_ENTITLEMENT)
    setLoading(true)

    async function hydrate() {
      try {
        // Stamdata (users-row) version comes from the supplied row; HR reads are
        // only fired for an HR actor (a non-HR actor's HR sections are hidden, so
        // a 403 on those GETs would be noise).
        const [profileSnap, dob, employmentStart, elig] = await Promise.all([
          isHr ? fetchEmployeeProfile(target.userId).catch(() => null) : Promise.resolve(null),
          isHr ? fetchBirthDate(target.userId).catch(() => null) : Promise.resolve(null),
          isHr ? fetchEmploymentStartDate(target.userId).catch(() => null) : Promise.resolve(null),
          isHr
            ? fetchChildSickEligibility(target.userId).catch(() => null)
            : Promise.resolve(null),
        ])
        if (cancelled) return

        if (profileSnap) {
          setProfile({
            partTimeFraction: profileSnap.partTimeFraction.toFixed(3),
            position: profileSnap.position ?? '',
            enhedLabel: profileSnap.enhedLabel ?? '',
          })
        }
        setEntitlement({
          birthDate: dob?.birthDate ?? '',
          employmentStartDate: employmentStart?.employmentStartDate ?? '',
          childSickEligible: elig?.eligible ?? false,
        })
        setLive({
          // The users row carries a `version`; compose its If-Match from it.
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
        if (!cancelled) setLoading(false)
      }
    }
    void hydrate()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, user])

  const patchStamdata = useCallback((patch: Partial<StamdataFields>) => {
    setStamdata((s) => ({ ...s, ...patch }))
  }, [])
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

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)

    if (isNew) {
      try {
        await createPerson({
          userId: creds.userId,
          username: creds.username,
          password: creds.password,
          displayName: stamdata.displayName,
          email: stamdata.email || undefined,
          primaryOrgId: stamdata.primaryOrgId,
          agreementCode: stamdata.agreementCode,
          // The backend CreateUserRequest REQUIRES okVersion — derive it from the
          // selected org's denormalized `okVersion` (the org list serves it).
          okVersion: organizations.find((o) => o.orgId === stamdata.primaryOrgId)?.okVersion ?? '',
          // 7603 — atomic create+assign (S74 R9): the draft approver plants the
          // PRIMARY reporting line in the SAME tx (no orphan window).
          approverId: draftApproverId ?? undefined,
        })
        toast({ title: 'Oprettet', description: 'Medarbejder oprettet', variant: 'success' })
        onSaved?.()
        onClose()
      } catch (err) {
        setFormError(err instanceof Error ? err.message : String(err))
      }
      return
    }

    if (!live) return
    const result = await saveEdit(
      { stamdata, profile, entitlement, childSickDirty, isHr },
      live,
    )
    // `result.live` carries the advanced versions + initials + ETags
    // (read-your-write). Re-binding it means a follow-up save after a PARTIAL
    // failure re-uses the freshest If-Match for the committed sections and only
    // re-attempts the failed ones. The form values stay as the user typed them
    // (those are the desired target); the hook compares them against the new
    // initials, so a committed change is a no-op on the next save. childSick is
    // not dirty until the toggle is touched again.
    setLive(result.live)
    setStaleConflict(result.staleConflict)
    setChildSickDirty(false)

    if (result.ok) {
      toast({ title: 'Gemt', description: 'Medarbejder opdateret', variant: 'success' })
      onSaved?.()
      onClose()
    }
    // On a partial/full failure the drawer stays open; the per-section state +
    // the stale banner surface the honest outcome.
  }

  const handleStaleRefresh = useCallback(async () => {
    if (!live) {
      setStaleConflict(null)
      return
    }
    try {
      // BLOCKER 2 (S76b fix-forward): the 412 stale banner fires on the USERS PUT
      // (the primary row). Re-binding only the PROFILE left `live.user.version`
      // stale → the retried users/DOB/employment-start writes (all keyed on
      // users.version) would 412 AGAIN. So re-fetch the USERS row FIRST and
      // re-bind the fresh `version`/ETag (mirror UserManagement's handleStaleRefresh),
      // then re-bind the profile snapshot.
      const freshUser = await fetchUser(live.user.userId)
      const profileSnap = isHr ? await fetchEmployeeProfile(live.user.userId) : null
      if (profileSnap) {
        setProfile({
          partTimeFraction: profileSnap.partTimeFraction.toFixed(3),
          position: profileSnap.position ?? '',
          enhedLabel: profileSnap.enhedLabel ?? '',
        })
      }
      // Re-bind the form's stamdata to the fresh server row (a concurrent edit may
      // have changed displayName/email/org/agreement), so the retried users PUT
      // carries the current values + the fresh If-Match version.
      setStamdata({
        displayName: freshUser.displayName,
        email: freshUser.email ?? '',
        primaryOrgId: freshUser.primaryOrgId,
        agreementCode: freshUser.agreementCode,
      })
      setLive((cur) =>
        cur
          ? {
              ...cur,
              user: freshUser,
              ...(profileSnap ? { profile: profileSnap } : {}),
            }
          : cur,
      )
      setStaleConflict(null)
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }, [live, isHr, fetchUser, fetchEmployeeProfile])

  const title = isNew ? 'Opret medarbejder' : `Redigér ${user?.displayName ?? ''}`.trim()
  const submitLabel = isNew ? 'Opret medarbejder' : 'Gem ændringer'

  return (
    <Drawer open={open} onClose={onClose} ariaLabel={title}>
      <form className={styles.drawerForm} onSubmit={handleSubmit}>
        <div className={styles.header}>
          <div>
            <h2 className={styles.title} data-testid="ep-title">
              {title}
            </h2>
            <div className={styles.subtitle}>
              {isNew ? 'Opret medarbejder' : 'Redigér medarbejder'}
            </div>
          </div>
          <button
            type="button"
            className={styles.closeBtn}
            onClick={onClose}
            aria-label="Luk"
          >
            ✕
          </button>
        </div>

        <div className={styles.body}>
          {staleConflict && (
            <div className={styles.alert} role="alert" data-testid="ep-stale-conflict-banner">
              Medarbejderen er ændret af en anden administrator siden du indlæste den.
              {staleConflict.expected !== undefined && staleConflict.actual !== undefined && (
                <>
                  {' '}
                  (Forventet version {staleConflict.expected}, aktuel version{' '}
                  {staleConflict.actual}.)
                </>
              )}{' '}
              <button type="button" className={styles.linkBtn} onClick={handleStaleRefresh}>
                Genindlæs
              </button>
            </div>
          )}

          {loading && (
            <div className={styles.loading} data-testid="ep-loading">
              Indlæser...
            </div>
          )}

          {/* Create-only credentials — the backend REQUIRES username+password. */}
          {isNew && (
            <section className={styles.section} aria-labelledby="ep-creds-heading">
              <h3 id="ep-creds-heading" className={styles.sectionLabel}>
                Bruger
              </h3>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="ep-userId">
                  Bruger-ID <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="ep-userId"
                  type="text"
                  required
                  value={creds.userId}
                  onChange={(e) => setCreds((c) => ({ ...c, userId: e.target.value }))}
                  placeholder="f.eks. EMP010"
                  data-testid="ep-create-user-id"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="ep-username">
                  Brugernavn <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="ep-username"
                  type="text"
                  required
                  value={creds.username}
                  onChange={(e) => setCreds((c) => ({ ...c, username: e.target.value }))}
                  placeholder="Brugernavn"
                  data-testid="ep-create-username"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="ep-password">
                  Adgangskode <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="ep-password"
                  type="password"
                  required
                  value={creds.password}
                  onChange={(e) => setCreds((c) => ({ ...c, password: e.target.value }))}
                  placeholder="Adgangskode"
                  data-testid="ep-create-password"
                />
              </div>
            </section>
          )}

          <StamdataSection
            fields={stamdata}
            onChange={patchStamdata}
            organizations={organizations}
            userId={isNew ? undefined : user?.userId}
            disabled={saving}
          />

          {/* HR-gated sections — hidden for a non-HR actor (OQ-5b: avoid
              show-then-403). At CREATE the HR fields are unavailable anyway (the
              POST seeds profile defaults; a follow-up HR PUT would 403 for a
              non-HR actor) — so on create we only render them for an HR actor. */}
          {isHr && !isNew && (
            <>
              <ProfileSection
                fields={profile}
                onChange={patchProfile}
                hasProfile={live?.profile != null}
                saveState={sections.profile}
                disabled={saving}
              />
              <EntitlementSection
                fields={entitlement}
                onChange={patchEntitlement}
                onChildSickToggle={onChildSickToggle}
                hasDateVersions={live?.birthDateVersion != null}
                birthDateSave={sections.birthDate}
                employmentStartSave={sections.employmentStart}
                childSickSave={sections.childSick}
                disabled={saving}
              />
            </>
          )}

          {/* 7603 SLOT — ledelseslinje / vikariering / fjern medarbejder. */}
          <div data-ep-slot="lifecycle">
            {!hideLifecycleSections && (
              <LifecycleSections
                mode={isNew ? 'create' : 'edit'}
                employeeId={isNew ? undefined : user?.userId}
                personName={isNew ? stamdata.displayName : user?.displayName ?? ''}
                context={lifecycleContext}
                draftApproverId={draftApproverId}
                draftApproverName={draftApproverName}
                onDraftApproverChange={(id, name) => {
                  setDraftApproverId(id)
                  setDraftApproverName(name)
                }}
                onMutated={() => onSaved?.()}
                onPersonRemoved={() => {
                  onSaved?.()
                  onClose()
                }}
                disabled={saving}
              />
            )}
            {extraSections}
          </div>

          {formError && (
            <div className={styles.alert} role="alert" data-testid="ep-form-error">
              {formError}
            </div>
          )}
        </div>

        <div className={styles.footer}>
          <button
            type="button"
            className={styles.cancelBtn}
            onClick={onClose}
            disabled={saving}
          >
            Annullér
          </button>
          <button type="submit" className={styles.submitBtn} disabled={saving}>
            {saving ? 'Gemmer...' : submitLabel}
          </button>
        </div>
      </form>
    </Drawer>
  )
}
