// Configuration management page (S21 / ADR-017 / TASK-2109).
//
// Replaces the legacy three-tab UI (Effective / Local / Constraints). The
// Effective + Local tabs are consolidated into a single profile-centric
// editor; History is added; Central constraints is preserved as-is.
//
// The user picks (Organization, Agreement, OkVersion) and the profile editor
// loads the currently active profile (or signals "no profile — central applies"
// for first creation). All work happens inside the (org, agreement, OkVersion)
// scope — you can switch tuples freely without losing tab state.
//
// SCOPE: basic functional only — no Phase-5 polish (per the user's S21 cycle-1
// resolution).
import { useMemo, useState } from 'react'
import { useOrganizations } from '../../hooks/useAdmin'
import {
  useCurrentProfile,
  useProfileHistory,
  useConfigConstraints,
  type ConfigConstraint,
  type LocalAgreementProfile,
} from '../../hooks/useConfig'
import { useAgreementConfigs, type AgreementConfig } from '../../hooks/useAgreementConfigs'
import {
  Badge,
  Card,
  Alert,
  Spinner,
  FormField,
  Select,
  Tabs,
  Button,
  Table,
} from '../../components/ui'
import { ProfileEditor } from '../../components/config/ProfileEditor'
import styles from './ConfigManagement.module.css'

// Must mirror the agreement_code values seeded as ACTIVE in
// docker/postgres/init.sql's agreement_configs table (currently AC, AC_RESEARCH,
// AC_TEACHING, HK, PROSA). Step-7a cycle-1 fix: previously hard-coded only the
// three primary codes, which silently hid AC_RESEARCH / AC_TEACHING orgs from
// the local-profile editor after the S21 ConfigManagement rewrite.
const AGREEMENT_OPTIONS = [
  { value: 'AC',           label: 'AC' },
  { value: 'AC_RESEARCH',  label: 'AC_RESEARCH' },
  { value: 'AC_TEACHING',  label: 'AC_TEACHING' },
  { value: 'HK',           label: 'HK' },
  { value: 'PROSA',        label: 'PROSA' },
]

const OK_VERSION_OPTIONS = [
  { value: 'OK24', label: 'OK24' },
  { value: 'OK26', label: 'OK26' },
]

interface OrgLike {
  orgId: string
  orgName: string
}

function formatDate(dateStr: string | null | undefined): string {
  if (!dateStr) return '—'
  // The backend emits yyyy-MM-dd for DateOnly columns. Render in da-DK for the user.
  const d = new Date(dateStr)
  if (Number.isNaN(d.getTime())) return dateStr
  return d.toLocaleDateString('da-DK')
}

function formatDateTime(s: string | null | undefined): string {
  if (!s) return '—'
  const d = new Date(s)
  if (Number.isNaN(d.getTime())) return s
  return d.toLocaleString('da-DK')
}

function boolBadge(value: boolean) {
  return (
    <Badge variant={value ? 'success' : 'default'}>
      {value ? 'Ja' : 'Nej'}
    </Badge>
  )
}

// ── Tab 1: Profile editor. ──

interface ProfileTabProps {
  orgId: string
  agreementCode: string
  okVersion: string
  orgLabel: string
}

function ProfileTab({ orgId, agreementCode, okVersion, orgLabel }: ProfileTabProps) {
  const { profile, etag, loading, error, refresh } = useCurrentProfile(orgId, agreementCode, okVersion)
  const { configs: centralConfigs } = useAgreementConfigs('ACTIVE')

  // The central agreement config to surface as read-only context. There is at
  // most one ACTIVE config per (agreement, OkVersion) by design — pick it.
  const centralConfig: AgreementConfig | null = useMemo(() => {
    return centralConfigs.find(
      c => c.agreementCode === agreementCode && c.okVersion === okVersion,
    ) ?? null
  }, [centralConfigs, agreementCode, okVersion])

  return (
    <ProfileEditor
      orgId={orgId}
      agreementCode={agreementCode}
      okVersion={okVersion}
      orgLabel={orgLabel}
      profile={profile}
      etag={etag}
      centralConfig={centralConfig}
      loading={loading}
      loadError={error}
      onSaved={refresh}
    />
  )
}

