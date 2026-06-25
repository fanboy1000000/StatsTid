import { Tooltip, Button } from 'statstid-frontend'

// Tooltip is Radix hover/focus-driven: the bubble portals on hover/focus and will
// NOT show in a static capture. This cell renders the TRIGGER with the `content`
// prop set — honest and real, but the bubble may not appear statically. See
// learnings: candidate for cfg.overrides.Tooltip.skip OR keep as a single
// trigger-only card. We do NOT hand-fake the bubble.

export const Trigger = () => (
  <div style={{ display: 'flex', gap: 24, alignItems: 'center', padding: 24 }}>
    <Tooltip content="Genåbn perioden for redigering">
      <Button variant="ghost">Genåbn</Button>
    </Tooltip>
    <Tooltip content="Perioden er sendt til løn og kan ikke ændres" side="bottom">
      <Button variant="ghost" disabled>
        Indsend
      </Button>
    </Tooltip>
  </div>
)
