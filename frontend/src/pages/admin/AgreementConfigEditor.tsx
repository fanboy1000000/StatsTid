import { useState, useEffect, useCallback, type ChangeEvent } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { apiClient } from '../../lib/api'
import { useAgreementConfigActions } from '../../hooks/useAgreementConfigs'
import type { AgreementConfig } from '../../hooks/useAgreementConfigs'
import styles from './AgreementConfigEditor.module.css'

type ConfigForm = Omit<AgreementConfig, 'configId' | 'createdBy' | 'createdAt' | 'updatedAt' | 'publishedAt' | 'archivedAt' | 'clonedFromId'>

const NORM_MODELS = [
  { value: 'WEEKLY_HOURS', label: 'Ugentlige timer' },
  { value: 'ANNUAL_ACTIVITY', label: 'Arlig aktivitet' },
]

const STATUS_LABELS: Record<string, string> = {
  DRAFT: 'Kladde',
  ACTIVE: 'Aktiv',
  ARCHIVED: 'Arkiveret',
}

function emptyForm(): ConfigForm {
  return {
    agreementCode: '',
    okVersion: '',
    status: 'DRAFT',
    description: '',
    normModel: 'WEEKLY_HOURS',
    weeklyNormHours: 37,
    normPeriodWeeks: 1,
    annualNormHours: 1924,
    maxFlexBalance: 150,
    flexCarryoverMax: 37,
    hasOvertime: false,
    hasMerarbejde: false,
    overtimeThreshold50: 0,
    overtimeThreshold100: 0,
    eveningSupplementEnabled: false,
    nightSupplementEnabled: false,
    weekendSupplementEnabled: false,
    holidaySupplementEnabled: false,
    eveningStart: 17,
    eveningEnd: 23,
    nightStart: 23,
    nightEnd: 6,
    eveningRate: 0,
    nightRate: 0,
    weekendSaturdayRate: 0,
    weekendSundayRate: 0,
    holidayRate: 0,
    onCallDutyEnabled: false,
    onCallDutyRate: 0,
    callInWorkEnabled: false,
    callInMinimumHours: 3,
    callInRate: 1,
    travelTimeEnabled: false,
    workingTravelRate: 1,
    nonWorkingTravelRate: 0.5,
  }
}

function configToForm(config: AgreementConfig): ConfigForm {
  return {
    agreementCode: config.agreementCode,
    okVersion: config.okVersion,
    status: config.status,
    description: config.description,
    normModel: config.normModel,
    weeklyNormHours: config.weeklyNormHours,
    normPeriodWeeks: config.normPeriodWeeks,
    annualNormHours: config.annualNormHours,
    maxFlexBalance: config.maxFlexBalance,
    flexCarryoverMax: config.flexCarryoverMax,
    hasOvertime: config.hasOvertime,
    hasMerarbejde: config.hasMerarbejde,
    overtimeThreshold50: config.overtimeThreshold50,
    overtimeThreshold100: config.overtimeThreshold100,
    eveningSupplementEnabled: config.eveningSupplementEnabled,
    nightSupplementEnabled: config.nightSupplementEnabled,
    weekendSupplementEnabled: config.weekendSupplementEnabled,
    holidaySupplementEnabled: config.holidaySupplementEnabled,
    eveningStart: config.eveningStart,
    eveningEnd: config.eveningEnd,
    nightStart: config.nightStart,
    nightEnd: config.nightEnd,
    eveningRate: config.eveningRate,
    nightRate: config.nightRate,
    weekendSaturdayRate: config.weekendSaturdayRate,
    weekendSundayRate: config.weekendSundayRate,
    holidayRate: config.holidayRate,
    onCallDutyEnabled: config.onCallDutyEnabled,
    onCallDutyRate: config.onCallDutyRate,
    callInWorkEnabled: config.callInWorkEnabled,
    callInMinimumHours: config.callInMinimumHours,
    callInRate: config.callInRate,
    travelTimeEnabled: config.travelTimeEnabled,
    workingTravelRate: config.workingTravelRate,
    nonWorkingTravelRate: config.nonWorkingTravelRate,
  }
}

function formatDateTime(dateStr: string | null): string {
  if (!dateStr) return '\u2014'
  try {
    return new Date(dateStr).toLocaleString('da-DK')
  } catch {
    return dateStr
  }
}

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'ACTIVE':
      return `${styles.badge} ${styles.badgeActive}`
    case 'ARCHIVED':
      return `${styles.badge} ${styles.badgeArchived}`
    default:
      return styles.badge
  }
}

