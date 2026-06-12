import { type ReactNode } from 'react'
import * as DialogPrimitive from '@radix-ui/react-dialog'
import styles from './Dialog.module.css'

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  /** S72 / TASK-7204 — optional class merged onto the content panel (additive,
      mirrors the Button/Input `className` passthrough convention) so callers can
      widen the default 500px panel (the manager modal's 720px per the handoff). */
  contentClassName?: string
  children: ReactNode
}

export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  contentClassName,
  children,
}: DialogProps) {
  const contentClass = contentClassName
    ? `${styles.content} ${contentClassName}`
    : styles.content
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className={styles.overlay} />
        <DialogPrimitive.Content className={contentClass}>
          <DialogPrimitive.Title className={styles.title}>
            {title}
          </DialogPrimitive.Title>
          {description && (
            <DialogPrimitive.Description className={styles.description}>
              {description}
            </DialogPrimitive.Description>
          )}
          <div className={styles.body}>{children}</div>
          <DialogPrimitive.Close asChild>
            <button className={styles.close} aria-label="Close">
              &#x2715;
            </button>
          </DialogPrimitive.Close>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}
