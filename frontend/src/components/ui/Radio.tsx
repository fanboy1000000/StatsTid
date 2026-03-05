import { type ChangeEvent } from 'react'
import styles from './Radio.module.css'

interface RadioProps {
  checked: boolean
  onChange: (e: ChangeEvent<HTMLInputElement>) => void
  label: string
  name: string
  value: string
  id: string
  disabled?: boolean
}

export function Radio({
  checked,
  onChange,
  label,
  name,
  value,
  id,
  disabled = false,
}: RadioProps) {
  return (
    <label htmlFor={id} className={styles.wrapper}>
      <span className={styles.control}>
        <input
          type="radio"
          id={id}
          name={name}
          value={value}
          checked={checked}
          onChange={onChange}
          disabled={disabled}
          className={styles.input}
        />
        <span
          className={`${styles.indicator} ${checked ? styles.selected : ''}`}
          aria-hidden="true"
        >
          {checked && <span className={styles.dot} />}
        </span>
      </span>
      <span className={styles.label}>{label}</span>
    </label>
  )
}
