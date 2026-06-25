import { Badge } from 'statstid-frontend'

// Badge sweeps `variant` ('default' | 'success' | 'error' | 'warning' | 'info').
export const Variants = () => (
  <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
    <Badge variant="default">Kladde</Badge>
    <Badge variant="info">Afventer</Badge>
    <Badge variant="success">Godkendt</Badge>
    <Badge variant="warning">Genåbnet</Badge>
    <Badge variant="error">Afvist</Badge>
  </div>
)

// Realistic status pills as they appear on the Teamoversigt roster.
export const StatusPills = () => (
  <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
    <Badge variant="warning">3 afventer godkendelse</Badge>
    <Badge variant="success">Sendt til løn</Badge>
    <Badge variant="error">2 mangler indsendelse</Badge>
  </div>
)
