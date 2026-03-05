import { type ReactNode } from 'react'
import * as DropdownMenuPrimitive from '@radix-ui/react-dropdown-menu'
import styles from './DropdownMenu.module.css'

interface DropdownMenuItem {
  label: string
  onClick: () => void
  variant?: 'default' | 'danger'
  disabled?: boolean
}

interface DropdownMenuProps {
  trigger: ReactNode
  items: DropdownMenuItem[]
}

export function DropdownMenu({ trigger, items }: DropdownMenuProps) {
  return (
    <DropdownMenuPrimitive.Root>
      <DropdownMenuPrimitive.Trigger asChild>
        {trigger}
      </DropdownMenuPrimitive.Trigger>

      <DropdownMenuPrimitive.Portal>
        <DropdownMenuPrimitive.Content
          className={styles.content}
          sideOffset={4}
          align="end"
        >
          {items.map((item, index) => (
            <DropdownMenuPrimitive.Item
              key={index}
              className={`${styles.item} ${item.variant === 'danger' ? styles.danger : ''}`}
              disabled={item.disabled}
              onSelect={item.onClick}
            >
              {item.label}
            </DropdownMenuPrimitive.Item>
          ))}
        </DropdownMenuPrimitive.Content>
      </DropdownMenuPrimitive.Portal>
    </DropdownMenuPrimitive.Root>
  )
}

export function DropdownMenuSeparator() {
  return <DropdownMenuPrimitive.Separator className={styles.separator} />
}
