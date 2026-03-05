import { type ReactNode } from 'react'
import styles from './Alert.module.css'

interface AlertProps {
  variant: 'info' | 'success' | 'warning' | 'error'
  children: ReactNode
  onDismiss?: () => void
}

export function Alert({
  variant,
  children,
  onDismiss,
}: AlertProps) {
  return (
    <div className={`${styles.alert} ${styles[variant]}`} role="alert">
      <div className={styles.content}>{children}</div>
      {onDismiss && (
        <button
          type="button"
          className={styles.dismiss}
          onClick={onDismiss}
          aria-label="Luk besked"
        >
          <svg
            width="16"
            height="16"
            viewBox="0 0 16 16"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
            aria-hidden="true"
          >
            <path
              d="M4 4L12 12M12 4L4 12"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="square"
            />
          </svg>
        </button>
      )}
    </div>
  )
}
