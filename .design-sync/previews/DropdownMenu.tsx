import { DropdownMenu, Button } from 'statstid-frontend'

// NOTE: DropdownMenu wraps Radix <Root> WITHOUT exposing an `open`/`defaultOpen` prop,
// so the menu cannot be forced open statically — only the trigger renders at rest.
// Items are real and click-driven: clicking the trigger opens the portal menu in a live app.
// API: trigger: ReactNode · items: { label, onClick, variant?: 'default'|'danger', disabled? }[]

const noop = () => {}

// Row-action menu (the roster "…" / "Handlinger" pattern).
export const RowActions = () => (
  <div style={{ display: 'flex', justifyContent: 'flex-end', padding: 16 }}>
    <DropdownMenu
      trigger={<Button variant="ghost" size="sm">Handlinger ▾</Button>}
      items={[
        { label: 'Rediger', onClick: noop },
        { label: 'Flyt', onClick: noop },
        { label: 'Slet', onClick: noop, variant: 'danger' },
      ]}
    />
  </div>
)

// Menu with a disabled item (e.g. an action gated by org-scope).
export const WithDisabled = () => (
  <div style={{ display: 'flex', justifyContent: 'flex-end', padding: 16 }}>
    <DropdownMenu
      trigger={<Button variant="secondary" size="sm">Indstillinger ▾</Button>}
      items={[
        { label: 'Omdøb', onClick: noop },
        { label: 'Genåbn periode', onClick: noop, disabled: true },
        { label: 'Fjern medarbejder', onClick: noop, variant: 'danger' },
      ]}
    />
  </div>
)
