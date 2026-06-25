import { FormField, Input, Textarea } from 'statstid-frontend'

export const Default = () => (
  <div style={{ maxWidth: 420 }}>
    <FormField label="Navn" htmlFor="ff-navn">
      <Input id="ff-navn" defaultValue="Emil Christensen" />
    </FormField>
  </div>
)

export const Required = () => (
  <div style={{ maxWidth: 420 }}>
    <FormField label="Email" htmlFor="ff-email" required>
      <Input id="ff-email" type="email" defaultValue="emil.christensen@stat.dk" />
    </FormField>
  </div>
)

export const WithError = () => (
  <div style={{ maxWidth: 420 }}>
    <FormField label="Email" htmlFor="ff-email-err" required error="Indtast en gyldig email-adresse">
      <Input id="ff-email-err" type="email" defaultValue="emil.christensen@" error />
    </FormField>
  </div>
)

export const WithTextarea = () => (
  <div style={{ maxWidth: 420 }}>
    <FormField label="Bemærkning" htmlFor="ff-bemaerkning">
      <Textarea
        id="ff-bemaerkning"
        defaultValue="Restferie overføres til næste ferieår jf. ferieaftalen."
      />
    </FormField>
  </div>
)
