import { type TextareaHTMLAttributes } from 'react'
import styles from './Textarea.module.css'

interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  id: string
  error?: boolean
}

export function Textarea({
  rows = 4,
  error = false,
  disabled = false,
  className,
  ...rest
}: TextareaProps) {
  const classNames = [
    styles.textarea,
    error ? styles.error : '',
    className,
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <textarea
      rows={rows}
      disabled={disabled}
      aria-invalid={error || undefined}
      className={classNames}
      {...rest}
    />
  )
}
