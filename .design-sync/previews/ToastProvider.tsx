import { ToastProvider } from 'statstid-frontend'

// ToastProvider is a CONTEXT PROVIDER. It wraps the app and renders nothing
// visible until a toast is fired imperatively via the `useToast()` hook
// (toast({ title, description, variant })). A static preview CANNOT trigger a
// toast — there is no static visual to capture. See learnings: this should be a
// FLOOR CARD or cfg.overrides.ToastProvider.skip. We do NOT hand-fake a toast UI
// the provider doesn't render itself; we render the provider honestly wrapping a
// short explanatory block.

export const Provider = () => (
  <ToastProvider>
    <div
      style={{
        padding: 24,
        maxWidth: 420,
        color: '#374151',
        fontSize: 14,
        lineHeight: 1.5,
      }}
    >
      <strong>ToastProvider</strong> — kontekst-udbyder uden statisk visning. Toasts
      vises imperativt via <code>useToast()</code>, fx ved «Ændringer gemt».
    </div>
  </ToastProvider>
)
