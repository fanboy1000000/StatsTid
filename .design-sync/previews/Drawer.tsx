import { Drawer, Button, FormField, Input } from 'statstid-frontend'

// Drawer is a scratch-built right-side panel (no Radix) controlled by `open` +
// `onClose`, with a REQUIRED `ariaLabel` (it has no intrinsic title element). It
// returns null when !open, so we render with open={true} and supply our own
// heading/footer inside `children`. It portals to document.body — suggested pin:
//   cfg.overrides.Drawer = { "cardMode": "single", "viewport": "420x560" }
// onClose is a no-op here (preview is static — we keep it open).

export const Open = () => (
  <Drawer open={true} onClose={() => {}} ariaLabel="Rediger medarbejder">
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, padding: 24, height: '100%' }}>
      <h2 style={{ margin: 0, fontSize: 18 }}>Rediger medarbejder</h2>
      <FormField label="Fulde navn" htmlFor="drw-name">
        <Input id="drw-name" defaultValue="Mette Sørensen" />
      </FormField>
      <FormField label="Stillingsbetegnelse" htmlFor="drw-title">
        <Input id="drw-title" defaultValue="Fuldmægtig" />
      </FormField>
      <FormField label="Enhed" htmlFor="drw-unit">
        <Input id="drw-unit" defaultValue="Ydelseskontoret" />
      </FormField>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 'auto' }}>
        <Button variant="secondary">Annuller</Button>
        <Button variant="primary">Gem ændringer</Button>
      </div>
    </div>
  </Drawer>
)
