import { Table, Badge } from 'statstid-frontend'

// Employee roster — the real Table takes `headers: string[]` and raw <tr>/<td> children.
export const Roster = () => (
  <Table headers={['Navn', 'Afdeling', 'Status', 'Timer']}>
    <tr>
      <td>Anne Sørensen</td>
      <td>Digitaliseringsstyrelsen</td>
      <td><Badge variant="success">Godkendt</Badge></td>
      <td>160,5</td>
    </tr>
    <tr>
      <td>Mikkel Hansen</td>
      <td>Kontoret for Drift</td>
      <td><Badge variant="warning">Afventer</Badge></td>
      <td>148,0</td>
    </tr>
    <tr>
      <td>Sofie Bjerg</td>
      <td>HR &amp; Personale</td>
      <td><Badge variant="error">Afvist</Badge></td>
      <td>132,5</td>
    </tr>
    <tr>
      <td>Lars Nygaard</td>
      <td>Økonomi</td>
      <td><Badge variant="success">Godkendt</Badge></td>
      <td>156,0</td>
    </tr>
  </Table>
)

// Striped variant — same shape, alternating row backgrounds.
export const Striped = () => (
  <Table headers={['Dato', 'Timer', 'Overenskomst', 'Status']} striped>
    <tr>
      <td>03-06-2026</td>
      <td>7,5</td>
      <td>AC</td>
      <td><Badge variant="success">Godkendt</Badge></td>
    </tr>
    <tr>
      <td>04-06-2026</td>
      <td>7,5</td>
      <td>AC</td>
      <td><Badge variant="success">Godkendt</Badge></td>
    </tr>
    <tr>
      <td>05-06-2026</td>
      <td>4,0</td>
      <td>AC</td>
      <td><Badge variant="warning">Afventer</Badge></td>
    </tr>
  </Table>
)