// ── Tab 2: History. ──

interface HistoryTabProps {
  orgId: string
  agreementCode: string
  okVersion: string
}

function HistoryTab({ orgId, agreementCode, okVersion }: HistoryTabProps) {
  const { history, loading, error } = useProfileHistory(orgId, agreementCode, okVersion)
  const [expanded, setExpanded] = useState<string | null>(null)

  if (loading) {
    return (
      <div className={styles.loadingWrapper}>
        <Spinner />
      </div>
    )
  }
  if (error) {
    return <Alert variant="error">{error}</Alert>
  }
  if (history.length === 0) {
    return (
      <div className={styles.emptyState}>
        Ingen tidligere profiler — denne (organisation, overenskomst, OK-version) har ingen lukkede forgaengere.
      </div>
    )
  }

  return (
    <Card>
      <Table headers={['Gaelder fra', 'Gaelder til', 'Aendret af', 'Oprettet', 'Detaljer']}>
        {history.map((row: LocalAgreementProfile) => (
          <RowWithDelta
            key={row.profileId}
            row={row}
            expanded={expanded === row.profileId}
            onToggle={() => setExpanded(expanded === row.profileId ? null : row.profileId)}
          />
        ))}
      </Table>
    </Card>
  )
}

interface RowWithDeltaProps {
  row: LocalAgreementProfile
  expanded: boolean
  onToggle: () => void
}

function RowWithDelta({ row, expanded, onToggle }: RowWithDeltaProps) {
  return (
    <>
      <tr>
        <td>{formatDate(row.effectiveFrom)}</td>
        <td>{formatDate(row.effectiveTo)}</td>
        <td>{row.createdBy}</td>
        <td>{formatDateTime(row.createdAt)}</td>
        <td>
          <Button variant="secondary" size="sm" onClick={onToggle}>
            {expanded ? 'Skjul' : 'Vis aendringer'}
          </Button>
        </td>
      </tr>
      {expanded && (
        <tr>
          <td colSpan={5} style={{ background: 'var(--color-bg-subtle, #f9fafb)' }}>
            <div style={{ padding: '0.5rem', fontSize: '0.875rem' }}>
              <div><strong>Profil-ID:</strong> {row.profileId}</div>
              <div style={{ marginTop: '0.5rem' }}><strong>Vaerdier paa denne profil:</strong></div>
              <ul style={{ margin: '0.25rem 0 0 1rem' }}>
                <li>WeeklyNormHours: {valueOrInherit(row.weeklyNormHours)}</li>
                <li>MaxFlexBalance: {valueOrInherit(row.maxFlexBalance)}</li>
                <li>FlexCarryoverMax: {valueOrInherit(row.flexCarryoverMax)}</li>
                <li>MaxOvertimeHoursPerPeriod: {valueOrInherit(row.maxOvertimeHoursPerPeriod)}</li>
                <li>OvertimeRequiresPreApproval: {row.overtimeRequiresPreApproval === null ? '(arvet fra central)' : row.overtimeRequiresPreApproval ? 'Ja' : 'Nej'}</li>
              </ul>
            </div>
          </td>
        </tr>
      )}
    </>
  )
}

function valueOrInherit(value: number | null): string {
  if (value === null) return '(arvet fra central)'
  return String(value)
}

// ── Tab 3: Central constraints (unchanged). ──

