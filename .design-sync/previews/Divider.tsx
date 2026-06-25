import { Divider } from 'statstid-frontend'

// Divider is a zero-prop styled <hr> — a horizontal rule between two content blocks.
export const Between = () => (
  <div style={{ maxWidth: 360 }}>
    <div style={{ paddingBottom: 12 }}>
      <h3 style={{ margin: '0 0 4px' }}>Personoplysninger</h3>
      <p style={{ margin: 0 }}>Anne Sørensen · Digitaliseringsstyrelsen</p>
    </div>
    <Divider />
    <div style={{ paddingTop: 12 }}>
      <h3 style={{ margin: '0 0 4px' }}>Ansættelse</h3>
      <p style={{ margin: 0 }}>Overenskomst AC · 37 timer/uge</p>
    </div>
  </div>
)
