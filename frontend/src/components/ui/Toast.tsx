import {
  type ReactNode,
  createContext,
  useCallback,
  useContext,
  useState,
} from 'react'
import * as ToastPrimitive from '@radix-ui/react-toast'
import styles from './Toast.module.css'

interface ToastData {
  id: string
  title: string
  description?: string
  variant: 'default' | 'success' | 'error'
}

interface ToastContextValue {
  toast: (opts: {
    title: string
    description?: string
    variant?: 'default' | 'success' | 'error'
  }) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

let toastCounter = 0

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastData[]>([])

  const addToast = useCallback(
    (opts: {
      title: string
      description?: string
      variant?: 'default' | 'success' | 'error'
    }) => {
      const id = `toast-${++toastCounter}`
      setToasts((prev) => [
        ...prev,
        {
          id,
          title: opts.title,
          description: opts.description,
          variant: opts.variant ?? 'default',
        },
      ])
    },
    []
  )

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  return (
    <ToastContext.Provider value={{ toast: addToast }}>
      <ToastPrimitive.Provider duration={5000}>
        {children}
        {toasts.map((t) => (
          <ToastPrimitive.Root
            key={t.id}
            className={`${styles.toast} ${styles[t.variant]}`}
            onOpenChange={(open) => {
              if (!open) removeToast(t.id)
            }}
          >
            <ToastPrimitive.Title className={styles.title}>
              {t.title}
            </ToastPrimitive.Title>
            {t.description && (
              <ToastPrimitive.Description className={styles.description}>
                {t.description}
              </ToastPrimitive.Description>
            )}
            <ToastPrimitive.Close asChild>
              <button className={styles.close} aria-label="Close">
                &#x2715;
              </button>
            </ToastPrimitive.Close>
          </ToastPrimitive.Root>
        ))}
        <ToastPrimitive.Viewport className={styles.viewport} />
      </ToastPrimitive.Provider>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext)
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider')
  }
  return context
}
