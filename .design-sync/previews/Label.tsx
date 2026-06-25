import { Label, Input } from 'statstid-frontend'

export const Default = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxWidth: 360 }}>
    <Label htmlFor="label-navn">Navn</Label>
    <Input id="label-navn" defaultValue="Emil Christensen" />
  </div>
)

export const Required = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxWidth: 360 }}>
    <Label htmlFor="label-email" required>Email</Label>
    <Input id="label-email" type="email" defaultValue="emil.christensen@stat.dk" />
  </div>
)

export const WithAfdeling = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 6, maxWidth: 360 }}>
    <Label htmlFor="label-afdeling" required>Afdeling</Label>
    <Input id="label-afdeling" defaultValue="Løn & Personale" />
  </div>
)
