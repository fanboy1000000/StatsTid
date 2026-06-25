import { Tabs, Badge } from 'statstid-frontend'

// Tabs takes a `tabs: { value, label, content }[]` array prop and is uncontrolled
// (`defaultValue` selects the initially-active tab). Static render → active panel shows.
export const Default = () => (
  <div style={{ maxWidth: 520 }}>
    <Tabs
      defaultValue="oversigt"
      tabs={[
        {
          value: 'oversigt',
          label: 'Oversigt',
          content: (
            <div style={{ padding: '8px 0' }}>
              <h3 style={{ margin: '0 0 8px' }}>Oversigt</h3>
              <p style={{ margin: 0 }}>
                160,5 timer registreret i juni · 12,5 timer i flex · 3 dage ferie tilbage.
              </p>
            </div>
          ),
        },
        {
          value: 'skema',
          label: 'Skema',
          content: (
            <div style={{ padding: '8px 0' }}>
              <h3 style={{ margin: '0 0 8px' }}>Skema</h3>
              <p style={{ margin: 0 }}>Standardskema 37 timer/uge · Overenskomst AC.</p>
            </div>
          ),
        },
        {
          value: 'indstillinger',
          label: 'Indstillinger',
          content: (
            <div style={{ padding: '8px 0' }}>
              <h3 style={{ margin: '0 0 8px' }}>Indstillinger</h3>
              <p style={{ margin: 0 }}>Notifikationer, sprog og godkendelsesflow.</p>
            </div>
          ),
        },
      ]}
    />
  </div>
)

// `label` is ReactNode (widened S72) — triggers can carry inline count badges.
export const WithBadge = () => (
  <div style={{ maxWidth: 520 }}>
    <Tabs
      defaultValue="projekter"
      tabs={[
        {
          value: 'projekter',
          label: (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              Projekter <Badge variant="info">3</Badge>
            </span>
          ),
          content: (
            <p style={{ margin: 0, padding: '8px 0' }}>
              Tilknyttede projekter: Sagsbehandling, Drift, Digital Post.
            </p>
          ),
        },
        {
          value: 'medarbejdere',
          label: 'Medarbejdere',
          content: (
            <p style={{ margin: 0, padding: '8px 0' }}>
              19 medarbejdere i Kontoret for Drift.
            </p>
          ),
        },
      ]}
    />
  </div>
)
