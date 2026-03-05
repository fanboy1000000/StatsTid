import { type ReactNode } from 'react'
import styles from './Table.module.css'

interface TableProps {
  headers: string[]
  children: ReactNode
  striped?: boolean
}

export function Table({
  headers,
  children,
  striped = false,
}: TableProps) {
  return (
    <table className={`${styles.table} ${striped ? styles.striped : ''}`}>
      <thead>
        <tr>
          {headers.map((header) => (
            <th key={header} className={styles.th}>
              {header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>{children}</tbody>
    </table>
  )
}
