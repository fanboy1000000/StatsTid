import { useState, type ChangeEvent } from 'react'
import { useOrganizations } from '../../hooks/useAdmin'
import {
  useEffectiveConfig,
  useLocalConfig,
  useConfigConstraints,
} from '../../hooks/useConfig'
import {
  Button,
  Select,
  Badge,
  Card,
  Alert,
  Spinner,
  Dialog,
  FormField,
  Input,
  Table,
  Tabs,
} from '../../components/ui'
import type {
  Organization,
  LocalConfiguration,
  ConfigConstraint,
} from '../../types'
import styles from './ConfigManagement.module.css'

const CONFIG_AREA_OPTIONS = [
  { value: 'WORKING_TIME', label: 'Arbejdstid' },
  { value: 'FLEX_RULES', label: 'Flex-regler' },
  { value: 'ORG_STRUCTURE', label: 'Organisationsstruktur' },
  { value: 'LOCAL_AGREEMENT', label: 'Lokal aftale' },
  { value: 'OPERATIONAL', label: 'Operationel' },
]

const AGREEMENT_OPTIONS = [
  { value: 'AC', label: 'AC' },
  { value: 'HK', label: 'HK' },
  { value: 'PROSA', label: 'PROSA' },
]

const OK_VERSION_OPTIONS = [
  { value: 'OK24', label: 'OK24' },
  { value: 'OK26', label: 'OK26' },
]

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleDateString('da-DK')
}

function boolBadge(value: boolean) {
  return (
    <Badge variant={value ? 'success' : 'default'}>
      {value ? 'Ja' : 'Nej'}
    </Badge>
  )
}

// --- Tab 1: Effective Configuration ---
function EffectiveConfigTab({ orgId }: { orgId: string }) {
  const { config, loading, error } = useEffectiveConfig(orgId)

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

  if (!config || Object.keys(config).length === 0) {
    return (
      <div className={styles.emptyState}>
        Ingen effektiv konfiguration fundet for denne organisation.
      </div>
    )
  }

  const entries = Object.entries(config as Record<string, unknown>)

  return (
    <Card>
      <Table headers={['Noegle', 'Vaerdi']}>
        {entries.map(([key, value]: [string, unknown]) => (
          <tr key={key}>
            <td>{key}</td>
            <td>{String(value)}</td>
          </tr>
        ))}
      </Table>
    </Card>
  )
}

