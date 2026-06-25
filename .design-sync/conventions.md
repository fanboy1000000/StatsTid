# StatsTid UI Kit — conventions for building with this library

StatsTid is a Danish public-sector (statslig) HR/time-registration app. The look is the Danish government **oes** design language: **IBM Plex Sans**, **oes green** as the single brand colour, **0px corner radius** (sharp, square edges — never round buttons/cards), hairline borders, restrained status colours. Build screens that read as a calm, dense government back-office tool. Use Danish UI text.

## Setup — what to wrap
- Import components from the bundle: `import { Button, Card, Table } from 'statstid-frontend'`. The kit's styles (tokens + IBM Plex Sans + component CSS) load globally from the bound `styles.css` — **no ThemeProvider is needed for styling**; tokens are plain global CSS custom properties.
- **Only one provider exists:** `ToastProvider`. Wrap the app in it **only if** you fire toast notifications; toasts are then triggered imperatively with the `useToast()` hook (`const { toast } = useToast(); toast({ title, variant })`), not rendered as JSX.
- **Overlays are controlled, not port-triggered by you:** `Dialog` (`open` + `onOpenChange`) and `Drawer` (`open` + `onClose` + a required `ariaLabel`) take a boolean `open`. Render them alongside your page and drive `open` from state.

## Styling idiom — props first, then tokens (NO utility classes)
This kit has **no Tailwind / no utility-class vocabulary**. Style in two ways only:
1. **Component props carry the design language.** Sweep the real enums — never hand-roll a coloured `<div>` when a component variant exists:
   - `Button` — `variant`: `primary | secondary | danger | ghost`, `size`: `sm | md | lg`.
   - `Badge` / `Alert` — `variant`: `default|success|error|warning|info` (Alert: `info|success|warning|error`, message is `children`, `onDismiss?` adds an ×).
   - `Input` / `Textarea` — `error?: boolean` (red border); require an `id`.
   - `FormField` — the label+error wrapper (`label`, `htmlFor`, `required?`, `error?: string`).
   - `Table` — `headers: string[]` + raw `<tr>/<td>` rows (not compound). `Tabs` — a `tabs[]` array, `defaultValue`. `Card` — optional `header` slot.
2. **For your own layout glue, use the CSS custom-property tokens** (defined globally; read the bound `styles.css`). Real token families:
   - Colour: `--color-primary` (oes green) + `-hover/-dark/-darker`; `--color-text` / `-secondary` / `-muted`; `--color-bg` / `-surface` / `-bg-subtle` / `-bg-muted`; `--color-border` / `-border-strong`; status `--color-success` / `-error` / `-warning` / `-info` (+ `-light`), `--color-danger`; `--color-gray-50…600`.
   - Spacing: `--space-1`=4px … `--space-4`=16px … `--space-12`=48px (use these for gap/padding/margin).
   - Type: `--font-family` (IBM Plex Sans), `--font-weight-regular|medium|semibold|bold`.
   - Shape: **`--border-radius: 0px`** (keep corners square — a brand trait), `--border-width`, `--shadow-sm`, `--focus-outline`.
   - Example glue: `<div style={{ display: 'flex', gap: 'var(--space-3)', padding: 'var(--space-4)', background: 'var(--color-surface)', border: '1px solid var(--color-border)' }}>`.

## Where the truth lives
Read the bound `styles.css` (and its `@import`ed tokens) for the exact token values, and each component's `<Name>.d.ts` (the prop API) + `<Name>.prompt.md` (usage) before composing. Prefer a library component for any control; use tokens only for the surrounding layout.

## One idiomatic example
```tsx
import { Card, Button, Badge } from 'statstid-frontend'

export function FlexSaldoCard() {
  return (
    <Card header={<div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
      <strong>Flexsaldo</strong><Badge variant="success">Ajour</Badge>
    </div>}>
      <div style={{ fontSize: 28, fontWeight: 'var(--font-weight-bold)' }}>+12,5 timer</div>
      <p style={{ color: 'var(--color-text-secondary)', margin: 'var(--space-2) 0 var(--space-4)' }}>
        Optjent denne måned i Kontoret for Drift.
      </p>
      <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
        <Button variant="primary" size="sm">Se detaljer</Button>
        <Button variant="ghost" size="sm">Afspadser</Button>
      </div>
    </Card>
  )
}
```
