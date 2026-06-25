import { Input } from 'statstid-frontend'

export const Default = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 360 }}>
    <Input id="input-navn" defaultValue="Emil Christensen" />
  </div>
)

export const Types = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 360 }}>
    <Input id="input-email" type="email" defaultValue="emil.christensen@stat.dk" />
    <Input id="input-dato" type="date" defaultValue="2026-06-24" />
    <Input id="input-search" type="search" placeholder="Søg medarbejder..." />
  </div>
)

export const Placeholder = () => (
  <div style={{ maxWidth: 360 }}>
    <Input id="input-placeholder" placeholder="Indtast afdeling..." />
  </div>
)

export const Error = () => (
  <div style={{ maxWidth: 360 }}>
    <Input id="input-error" error defaultValue="ugyldig-email" />
  </div>
)

export const Disabled = () => (
  <div style={{ maxWidth: 360 }}>
    <Input id="input-disabled" disabled defaultValue="Emil Christensen" />
  </div>
)
