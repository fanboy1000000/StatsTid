import { type InputHTMLAttributes } from 'react'
import styles from './Input.module.css'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  id: string
  error?: boolean
}

export function Input({
  type = 'text',
  error = false,
  disabled = false,
  className,
  ...rest
}: InputProps) {
  const classNames = [
    styles.input,
    error ? styles.error : '',
    className,
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <input
      type={type}
      disabled={disabled}
      aria-invalid={error || undefined}
      className={classNames}
      {...rest}
    />
  )
}