// --- Tab 2: Local Overrides ---
function LocalOverridesTab({ orgId }: { orgId: string }) {
  const { configs, loading, error, createOverride, deactivateOverride } =
    useLocalConfig(orgId)

  // Create override dialog state
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [formArea, setFormArea] = useState('')
  const [formKey, setFormKey] = useState('')
  const [formValue, setFormValue] = useState('')
  const [formFrom, setFormFrom] = useState('')
  const [formTo, setFormTo] = useState('')
  const [formAgreement, setFormAgreement] = useState('')
  const [formOkVersion, setFormOkVersion] = useState('')
  const [createLoading, setCreateLoading] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  // Deactivate dialog state
  const [deactivateDialogOpen, setDeactivateDialogOpen] = useState(false)
  const [deactivateTargetId, setDeactivateTargetId] = useState<string | null>(
    null
  )
  const [deactivateLoading, setDeactivateLoading] = useState(false)
  const [deactivateError, setDeactivateError] = useState<string | null>(null)

  function openCreateDialog() {
    setFormArea('')
    setFormKey('')
    setFormValue('')
    setFormFrom('')
    setFormTo('')
    setFormAgreement('')
    setFormOkVersion('')
    setCreateError(null)
    setCreateDialogOpen(true)
  }

  async function handleCreate() {
    if (!formArea || !formKey || !formValue || !formFrom || !formAgreement || !formOkVersion) {
      setCreateError('Udfyld venligst alle paakraevede felter.')
      return
    }
    setCreateLoading(true)
    setCreateError(null)
    try {
      await createOverride({
        configArea: formArea,
        configKey: formKey,
        configValue: formValue,
        effectiveFrom: formFrom,
        effectiveTo: formTo || undefined,
        agreementCode: formAgreement,
        okVersion: formOkVersion,
      })
      setCreateDialogOpen(false)
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : String(err))
    } finally {
      setCreateLoading(false)
    }
  }

  function openDeactivateDialog(configId: string) {
    setDeactivateTargetId(configId)
    setDeactivateError(null)
    setDeactivateDialogOpen(true)
  }

  async function handleDeactivate() {
    if (!deactivateTargetId) return
    setDeactivateLoading(true)
    setDeactivateError(null)
    try {
      await deactivateOverride(deactivateTargetId)
      setDeactivateDialogOpen(false)
      setDeactivateTargetId(null)
    } catch (err) {
      setDeactivateError(err instanceof Error ? err.message : String(err))
    } finally {
      setDeactivateLoading(false)
    }
  }

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

  return (
    <>
      <div className={styles.overridesHeader}>
        <h3 className={styles.sectionTitle}>Lokale tilpasninger</h3>
        <Button variant="primary" size="sm" onClick={openCreateDialog}>
          Opret tilpasning
        </Button>
      </div>

      {configs.length === 0 ? (
        <div className={styles.emptyState}>
          Ingen lokale tilpasninger fundet for denne organisation.
        </div>
      ) : (
        <Table
          headers={[
            'Omraade',
            'Noegle',
            'Vaerdi',
            'Gaelder fra',
            'Gaelder til',
            'Overenskomst',
            'OK-version',
            'Aktiv',
            'Handlinger',
          ]}
        >
          {configs.map((cfg: LocalConfiguration) => (
            <tr key={cfg.configId}>
              <td>
                <Badge>{cfg.configArea}</Badge>
              </td>
              <td>{cfg.configKey}</td>
              <td>{cfg.configValue}</td>
              <td>{formatDate(cfg.effectiveFrom)}</td>
              <td>{formatDate(cfg.effectiveTo)}</td>
              <td>{cfg.agreementCode}</td>
              <td>{cfg.okVersion}</td>
              <td>
                <Badge variant={cfg.isActive ? 'success' : 'default'}>
                  {cfg.isActive ? 'Aktiv' : 'Inaktiv'}
                </Badge>
              </td>
              <td>
                {cfg.isActive && (
                  <Button
                    variant="danger"
                    size="sm"
                    onClick={() => openDeactivateDialog(cfg.configId)}
                  >
                    Deaktiver
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </Table>
      )}

      {/* Create override dialog */}
      <Dialog
        open={createDialogOpen}
        onOpenChange={setCreateDialogOpen}
        title="Opret lokal tilpasning"
        description="Opret en ny lokal konfigurationstilpasning."
      >
        <div className={styles.dialogForm}>
          {createError && <Alert variant="error">{createError}</Alert>}

          <FormField label="Omraade" htmlFor="cfg-area" required>
            <Select
              id="cfg-area"
              options={CONFIG_AREA_OPTIONS}
              value={formArea}
              onValueChange={setFormArea}
              placeholder="Vaelg omraade..."
            />
          </FormField>

          <FormField label="Noegle" htmlFor="cfg-key" required>
            <Input
              id="cfg-key"
              type="text"
              value={formKey}
              onChange={(e: ChangeEvent<HTMLInputElement>) => setFormKey(e.target.value)}
              placeholder="Konfigurationsnoegle"
            />
          </FormField>

          <FormField label="Vaerdi" htmlFor="cfg-value" required>
            <Input
              id="cfg-value"
              type="text"
              value={formValue}
              onChange={(e: ChangeEvent<HTMLInputElement>) => setFormValue(e.target.value)}
              placeholder="Konfigurationsvaerdi"
            />
          </FormField>

          <FormField label="Gaelder fra" htmlFor="cfg-from" required>
            <Input
              id="cfg-from"
              type="date"
              value={formFrom}
              onChange={(e: ChangeEvent<HTMLInputElement>) => setFormFrom(e.target.value)}
            />
          </FormField>

          <FormField label="Gaelder til" htmlFor="cfg-to">
            <Input
              id="cfg-to"
              type="date"
              value={formTo}
              onChange={(e: ChangeEvent<HTMLInputElement>) => setFormTo(e.target.value)}
            />
          </FormField>

          <FormField label="Overenskomst" htmlFor="cfg-agreement" required>
            <Select
              id="cfg-agreement"
              options={AGREEMENT_OPTIONS}
              value={formAgreement}
              onValueChange={setFormAgreement}
              placeholder="Vaelg overenskomst..."
            />
          </FormField>

          <FormField label="OK-version" htmlFor="cfg-ok" required>
            <Select
              id="cfg-ok"
              options={OK_VERSION_OPTIONS}
              value={formOkVersion}
              onValueChange={setFormOkVersion}
              placeholder="Vaelg OK-version..."
            />
          </FormField>

          <div className={styles.dialogActions}>
            <Button
              variant="secondary"
              onClick={() => setCreateDialogOpen(false)}
              disabled={createLoading}
            >
              Annuller
            </Button>
            <Button
              variant="primary"
              onClick={handleCreate}
              disabled={createLoading}
            >
              {createLoading ? 'Opretter...' : 'Opret'}
            </Button>
          </div>
        </div>
      </Dialog>

      {/* Deactivate confirmation dialog */}
      <Dialog
        open={deactivateDialogOpen}
        onOpenChange={setDeactivateDialogOpen}
        title="Deaktiver tilpasning"
        description="Er du sikker paa, at du vil deaktivere denne konfigurationstilpasning?"
      >
        <div className={styles.dialogForm}>
          {deactivateError && <Alert variant="error">{deactivateError}</Alert>}

          <p className={styles.confirmText}>
            Denne handling vil deaktivere den lokale tilpasning. Den centrale
            konfiguration vil traede i kraft.
          </p>

          <div className={styles.dialogActions}>
            <Button
              variant="secondary"
              onClick={() => setDeactivateDialogOpen(false)}
              disabled={deactivateLoading}
            >
              Annuller
            </Button>
            <Button
              variant="danger"
              onClick={handleDeactivate}
              disabled={deactivateLoading}
            >
              {deactivateLoading ? 'Deaktiverer...' : 'Deaktiver'}
            </Button>
          </div>
        </div>
      </Dialog>
    </>
  )
}

// --- Tab 3: Central Constraints ---
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

// --- Main Component ---
export function ConfigManagement() {
  const { organizations } = useOrganizations()
  const [selectedOrgId, setSelectedOrgId] = useState('')

  const orgOptions = organizations.map((org: Organization) => ({
    value: org.orgId,
    label: `${org.orgName} (${org.orgId})`,
  }))

  const tabs = [
    {
      value: 'effective',
      label: 'Effektiv konfiguration',
      content: selectedOrgId ? (
        <EffectiveConfigTab orgId={selectedOrgId} />
      ) : (
        <div className={styles.emptyState}>
          Vaelg en organisation for at se konfiguration.
        </div>
      ),
    },
    {
      value: 'overrides',
      label: 'Lokale tilpasninger',
      content: selectedOrgId ? (
        <LocalOverridesTab orgId={selectedOrgId} />
      ) : (
        <div className={styles.emptyState}>
          Vaelg en organisation for at se lokale tilpasninger.
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

      <div className={styles.orgSelector}>
        <FormField label="Organisation" htmlFor="config-org-select" required>
          <Select
            id="config-org-select"
            options={orgOptions}
            value={selectedOrgId}
            onValueChange={setSelectedOrgId}
            placeholder="Vaelg organisation..."
          />
        </FormField>
      </div>

      <Tabs tabs={tabs} defaultValue="effective" />
    </div>
  )
}
