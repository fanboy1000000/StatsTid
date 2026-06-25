import { Checkbox } from 'statstid-frontend'

const noop = () => {}

export const Group = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    <Checkbox id="cb-ferie" label="Ferie" checked onChange={noop} />
    <Checkbox id="cb-sygdom" label="Sygdom" checked={false} onChange={noop} />
    <Checkbox id="cb-barns-sygdom" label="Barns første sygedag" checked={false} onChange={noop} />
    <Checkbox id="cb-omsorgsdag" label="Omsorgsdag" checked onChange={noop} />
  </div>
)

export const Checked = () => (
  <Checkbox id="cb-checked" label="Send til lønsystem" checked onChange={noop} />
)

export const Unchecked = () => (
  <Checkbox id="cb-unchecked" label="Send til lønsystem" checked={false} onChange={noop} />
)

export const Disabled = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    <Checkbox id="cb-disabled-on" label="Godkendt af leder" checked onChange={noop} disabled />
    <Checkbox id="cb-disabled-off" label="Afventer godkendelse" checked={false} onChange={noop} disabled />
  </div>
)
