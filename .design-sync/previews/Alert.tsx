import { Alert } from 'statstid-frontend'

// Alert sweeps `variant` ('info' | 'success' | 'warning' | 'error'). The message
// is passed via `children` (there is no separate title prop) — a leading <strong>
// composes an optional inline title.
export const Variants = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 520 }}>
    <Alert variant="info">Lønkørslen for juni låses den 25. kl. 12.00.</Alert>
    <Alert variant="success">Ændringer gemt. Perioden er nu godkendt.</Alert>
    <Alert variant="warning">3 medarbejdere mangler stadig at indsende deres timer.</Alert>
    <Alert variant="error">Kunne ikke indsende perioden. Prøv igen.</Alert>
  </div>
)

export const WithTitle = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 520 }}>
    <Alert variant="success">
      <strong style={{ display: 'block', marginBottom: 4 }}>Indsendt</strong>
      Din arbejdstid for uge 26 er sendt til godkendelse.
    </Alert>
    <Alert variant="error">
      <strong style={{ display: 'block', marginBottom: 4 }}>Validering fejlede</strong>
      Den samlede arbejdstid overstiger 37 timer for ugen.
    </Alert>
  </div>
)

export const Dismissible = () => (
  <div style={{ maxWidth: 520 }}>
    <Alert variant="warning" onDismiss={() => {}}>
      Du har ikke-gemte ændringer i tidsregistreringen.
    </Alert>
  </div>
)
