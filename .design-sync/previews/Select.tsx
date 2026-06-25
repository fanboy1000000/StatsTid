import { Select } from 'statstid-frontend'

const noop = () => {}

const AGREEMENT_OPTIONS = [
  { value: 'AC', label: 'AC' },
  { value: 'AC_RESEARCH', label: 'AC_RESEARCH' },
  { value: 'AC_TEACHING', label: 'AC_TEACHING' },
  { value: 'HK', label: 'HK' },
  { value: 'PROSA', label: 'PROSA' },
]

const ORG_OPTIONS = [
  { value: 'STY01', label: 'Digitaliseringsstyrelsen' },
  { value: 'STY02', label: 'Skattestyrelsen' },
  { value: 'STY03', label: 'Arbejdstilsynet' },
]

const ROLE_OPTIONS = [
  { value: 'GLOBAL_ADMIN', label: 'Global administrator' },
  { value: 'LOCAL_ADMIN', label: 'Lokal administrator' },
  { value: 'LOCAL_HR', label: 'Lokal HR' },
  { value: 'LOCAL_LEADER', label: 'Lokal leder' },
  { value: 'EMPLOYEE', label: 'Medarbejder' },
]

export const Selected = () => (
  <div style={{ maxWidth: 320 }}>
    <Select id="sel-agreement" options={AGREEMENT_OPTIONS} value="AC" onValueChange={noop} />
  </div>
)

export const Placeholder = () => (
  <div style={{ maxWidth: 320 }}>
    <Select
      id="sel-org"
      options={ORG_OPTIONS}
      value=""
      onValueChange={noop}
      placeholder="Vælg organisation..."
    />
  </div>
)

export const RoleSelect = () => (
  <div style={{ maxWidth: 320 }}>
    <Select id="sel-role" options={ROLE_OPTIONS} value="LOCAL_HR" onValueChange={noop} />
  </div>
)

export const Disabled = () => (
  <div style={{ maxWidth: 320 }}>
    <Select id="sel-disabled" options={ORG_OPTIONS} value="STY01" onValueChange={noop} disabled />
  </div>
)
