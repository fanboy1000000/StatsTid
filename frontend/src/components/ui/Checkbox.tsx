import { type ChangeEvent } from 'react'
import styles from './Checkbox.module.css'

interface CheckboxProps {
  checked: boolean
  onChange: (e: ChangeEvent<HTMLInputElement>) => void
  label: string
  id: string
  disabled?: boolean
}

export function Checkbox({
  checked,
  onChange,
  label,
  id,
  disabled = false,
}: CheckboxProps) {
  return (
    <label htmlFor={id} className={styles.wrapper}>
      <span className={styles.control}>
        <input
          type="checkbox"
          id={id}
          checked={checked}
          onChange={onChange}
          disabled={disabled}
          className={styles.input}
        />
        <span
          className={`${styles.indicator} ${checked ? styles.checked : ''}`}
          aria-hidden="true"
        >
          {checked && (
            <svg
              width="12"
              height="12"
              viewBox="0 0 12 12"
              fill="none"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                d="M2 6L5 9L10 3"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="square"
              />
            </svg>
          )}
        </span>
      </span>
      <span className={styles.label}>{label}</span>
    </label>
  )
}
