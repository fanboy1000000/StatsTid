import { Card, Button, Badge } from 'statstid-frontend'

// Simple card — body content only (no header).
export const Simple = () => (
  <div style={{ maxWidth: 360 }}>
    <Card>
      <h3 style={{ margin: '0 0 4px' }}>Anne Sørensen</h3>
      <p style={{ margin: 0 }}>Fuldmægtig · Digitaliseringsstyrelsen</p>
    </Card>
  </div>
)

// Card with a header region (title + action) and a richer body.
export const WithHeader = () => (
  <div style={{ maxWidth: 360 }}>
    <Card
      header={
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
          }}
        >
          <h3 style={{ margin: 0 }}>Flexsaldo</h3>
          <Badge variant="success">Ajour</Badge>
        </div>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div style={{ fontSize: 32, fontWeight: 600 }}>+12,5 timer</div>
        <p style={{ margin: 0 }}>Optjent denne måned i Kontoret for Drift.</p>
        <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
          <Button variant="primary" size="sm">Se detaljer</Button>
          <Button variant="ghost" size="sm">Afspadser</Button>
        </div>
      </div>
    </Card>
  </div>
)