function CentralConstraintsTab() {
  const { constraints, loading, error } = useConfigConstraints()

  if (loading) {
    return (
      <div className={styles.loadingWrapper}>
        <Spinner />
      </div>
    )
  }
  if (error) {
    return <Alert variant="error">{error}</Alert>
  }
  if (constraints.length === 0) {
    return (
      <div className={styles.emptyState}>
        Ingen centrale begraensninger fundet.
      </div>
    )
  }

  return (
    <Card>
      <table className={styles.constraintsTable}>
        <thead>
          <tr>
            <th>Overenskomst</th>
            <th>OK-version</th>
            <th>Ugenorm</th>
            <th>Max flex</th>
            <th>Flex overfoersel</th>
            <th>Overarbejde</th>
            <th>Merarbejde</th>
            <th>Aften</th>
            <th>Nat</th>
            <th>Weekend</th>
            <th>Helligdag</th>
            <th>Raadighed</th>
            <th>Vagtrate</th>
          </tr>
        </thead>
        <tbody>
          {constraints.map((c: ConfigConstraint) => (
            <tr key={`${c.agreementCode}-${c.okVersion}`}>
              <td>{c.agreementCode}</td>
              <td>{c.okVersion}</td>
              <td>{c.weeklyNormHours}</td>
              <td>{c.maxFlexBalance}</td>
              <td>{c.flexCarryoverMax}</td>
              <td>{boolBadge(c.hasOvertime)}</td>
              <td>{boolBadge(c.hasMerarbejde)}</td>
              <td>{boolBadge(c.eveningSupplementEnabled)}</td>
              <td>{boolBadge(c.nightSupplementEnabled)}</td>
              <td>{boolBadge(c.weekendSupplementEnabled)}</td>
              <td>{boolBadge(c.holidaySupplementEnabled)}</td>
              <td>{boolBadge(c.onCallDutyEnabled)}</td>
              <td>{c.onCallDutyRate}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  )
}

// ── Main page. ──

export function ConfigManagement() {
  const { organizations } = useOrganizations()
  const [selectedOrgId, setSelectedOrgId] = useState('')
  const [selectedAgreement, setSelectedAgreement] = useState('AC')
  const [selectedOkVersion, setSelectedOkVersion] = useState('OK24')

  const orgOptions = (organizations as OrgLike[]).map(org => ({
    value: org.orgId,
    label: `${org.orgName} (${org.orgId})`,
  }))

  const orgLabel = (organizations as OrgLike[]).find(o => o.orgId === selectedOrgId)?.orgName ?? selectedOrgId

  const tupleSelected = Boolean(selectedOrgId && selectedAgreement && selectedOkVersion)

  const tabs = [
    {
      value: 'profile',
      label: 'Lokal profil',
      content: tupleSelected ? (
        <ProfileTab
          orgId={selectedOrgId}
          agreementCode={selectedAgreement}
          okVersion={selectedOkVersion}
          orgLabel={orgLabel}
        />
      ) : (
        <div className={styles.emptyState}>
          Vaelg organisation, overenskomst og OK-version for at se profilen.
        </div>
      ),
    },
    {
      value: 'history',
      label: 'Historik',
      content: tupleSelected ? (
        <HistoryTab
          orgId={selectedOrgId}
          agreementCode={selectedAgreement}
          okVersion={selectedOkVersion}
        />
      ) : (
        <div className={styles.emptyState}>
          Vaelg organisation, overenskomst og OK-version for at se historik.
        </div>
      ),
    },
    {
      value: 'constraints',
      label: 'Centrale begraensninger',
      content: <CentralConstraintsTab />,
    },
  ]

  return (
    <div className={styles.page}>
      <h1 className={styles.title}>Konfiguration</h1>

      <div className={styles.tupleSelector}>
        <FormField label="Organisation" htmlFor="config-org-select" required>
          <Select
            id="config-org-select"
            options={orgOptions}
            value={selectedOrgId}
            onValueChange={setSelectedOrgId}
            placeholder="Vaelg organisation..."
          />
        </FormField>
        <FormField label="Overenskomst" htmlFor="config-agreement-select" required>
          <Select
            id="config-agreement-select"
            options={AGREEMENT_OPTIONS}
            value={selectedAgreement}
            onValueChange={setSelectedAgreement}
          />
        </FormField>
        <FormField label="OK-version" htmlFor="config-okversion-select" required>
          <Select
            id="config-okversion-select"
            options={OK_VERSION_OPTIONS}
            value={selectedOkVersion}
            onValueChange={setSelectedOkVersion}
          />
        </FormField>
      </div>

      <Tabs tabs={tabs} defaultValue="profile" />
    </div>
  )
}
