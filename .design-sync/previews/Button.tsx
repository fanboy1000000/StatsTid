import { Button } from 'statstid-frontend'

export const Variants = () => (
  <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
    <Button variant="primary">Gem ændringer</Button>
    <Button variant="secondary">Annuller</Button>
    <Button variant="danger">Slet medarbejder</Button>
    <Button variant="ghost">Vis mere</Button>
  </div>
)

export const Sizes = () => (
  <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
    <Button size="sm">Lille</Button>
    <Button size="md">Mellem</Button>
    <Button size="lg">Stor</Button>
  </div>
)

export const Disabled = () => (
  <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
    <Button disabled>Indsend</Button>
    <Button variant="danger" disabled>Slet</Button>
  </div>
)
