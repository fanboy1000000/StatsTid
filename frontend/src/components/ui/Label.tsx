import { type ReactNode } from 'react'
import styles from './Label.module.css'

interface LabelProps {
  htmlFor: string
  required?: boolean
  children: ReactNode
}

export function Label({
  htmlFor,
  required = false,
  children,
}: LabelProps) {
  return (
    <label htmlFor={htmlFor} className={styles.label}>
      {children}
      {required && <span className={styles.required} aria-hidden="true"> *</span>}
    </label>
  )
}
