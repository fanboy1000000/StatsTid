import { type ReactNode } from 'react'
import styles from './Card.module.css'

interface CardProps {
  children: ReactNode
  header?: ReactNode
  className?: string
}

export function Card({
  children,
  header,
  className,
}: CardProps) {
  const classNames = [styles.card, className].filter(Boolean).join(' ')

  return (
    <div className={classNames}>
      {header && <div className={styles.header}>{header}</div>}
      <div className={styles.body}>{children}</div>
    </div>
  )
}
