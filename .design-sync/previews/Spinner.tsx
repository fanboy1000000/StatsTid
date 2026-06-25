import { Spinner } from 'statstid-frontend'

// Spinner sweeps `size` ('sm' | 'md' | 'lg'). The component is intentionally tiny;
// see learnings ("known small") — a light surface + label make the cells legible.
export const Sizes = () => (
  <div style={{ display: 'flex', gap: 32, alignItems: 'center' }}>
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
      <Spinner size="sm" />
      <span style={{ fontSize: 12, color: '#6b7280' }}>sm</span>
    </div>
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
      <Spinner size="md" />
      <span style={{ fontSize: 12, color: '#6b7280' }}>md</span>
    </div>
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
      <Spinner size="lg" />
      <span style={{ fontSize: 12, color: '#6b7280' }}>lg</span>
    </div>
  </div>
)

// Inline next to text — the common "loading" composition.
export const InlineWithLabel = () => (
  <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
    <Spinner size="sm" />
    <span>Indlæser medarbejdere…</span>
  </div>
)