export function AgreementConfigEditor() {
  const { configId } = useParams<{ configId: string }>()
  const navigate = useNavigate()
  const isNew = configId === 'new'

  const { createConfig, updateConfig, cloneConfig, publishConfig, archiveConfig } = useAgreementConfigActions()

  const [config, setConfig] = useState<AgreementConfig | null>(null)
  const [form, setForm] = useState<ConfigForm>(emptyForm())
  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [confirmDialog, setConfirmDialog] = useState<'publish' | 'archive' | null>(null)

  const isReadOnly = config !== null && config.status !== 'DRAFT'

  const fetchConfig = useCallback(async () => {
    if (isNew || !configId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<AgreementConfig>(`/api/agreement-configs/${configId}`)
    if (result.ok) {
      setConfig(result.data)
      setForm(configToForm(result.data))
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [configId, isNew])

  useEffect(() => { fetchConfig() }, [fetchConfig])

  const setField = <K extends keyof ConfigForm>(key: K, value: ConfigForm[K]) => {
    setForm((f) => ({ ...f, [key]: value }))
  }

  const handleTextChange = (key: keyof ConfigForm) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
    setField(key, e.target.value as ConfigForm[typeof key])
  }

  const handleNumberChange = (key: keyof ConfigForm) => (e: ChangeEvent<HTMLInputElement>) => {
    setField(key, parseFloat(e.target.value) || 0)
  }

  const handleCheckboxChange = (key: keyof ConfigForm) => (e: ChangeEvent<HTMLInputElement>) => {
    setField(key, e.target.checked as ConfigForm[typeof key])
  }

  const handleSave = async () => {
    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      if (isNew) {
        const result = await createConfig(form)
        navigate(`/admin/agreements/${result.configId}`, { replace: true })
      } else if (configId) {
        await updateConfig(configId, form)
        setSuccess('Konfiguration gemt')
        await fetchConfig()
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  const handlePublish = async () => {
    if (!configId) return
    setConfirmDialog(null)
    setSaving(true)
    setError(null)
    try {
      await publishConfig(configId)
      navigate('/admin/agreements')
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  const handleArchive = async () => {
    if (!configId) return
    setConfirmDialog(null)
    setSaving(true)
    setError(null)
    try {
      await archiveConfig(configId)
      navigate('/admin/agreements')
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  const handleClone = async () => {
    if (!configId) return
    setSaving(true)
    setError(null)
    try {
      const result = await cloneConfig(configId)
      navigate(`/admin/agreements/${result.configId}`)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return <div className={styles.page}><div className={styles.spinner}>Henter konfiguration...</div></div>
  }

  if (!isNew && !config && error) {
    return (
      <div className={styles.page}>
        <div className={styles.alert}>{error}</div>
        <button className={styles.secondaryBtn} onClick={() => navigate('/admin/agreements')}>Tilbage</button>
      </div>
    )
  }

  const status = config?.status ?? 'DRAFT'

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <div className={styles.headerLeft}>
          <h1 className={styles.title}>
            {isNew ? 'Ny overenskomst' : `${config?.agreementCode} / ${config?.okVersion}`}
          </h1>
          {!isNew && (
            <span className={statusBadgeClass(status)}>
              {STATUS_LABELS[status] ?? status}
            </span>
          )}
        </div>
        <div className={styles.headerActions}>
          {(isNew || status === 'DRAFT') && (
            <button className={styles.primaryBtn} onClick={handleSave} disabled={saving}>
              {saving ? 'Gemmer...' : 'Gem'}
            </button>
          )}
          {!isNew && status === 'DRAFT' && (
            <>
              <button className={styles.secondaryBtn} onClick={() => setConfirmDialog('publish')} disabled={saving}>
                Publicer
              </button>
              <button className={styles.dangerBtn} onClick={() => setConfirmDialog('archive')} disabled={saving}>
                Arkiver
              </button>
            </>
          )}
          {!isNew && (status === 'ACTIVE' || status === 'ARCHIVED') && (
            <button className={styles.secondaryBtn} onClick={handleClone} disabled={saving}>
              Klon som kladde
            </button>
          )}
          {!isNew && status === 'ACTIVE' && (
            <button className={styles.dangerBtn} onClick={() => setConfirmDialog('archive')} disabled={saving}>
              Arkiver
            </button>
          )}
          <button className={styles.secondaryBtn} onClick={() => navigate('/admin/agreements')}>
            Tilbage
          </button>
        </div>
      </div>

      {error && <div className={styles.alert}>{error}</div>}
      {success && <div className={styles.success}>{success}</div>}

      {/* Grundlaeggende */}
      <fieldset className={styles.fieldset}>
        <legend>Grundlaeggende</legend>
        <div className={styles.fieldGrid}>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Overenskomstkode</label>
            <input className={styles.input} type="text" value={form.agreementCode} onChange={handleTextChange('agreementCode')} readOnly={isReadOnly} placeholder="f.eks. AC" />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>OK-version</label>
            <input className={styles.input} type="text" value={form.okVersion} onChange={handleTextChange('okVersion')} readOnly={isReadOnly} placeholder="f.eks. OK24" />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Normmodel</label>
            <select className={styles.select} value={form.normModel} onChange={handleTextChange('normModel')} disabled={isReadOnly}>
              {NORM_MODELS.map((m) => (
                <option key={m.value} value={m.value}>{m.label}</option>
              ))}
            </select>
          </div>
          <div className={`${styles.formField} ${styles.fieldGridFull}`}>
            <label className={styles.formLabel}>Beskrivelse</label>
            <textarea className={styles.textarea} value={form.description ?? ''} onChange={handleTextChange('description')} readOnly={isReadOnly} placeholder="Valgfri beskrivelse" />
          </div>
        </div>
      </fieldset>

      {/* Normtid */}
      <fieldset className={styles.fieldset}>
        <legend>Normtid</legend>
        <div className={styles.fieldGrid}>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Ugentlige normtimer</label>
            <input className={styles.input} type="number" step="0.01" value={form.weeklyNormHours} onChange={handleNumberChange('weeklyNormHours')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Normperiode (uger)</label>
            <input className={styles.input} type="number" step="1" value={form.normPeriodWeeks} onChange={handleNumberChange('normPeriodWeeks')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Arlige normtimer</label>
            <input className={styles.input} type="number" step="0.01" value={form.annualNormHours} onChange={handleNumberChange('annualNormHours')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Flex */}
      <fieldset className={styles.fieldset}>
        <legend>Flex</legend>
        <div className={styles.fieldGrid}>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Maks. flexsaldo (timer)</label>
            <input className={styles.input} type="number" step="0.01" value={form.maxFlexBalance} onChange={handleNumberChange('maxFlexBalance')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Maks. flexoverforsel (timer)</label>
            <input className={styles.input} type="number" step="0.01" value={form.flexCarryoverMax} onChange={handleNumberChange('flexCarryoverMax')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Overarbejde */}
      <fieldset className={styles.fieldset}>
        <legend>Overarbejde</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="hasOvertime" checked={form.hasOvertime} onChange={handleCheckboxChange('hasOvertime')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="hasOvertime">Overarbejde aktiveret</label>
          </div>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="hasMerarbejde" checked={form.hasMerarbejde} onChange={handleCheckboxChange('hasMerarbejde')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="hasMerarbejde">Merarbejde aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Overarbejdsgraense 50% (timer)</label>
            <input className={styles.input} type="number" step="0.01" value={form.overtimeThreshold50} onChange={handleNumberChange('overtimeThreshold50')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Overarbejdsgraense 100% (timer)</label>
            <input className={styles.input} type="number" step="0.01" value={form.overtimeThreshold100} onChange={handleNumberChange('overtimeThreshold100')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Aftentillaeg */}
      <fieldset className={styles.fieldset}>
        <legend>Aftentillaeg</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="eveningSupplementEnabled" checked={form.eveningSupplementEnabled} onChange={handleCheckboxChange('eveningSupplementEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="eveningSupplementEnabled">Aftentillaeg aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Start (time)</label>
            <input className={styles.input} type="number" step="1" value={form.eveningStart} onChange={handleNumberChange('eveningStart')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Slut (time)</label>
            <input className={styles.input} type="number" step="1" value={form.eveningEnd} onChange={handleNumberChange('eveningEnd')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.eveningRate} onChange={handleNumberChange('eveningRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Nattillaeg */}
      <fieldset className={styles.fieldset}>
        <legend>Nattillaeg</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="nightSupplementEnabled" checked={form.nightSupplementEnabled} onChange={handleCheckboxChange('nightSupplementEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="nightSupplementEnabled">Nattillaeg aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Start (time)</label>
            <input className={styles.input} type="number" step="1" value={form.nightStart} onChange={handleNumberChange('nightStart')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Slut (time)</label>
            <input className={styles.input} type="number" step="1" value={form.nightEnd} onChange={handleNumberChange('nightEnd')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.nightRate} onChange={handleNumberChange('nightRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Weekendtillaeg */}
      <fieldset className={styles.fieldset}>
        <legend>Weekendtillaeg</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="weekendSupplementEnabled" checked={form.weekendSupplementEnabled} onChange={handleCheckboxChange('weekendSupplementEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="weekendSupplementEnabled">Weekendtillaeg aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Lordagssats</label>
            <input className={styles.input} type="number" step="0.01" value={form.weekendSaturdayRate} onChange={handleNumberChange('weekendSaturdayRate')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Sondagssats</label>
            <input className={styles.input} type="number" step="0.01" value={form.weekendSundayRate} onChange={handleNumberChange('weekendSundayRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Helligdagstillaeg */}
      <fieldset className={styles.fieldset}>
        <legend>Helligdagstillaeg</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="holidaySupplementEnabled" checked={form.holidaySupplementEnabled} onChange={handleCheckboxChange('holidaySupplementEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="holidaySupplementEnabled">Helligdagstillaeg aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.holidayRate} onChange={handleNumberChange('holidayRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Radighedsvagt */}
      <fieldset className={styles.fieldset}>
        <legend>Radighedsvagt</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="onCallDutyEnabled" checked={form.onCallDutyEnabled} onChange={handleCheckboxChange('onCallDutyEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="onCallDutyEnabled">Radighedsvagt aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.onCallDutyRate} onChange={handleNumberChange('onCallDutyRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Tilkald */}
      <fieldset className={styles.fieldset}>
        <legend>Tilkald</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="callInWorkEnabled" checked={form.callInWorkEnabled} onChange={handleCheckboxChange('callInWorkEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="callInWorkEnabled">Tilkald aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Minimumstimer</label>
            <input className={styles.input} type="number" step="0.01" value={form.callInMinimumHours} onChange={handleNumberChange('callInMinimumHours')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.callInRate} onChange={handleNumberChange('callInRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Rejsetid */}
      <fieldset className={styles.fieldset}>
        <legend>Rejsetid</legend>
        <div className={styles.fieldGrid}>
          <div className={`${styles.checkboxField} ${styles.fieldGridFull}`}>
            <input className={styles.checkbox} type="checkbox" id="travelTimeEnabled" checked={form.travelTimeEnabled} onChange={handleCheckboxChange('travelTimeEnabled')} disabled={isReadOnly} />
            <label className={styles.checkboxLabel} htmlFor="travelTimeEnabled">Rejsetid aktiveret</label>
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Arbejdsrejse-sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.workingTravelRate} onChange={handleNumberChange('workingTravelRate')} readOnly={isReadOnly} />
          </div>
          <div className={styles.formField}>
            <label className={styles.formLabel}>Ikke-arbejdsrejse-sats</label>
            <input className={styles.input} type="number" step="0.01" value={form.nonWorkingTravelRate} onChange={handleNumberChange('nonWorkingTravelRate')} readOnly={isReadOnly} />
          </div>
        </div>
      </fieldset>

      {/* Metadata */}
      {!isNew && config && (
        <div className={styles.metadataSection}>
          <h3 className={styles.metadataTitle}>Metadata</h3>
          <div className={styles.metadataGrid}>
            <span className={styles.metadataLabel}>Oprettet af</span>
            <span className={styles.metadataValue}>{config.createdBy}</span>
            <span className={styles.metadataLabel}>Oprettet</span>
            <span className={styles.metadataValue}>{formatDateTime(config.createdAt)}</span>
            <span className={styles.metadataLabel}>Opdateret</span>
            <span className={styles.metadataValue}>{formatDateTime(config.updatedAt)}</span>
            <span className={styles.metadataLabel}>Publiceret</span>
            <span className={styles.metadataValue}>{formatDateTime(config.publishedAt)}</span>
            <span className={styles.metadataLabel}>Klonet fra</span>
            <span className={styles.metadataValue}>{config.clonedFromId ?? '\u2014'}</span>
          </div>
        </div>
      )}

      {/* Confirm dialogs */}
      {confirmDialog && (
        <div className={styles.overlay} onClick={() => setConfirmDialog(null)}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>
              {confirmDialog === 'publish' ? 'Publicer overenskomst' : 'Arkiver overenskomst'}
            </h2>
            <p className={styles.dialogText}>
              {confirmDialog === 'publish'
                ? 'Er du sikker pa, at du vil publicere denne konfiguration? Den kan ikke redigeres efter publicering.'
                : 'Er du sikker pa, at du vil arkivere denne konfiguration?'}
            </p>
            <div className={styles.dialogActions}>
              <button className={styles.secondaryBtn} onClick={() => setConfirmDialog(null)}>
                Annuller
              </button>
              <button
                className={confirmDialog === 'publish' ? styles.primaryBtn : styles.dangerBtn}
                onClick={confirmDialog === 'publish' ? handlePublish : handleArchive}
              >
                {confirmDialog === 'publish' ? 'Publicer' : 'Arkiver'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
