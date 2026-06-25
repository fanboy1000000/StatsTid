import { Dialog, Button, FormField, Input } from 'statstid-frontend'

// Dialog is a Radix portal modal controlled by `open` + `onOpenChange`. Rendered
// here in the OPEN state so the panel paints inside the card. It portals to
// document.body, so the suggested override pins a single, tall-enough viewport:
//   cfg.overrides.Dialog = { "cardMode": "single", "viewport": "560x420" }
// onOpenChange is a no-op here (preview is static — we keep it open).

export const Open = () => (
  <Dialog
    open={true}
    onOpenChange={() => {}}
    title="Tildel rolle"
    description="Tildel en ny rolle til brugeren."
  >
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <FormField label="Rolle" htmlFor="dlg-role">
        <Input id="dlg-role" defaultValue="Lokal HR-administrator" />
      </FormField>
      <FormField label="Organisation" htmlFor="dlg-org">
        <Input id="dlg-org" defaultValue="Styrelsen for Arbejdsmarked" />
      </FormField>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 8 }}>
        <Button variant="secondary">Annuller</Button>
        <Button variant="primary">Tildel rolle</Button>
      </div>
    </div>
  </Dialog>
)

export const Confirm = () => (
  <Dialog
    open={true}
    onOpenChange={() => {}}
    title="Fjern rolle"
    description="Er du sikker på, at du vil fjerne denne rolle?"
  >
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <p style={{ margin: 0 }}>
        Rollen <strong>Lokal HR-administrator</strong> fjernes fra Mette Sørensen.
        Handlingen kan ikke fortrydes.
      </p>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12 }}>
        <Button variant="secondary">Annuller</Button>
        <Button variant="danger">Fjern rolle</Button>
      </div>
    </div>
  </Dialog>
)
