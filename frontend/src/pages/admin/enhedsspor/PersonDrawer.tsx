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

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Drawer } from '../../../components/ui'
import { useToast } from '../../../components/ui/Toast'
import { useAuth } from '../../../contexts/AuthContext'
import { useEntitlementEligibility } from '../../../hooks/useEntitlementEligibility'
import { usePlacement } from '../../../hooks/usePlacement'
import { todayIso, type EditLiveState } from '../../../hooks/useEditPerson'
import type { Organization, WithEtag, User } from '../../../hooks/useAdmin'
import type { ForestMaoNode } from '../../../hooks/useForest'
import { fetchEmployeeProfile } from '../editPerson/employeeProfileApi'
import { StamdataSection } from '../editPerson/StamdataSection'
import { ProfileSection } from '../editPerson/ProfileSection'
import { EntitlementSection } from '../editPerson/EntitlementSection'
import { LifecycleSections, type LifecycleContext } from '../editPerson/LifecycleSections'
import { ScheduledChangeNotice } from '../editPerson/ScheduledChangeNotice'
import { EffectiveDatePicker, relateToScheduled, type ScheduleRelation } from '../editPerson/EffectiveDatePicker'
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
  // S124 / TASK-12401 — set when an Organisation change DISCARDED a picked draft approver, so the
  // ApproverSection can explain the empty field. Cleared as soon as a new approver is picked.
  const [approverClearedByOrgChange, setApproverClearedByOrgChange] = useState(false)

  const [live, setLive] = useState<EditLiveState | null>(null)
  const [hydrating, setHydrating] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  // S141 / TASK-14107 — OQ-6 (a): "apply until the scheduled change" (default,
  // unchecked) vs "also carry this edit into it" — one choice per DATED field
  // that can carry a scheduled change (profile / agreement code). Reset on
  // every open so a stale choice from a previous edit never survives a reopen.
  const [profileCarryForward, setProfileCarryForward] = useState(false)
  const [agreementCarryForward, setAgreementCarryForward] = useState(false)
  // S142 / TASK-14209 (census rows 60-64) — owner ruling OQ-12 (2026-09-17). `todayIso()` (the
  // Europe/Copenhagen calendar day — see `useEditPerson.ts`) throws if the runtime cannot resolve
  // that zone, deliberately: a fallback to the browser's own zone would silently reinstate the
  // exact UTC/browser-local defect S142 removes. This drawer reads "today" in FOUR places within
  // one render — the effective-date default just below, the open-time reset, the create-mode
  // hire-date pre-fill, and the future/past classification further down — and previously called
  // the (wrong) raw formula directly in each. An uncaught throw in ANY of them blanks the WHOLE
  // drawer (Name, e-mail, Organisation, Placering, the approver section — everything), for a fault
  // that has nothing to do with most of those fields. Resolved ONCE per render, mirroring
  // `MondayDatePicker.tsx` (the first OQ-12 site), and reused everywhere below so all four agree
  // with each other within the same render pass, instead of independently risking disagreement.
  let today: string | null
  let zoneError: string | null
  try {
    today = todayIso()
    zoneError = null
  } catch {
    today = null
    zoneError =
      'Dags dato kan ikke bestemmes: denne browser kan ikke bestemme den danske kalenderdag ' +
      '(tidszonedata for Europe/Copenhagen mangler). Prøv en anden browser eller opdater den.'
  }

  // S141 / TASK-14111 — the effective-date picker. ONE date governs the whole
  // save (both the users PUT and the employee-profiles PUT below read it).
  // Requirement 1: the default is ALWAYS today, reset on every open exactly
  // like every other field — dating a change ahead is something HR must
  // deliberately choose on THIS open, never something that survives from a
  // previous edit or a stale render. `today ?? ''` (S142/OQ-12): an unresolvable
  // zone leaves this EMPTY rather than guessing — HR must then pick a date
  // explicitly, and the save stays blocked (see `dateBlockedReason` below)
  // until the zone resolves or the tab is reloaded on a capable runtime.
  const [effectiveFrom, setEffectiveFrom] = useState<string>(today ?? '')
  // SPRINT-END BLOCKER FIX (2026-09-14) — the baseline each of the two dated
  // field-groups was last RE-PRE-FILLED from (today's values, or an existing
  // scheduled change's values, per `relateToScheduled`), so the re-baseline
  // effects below can tell "HR never touched this since the last baseline
  // change" (→ follow the new baseline) apart from "HR deliberately edited
  // it" (→ leave their edit alone). `null` = no baseline applied yet (right
  // after open); the effects treat that as "apply unconditionally" instead
  // of comparing against a nonexistent previous value. Refs, not state: this
  // is bookkeeping for a comparison, not a value the render or the save reads.
  const profileBaselineRef = useRef<{ partTimeFraction: string; position: string } | null>(null)
  const agreementBaselineRef = useRef<string | null>(null)

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
    setProfileCarryForward(false)
    setAgreementCarryForward(false)
    // S142/OQ-12: `today` is the SAME zone-resolved (or null) value computed once at the top of
    // this render — reused rather than re-derived so an unresolvable zone can never disagree with
    // itself within the same open.
    setEffectiveFrom(today ?? '')
    // A stale baseline from a PREVIOUS edit session must never suppress the
    // first re-baseline of this one — null means "apply unconditionally".
    profileBaselineRef.current = null
    agreementBaselineRef.current = null

    if (isNew) {
      const orgId = defaultOrgId ?? organizations[0]?.orgId ?? ''
      setStamdata({
        ...EMPTY_STAMDATA,
        primaryOrgId: orgId,
        agreementCode: organizations.find((o) => o.orgId === orgId)?.agreementCode ?? 'AC',
      })
      setProfile(EMPTY_PROFILE)
      // SPRINT-140 / TASK-14007 (HRP-016) — pre-fill today, editable. An undated
      // create silently means "hired today" server-side (S137 ruling) and blocks
      // back-filling any registration from before the record existed; pre-filling
      // (rather than leaving it blank) makes that default visible and correctable
      // in the one place a backdated hire can be recorded at create time.
      // S142 / TASK-14209 (census rows 60-64) — was the raw UTC formula; now the Copenhagen day
      // (`today`, computed once at the top of this render). S142/OQ-12: when the zone cannot be
      // resolved (`today === null`), this pre-fill is left BLANK rather than guessed — the field
      // stays optional and editable either way, and an omitted value still means "hired today"
      // per the S137 ruling, resolved correctly SERVER-side regardless of what the browser could
      // determine (the backend's own S142 waves 1-2 already moved to the Copenhagen day).
      setEntitlement({ ...EMPTY_ENTITLEMENT, employmentStartDate: today ?? '' })
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
        // S124 / TASK-12401 — a DRAFT approver picked under the OLD Organisation is now invalid:
        // the create POST same-Organisation-validates the edge (AdminEndpoints.cs:1180) and would
        // 400. Clear it for the same reason Placering resets, and flag it so the section can say
        // WHY it emptied — a silently-cleared field reads as a bug.
        // Read the current value OUTSIDE the updater: React may invoke an updater twice, and a
        // conditional side effect in there would be impure (the discipline StrukturPanel.revealSubtree
        // already documents). `draftApproverId` is therefore a DEPENDENCY of this callback — without
        // it the closure goes stale and the notice never fires.
        if (draftApproverId !== null) setApproverClearedByOrgChange(true)
        setDraftApproverId(null)
        setDraftApproverName(null)
        // Adopt the new org's default agreement (mirrors the create defaulting).
        const ag = organizations.find((o) => o.orgId === patch.primaryOrgId)?.agreementCode
        if (ag) next.agreementCode = ag
      }
      return next
    })
  }, [organizations, draftApproverId])
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

  // S124 / TASK-12401 — one line explaining the approver field's Organisation coupling, so
  // neither behaviour reads as a bug. CREATE: the pick was DISCARDED by an org change. EDIT: the
  // picker still searches the PERSISTED org while an unsaved transfer is pending, so the person
  // list will not match the Organisation shown above it.
  const approverNotice = isNew
    ? approverClearedByOrgChange
      ? 'Godkenderen blev ryddet, fordi organisationen blev ændret.'
      : null
    : orgChanged
      ? 'Godkendere søges stadig i den nuværende organisation, indtil overflytningen er gemt.'
      : null
  const unitChanged = !isNew && placementUnitId !== (currentUnitId ?? null)
  const placementOrgId = stamdata.primaryOrgId || null

  // S141 / TASK-14107 — B0 (visibility) + OQ-6 (a) (the edit prompt). Both
  // read straight off the SAME payload the drawer already hydrated (`live`) —
  // no second call (B0 forbids that bolt-on shape). `scheduled`/
  // `scheduledAgreementCode` are `null` when nothing is scheduled, so these
  // are `null` too in that case and nothing renders.
  const scheduledProfile = live?.profile?.scheduled ?? null
  const scheduledAgreement = live?.user.scheduledAgreementCode ?? null
  // Dirty = the value HR is about to SEND differs from what a fresh open
  // hydrated (mirrors exactly what `useEditPerson.saveEdit` sends, so the
  // OQ-6 choice appears precisely when it would matter).
  const profileDirty =
    !!live?.profile &&
    (profile.partTimeFraction !== live.profile.partTimeFraction.toFixed(3) ||
      (profile.position.trim() || null) !== live.profile.position)
  const agreementDirty = !isNew && !!live && stamdata.agreementCode !== live.user.agreementCode
  const profileScheduledSummary = scheduledProfile
    ? `Deltidsfraktion ${scheduledProfile.partTimeFraction.toFixed(3).replace('.', ',')}${
        scheduledProfile.position ? ` · ${scheduledProfile.position}` : ''
      }`
    : ''
  const agreementScheduledSummary = scheduledAgreement ? `Overenskomst ${scheduledAgreement.agreementCode}` : ''

  // S141 / TASK-14111 — `today` (computed once at the top of this render, S142/OQ-12) is reused
  // here rather than re-derived, so the picker's "is this future?" check and the
  // ScheduledChangeNotice wording below can never disagree with each other even if the drawer sits
  // open across a Copenhagen midnight. `undefined` here (the write IS dated today) is exactly the
  // value ScheduledChangeNotice's `writeEffectiveFrom` prop treats as "keep the previous 'gemmer du
  // nu' wording". `today === null` (zone unresolvable) is NEVER treated as "dated today" — that
  // would be guessing an agreement with a day we do not actually know — so it falls through to the
  // dated ("gemmer du med virkning fra …") wording instead, which is the honest default.
  const writeEffectiveFrom = today !== null && effectiveFrom === today ? undefined : effectiveFrom

  // SPRINT-END BLOCKER FIX (2026-09-14, coordinator-verified) — relate the
  // PICKED date to each of the two scheduled changes' own intervals. This is
  // the classification the picker was missing entirely: it let HR date a
  // write to fall INSIDE an existing scheduled change's interval while the
  // form still showed TODAY's values, so an untouched field sent today's
  // stale values dated into the scheduled interval — which the backend
  // (correctly comparing against the row that actually covers that date)
  // read as a genuine change and wrote forward, silently reverting the
  // colleague's scheduled decision. See `EffectiveDatePicker.tsx`'s
  // `relateToScheduled` doc for exactly what each relation means.
  const profileRelation: ScheduleRelation = relateToScheduled(scheduledProfile, effectiveFrom)
  const agreementRelation: ScheduleRelation = relateToScheduled(scheduledAgreement, effectiveFrom)

  // 'covers' — re-baseline the profile fields FROM the scheduled row, so a
  // field HR never touches sends the SCHEDULED value back (a genuine no-op)
  // instead of today's stale one. Per-field tracking via `profileBaselineRef`:
  // a field is only re-baselined while it still equals the PREVIOUS
  // baseline — the moment HR deliberately edits it away from that, this
  // effect leaves the edit alone rather than clobbering it on a later date
  // change. Does NOT fire on every keystroke: its deps are the RELATION
  // (a string, unchanged by typing) and `live.profile` (unchanged by
  // typing), not the `profile` form state itself.
  useEffect(() => {
    if (isNew || !isHr || !live?.profile) return
    const target =
      profileRelation === 'covers' && scheduledProfile
        ? { partTimeFraction: scheduledProfile.partTimeFraction.toFixed(3), position: scheduledProfile.position ?? '' }
        : { partTimeFraction: live.profile.partTimeFraction.toFixed(3), position: live.profile.position ?? '' }
    const prev = profileBaselineRef.current
    setProfile((p) => ({
      partTimeFraction:
        prev === null || p.partTimeFraction === prev.partTimeFraction ? target.partTimeFraction : p.partTimeFraction,
      position: prev === null || (p.position.trim() || null) === (prev.position || null) ? target.position : p.position,
    }))
    profileBaselineRef.current = target
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isNew, isHr, live?.profile, profileRelation, scheduledProfile])

  // Same re-baseline, for the AGREEMENT CODE (bundled into `stamdata`, not
  // HR-gated). `live?.user.agreementCode` (today's value), not the whole
  // `live` object, so this does not refire on unrelated `live` updates.
  useEffect(() => {
    if (isNew || !live) return
    const target =
      agreementRelation === 'covers' && scheduledAgreement ? scheduledAgreement.agreementCode : live.user.agreementCode
    const prev = agreementBaselineRef.current
    setStamdata((s) => ({
      ...s,
      agreementCode: prev === null || s.agreementCode === prev ? target : s.agreementCode,
    }))
    agreementBaselineRef.current = target
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isNew, live?.user.agreementCode, agreementRelation, scheduledAgreement])

  // 'beyond' — refuse rather than guess: a further row must exist past the
  // scheduled change's own end and this payload (one hop ahead only) does
  // not carry it. Blocks the WHOLE save (one submit covers both writes)
  // rather than letting one dimension proceed while the other is unsafe.
  //
  // S142 / TASK-14209 — OQ-12: an unresolvable Copenhagen zone (`zoneError`) blocks the save the
  // SAME way, but ONLY in edit mode, where this drawer actually depends on "today" for a required
  // value (the effective-date default). Create mode's own use of `today` (the hire-date pre-fill)
  // is optional and already left blank rather than guessed — see the "open" effect above — so it
  // has nothing that needs blocking, and gating it here too would needlessly refuse a create the
  // zone problem never actually touches.
  const dateBlockedReason: string | null =
    !isNew && zoneError !== null
      ? zoneError
      : profileRelation === 'beyond' && agreementRelation === 'beyond'
        ? 'Denne dato ligger efter både den planlagte ændring af deltid/stilling og den planlagte overenskomstændring. Hvad der gælder derefter, er ikke vist her — vælg en tidligere dato.'
        : profileRelation === 'beyond'
          ? 'Denne dato ligger efter den planlagte ændring af deltid/stilling. Hvad der gælder derefter, er ikke vist her — vælg en tidligere dato.'
          : agreementRelation === 'beyond'
            ? 'Denne dato ligger efter den planlagte overenskomstændring. Hvad der gælder derefter, er ikke vist her — vælg en tidligere dato.'
            : null

  // 'covers' — an advisory note the picker itself cannot phrase (it doesn't
  // know about either scheduled change): names which field(s) were just
  // re-baselined and what leaving vs. editing them now does.
  const coversFields = [
    profileRelation === 'covers' ? 'deltid/stilling' : null,
    agreementRelation === 'covers' ? 'overenskomstkoden' : null,
  ].filter((f): f is string => f !== null)
  const coversScheduledNote =
    coversFields.length > 0
      ? `Denne dato ligger inden for en allerede planlagt ændring af ${coversFields.join(' og ')}. Felterne herunder er derfor forudfyldt med de planlagte værdier — retter du et felt, erstatter din nye værdi den planlagte ændring fra denne dato; lader du det stå, bevares den planlagte værdi.`
      : null

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
            // SPRINT-140 / TASK-14007 (HRP-016) — sent as EmploymentStartDate;
            // omitted (blank) falls through to the backend's own today-default.
            employmentStartDate: entitlement.employmentStartDate || undefined,
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
      // SPRINT-END BLOCKER FIX — defensive, in-depth: the submit button is
      // already disabled while `dateBlockedReason` holds, but this guard
      // means a re-render race can never smuggle the save through anyway.
      if (dateBlockedReason) {
        setFormError(dateBlockedReason)
        return
      }
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
        editInput: {
          stamdata,
          profile,
          entitlement,
          childSickDirty,
          isHr,
          // S141 / OQ-6 (a) — only meaningful (and only sent) when a
          // scheduled change actually exists for that field; the backend
          // otherwise ignores the flag anyway, but omitting it keeps a plain
          // save free of a field with no effect.
          ...(scheduledAgreement !== null ? { stamdataCarryForward: agreementCarryForward } : {}),
          ...(scheduledProfile !== null ? { profileCarryForward } : {}),
          // S141 / TASK-14111 — the effective-date picker's value; governs
          // BOTH of this save's dated writes (see useEditPerson.saveEdit).
          effectiveFrom,
        },
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

          {/* S141 / TASK-14107 — B0: the agreement code is a SECOND dated field
              written on every save (bundled into the same users PUT as
              Navn/E-mail/Organisation above), so a scheduled change to it must be
              just as visible as one on the profile fields — otherwise the
              owner's own defect reappears one field over. */}
          {scheduledAgreement && (
            <ScheduledChangeNotice
              effectiveFrom={scheduledAgreement.effectiveFrom}
              summary={agreementScheduledSummary}
              // SPRINT-END BLOCKER FIX — OQ-6's apply-until / carry-forward
              // choice only makes sense in the 'before' relation (the write
              // TRUNCATES this scheduled row). In 'covers'/'beyond' the
              // checkbox is backend-inert (there is no bounded carry
              // target), so showing it would be misleading; `supersedes`
              // below explains the 'covers' case with an accurate sentence
              // instead.
              carryForward={
                agreementRelation === 'before' && agreementDirty
                  ? { checked: agreementCarryForward, onChange: setAgreementCarryForward, disabled: busy }
                  : undefined
              }
              supersedes={agreementRelation === 'covers' ? { writeEffectiveFrom: effectiveFrom } : undefined}
              writeEffectiveFrom={writeEffectiveFrom}
              testId="pd-agreement-scheduled"
            />
          )}

          {/* S141 / TASK-14111 — the effective-date picker. Governs BOTH
              dated writes this drawer performs (the agreement-code field
              just above, and the profile fields further down) — one HR
              decision for the whole save. Edit mode only: a brand-new
              person has no "existing value" for a scheduled change to
              apply against. */}
          {!isNew && (
            <EffectiveDatePicker
              value={effectiveFrom}
              onChange={setEffectiveFrom}
              today={today}
              disabled={busy}
              blockedReason={dateBlockedReason}
              coversScheduledNote={coversScheduledNote}
            />
          )}

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

          {/* SPRINT-140 / TASK-14007 (HRP-016) — the create-only hire date. HR-gated
              like the edit-mode Entitlement section it mirrors (isHrCapable — in
              practice every actor who can reach this drawer already satisfies it,
              the page itself is LocalHR-floored, but the same gate is kept for
              consistency and defense-in-depth). Pre-filled with today (set at
              open, above) and editable — never leave it blank by construction, so
              a backdated hire is a deliberate choice rather than a silent default
              the admin never saw. */}
          {isNew && isHr && (
            <section className={styles.section} aria-labelledby="pd-employment-heading">
              <h3 id="pd-employment-heading" className={styles.sectionLabel}>
                Ansættelse
              </h3>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="pd-employment-start">
                  Ansættelsesdato
                </label>
                <input
                  className={styles.input}
                  id="pd-employment-start"
                  type="date"
                  value={entitlement.employmentStartDate}
                  onChange={(e) => patchEntitlement({ employmentStartDate: e.target.value })}
                  disabled={busy}
                  data-testid="pd-employment-start"
                />
                <div className={styles.helperText}>
                  Forudfyldt med dagens dato. En udatert oprettelse betyder “ansat i dag” og forhindrer registrering af
                  noget, der ligger før — ret datoen her, hvis medarbejderen reelt er ansat tidligere.
                </div>
              </div>
            </section>
          )}

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

              {/* S141 / TASK-14107 — B0 + OQ-6 (a) for the profile fields
                  (partTimeFraction / position). Read straight off the profile
                  GET's `scheduled` member — no second call. */}
              {scheduledProfile && (
                <ScheduledChangeNotice
                  effectiveFrom={scheduledProfile.effectiveFrom}
                  summary={profileScheduledSummary}
                  // SPRINT-END BLOCKER FIX — see the agreement-code notice
                  // above for why this is gated on 'before' and paired with
                  // `supersedes`.
                  carryForward={
                    profileRelation === 'before' && profileDirty
                      ? { checked: profileCarryForward, onChange: setProfileCarryForward, disabled: busy }
                      : undefined
                  }
                  supersedes={profileRelation === 'covers' ? { writeEffectiveFrom: effectiveFrom } : undefined}
                  writeEffectiveFrom={writeEffectiveFrom}
                  testId="pd-profile-scheduled"
                />
              )}

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

          {/* SPRINT-141 / TASK-14108 (termination screen) + TASK-14107 follow-up
              (owner-flagged gap: the screen existed but had no caller). A
              per-employee ACTION page, not a destination — it deliberately has
              no sidebar entry, so this link is the only way in. EDIT mode only
              (there is nobody to terminate at create). Deliberately does NOT
              reference the scheduled-change notices above: termination does
              NOT cancel a scheduled profile/agreement change (only deleting
              the profile does, a different action entirely) — the two are
              independent and this link says nothing that would imply otherwise. */}
          {!isNew && user && (
            <section className={styles.section} aria-labelledby="pd-termination-heading">
              <h3 id="pd-termination-heading" className={styles.sectionLabel}>
                Fratrædelse
              </h3>
              <p className={styles.helperText}>
                Registrér medarbejderens fratrædelsesdato på{' '}
                <Link to={`/admin/medarbejdere/${user.userId}/fratraedelse`} data-testid="pd-termination-link">
                  fratrædelsessiden
                </Link>
                .
              </p>
            </section>
          )}

          {/* Reused lifecycle cores: Nærmeste leder (ApproverSection) + Vikar ved
              fravær (VikarSection) + Slet (DangerSection). */}
          <LifecycleSections
            mode={isNew ? 'create' : 'edit'}
            employeeId={isNew ? undefined : user?.userId}
            personName={isNew ? stamdata.displayName : user?.displayName ?? ''}
            context={effectiveContext}
            // S124 / TASK-12401 — scope the pickers to the Organisation the SERVER will validate
            // the resulting edge against, which is NOT the same field in the two modes:
            //   • CREATE — the approver rides along in the create POST, which carries the DRAFT
            //     primaryOrgId and validates against it (AdminEndpoints.cs:1180) ⇒ the draft org.
            //   • EDIT — the assign is an IMMEDIATE POST /api/admin/reporting-lines that validates
            //     against the PERSISTED primary_org_id. A cross-Organisation transfer is a
            //     first-class flow here, so the select can be dirty; scoping to the draft would
            //     list the new org's people and then 400 on pick — reintroducing exactly the
            //     dishonest picker this task removes ⇒ the persisted org, until the save lands.
            organisationId={isNew ? stamdata.primaryOrgId || null : user?.primaryOrgId ?? null}
            approverNotice={approverNotice}
            draftApproverId={draftApproverId}
            draftApproverName={draftApproverName}
            onDraftApproverChange={(id, name) => {
              setDraftApproverId(id)
              setDraftApproverName(name)
              // The notice explained an EMPTY field; a fresh pick makes it stale.
              setApproverClearedByOrgChange(false)
            }}
            onMutated={() => onSaved(placementOrgId)}
            onPersonRemoved={() => {
              onSaved(placementOrgId)
              onClose()
            }}
            // S141 / TASK-14107 — B0: the danger section (DangerSection, "Fjern
            // medarbejder fra afgrænsning") shows this person's scheduled
            // change too, for awareness (see DangerSection's own doc comment).
            scheduledProfile={scheduledProfile ? { effectiveFrom: scheduledProfile.effectiveFrom, summary: profileScheduledSummary } : null}
            scheduledAgreement={scheduledAgreement ? { effectiveFrom: scheduledAgreement.effectiveFrom, summary: agreementScheduledSummary } : null}
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
          <button type="submit" className={styles.submitBtn} disabled={busy || dateBlockedReason !== null}>
            {saving ? 'Gemmer...' : submitLabel}
          </button>
        </div>
      </form>
    </Drawer>
  )
}
