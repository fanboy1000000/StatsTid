import { type ReactNode } from 'react'
import * as TabsPrimitive from '@radix-ui/react-tabs'
import styles from './Tabs.module.css'

interface Tab {
  value: string
  label: string
  content: ReactNode
}

interface TabsProps {
  tabs: Tab[]
  defaultValue?: string
}

export function Tabs({ tabs, defaultValue }: TabsProps) {
  const resolvedDefault = defaultValue ?? tabs[0]?.value

  return (
    <TabsPrimitive.Root
      className={styles.root}
      defaultValue={resolvedDefault}
    >
      <TabsPrimitive.List className={styles.list}>
        {tabs.map((tab) => (
          <TabsPrimitive.Trigger
            key={tab.value}
            className={styles.trigger}
            value={tab.value}
          >
            {tab.label}
          </TabsPrimitive.Trigger>
        ))}
      </TabsPrimitive.List>
      {tabs.map((tab) => (
        <TabsPrimitive.Content
          key={tab.value}
          className={styles.content}
          value={tab.value}
        >
          {tab.content}
        </TabsPrimitive.Content>
      ))}
    </TabsPrimitive.Root>
  )
}
