import { Textarea } from 'statstid-frontend'

export const Default = () => (
  <div style={{ maxWidth: 480 }}>
    <Textarea
      id="textarea-bemaerkning"
      defaultValue="Medarbejderen har afholdt ferie i uge 27 og 28. Restferie overføres til næste ferieår."
    />
  </div>
)

export const Placeholder = () => (
  <div style={{ maxWidth: 480 }}>
    <Textarea id="textarea-placeholder" placeholder="Tilføj en bemærkning..." />
  </div>
)

export const Error = () => (
  <div style={{ maxWidth: 480 }}>
    <Textarea
      id="textarea-error"
      error
      defaultValue="Begrundelse mangler"
    />
  </div>
)

export const Disabled = () => (
  <div style={{ maxWidth: 480 }}>
    <Textarea
      id="textarea-disabled"
      disabled
      defaultValue="Perioden er godkendt og kan ikke redigeres."
    />
  </div>
)
