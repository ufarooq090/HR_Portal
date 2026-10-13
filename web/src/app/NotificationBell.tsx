import {
  Body1,
  Button,
  Caption1,
  CounterBadge,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Subtitle2,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components'
import { AlertRegular } from '@fluentui/react-icons'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useMarkNotificationRead, useNotifications } from '../api/requisitions'

const useStyles = makeStyles({
  trigger: { position: 'relative', color: tokens.colorNeutralForegroundOnBrand },
  badge: { position: 'absolute', top: '2px', right: '2px' },
  surface: { width: 'min(380px, 90vw)', maxHeight: '70vh', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  heading: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
  item: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    textAlign: 'left',
    gap: tokens.spacingVerticalXXS,
    width: '100%',
    padding: tokens.spacingVerticalS,
    border: 'none',
    borderRadius: tokens.borderRadiusMedium,
    background: 'none',
    cursor: 'pointer',
    color: tokens.colorNeutralForeground1,
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  unread: { backgroundColor: tokens.colorBrandBackground2 },
  body: { whiteSpace: 'pre-wrap', color: tokens.colorNeutralForeground2 },
})

/** SUP-05: in-app notification centre. Each notification is also emailed. */
export function NotificationBell() {
  const styles = useStyles()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const { data } = useNotifications()
  const markRead = useMarkNotificationRead()
  const unread = data?.unreadCount ?? 0

  return (
    <Popover open={open} onOpenChange={(_, d) => setOpen(d.open)} positioning="below-end">
      <PopoverTrigger disableButtonEnhancement>
        <Button
          appearance="transparent"
          className={styles.trigger}
          icon={<AlertRegular />}
          aria-label={unread ? `Notifications, ${unread} unread` : 'Notifications'}
        >
          {unread > 0 && <CounterBadge className={styles.badge} count={unread} size="small" color="danger" />}
        </Button>
      </PopoverTrigger>
      <PopoverSurface className={styles.surface} aria-label="Notifications">
        <div className={styles.heading}>
          <Subtitle2>Notifications</Subtitle2>
          {unread > 0 && (
            <Button size="small" appearance="subtle" onClick={() => markRead.mutate('all')}>
              Mark all read
            </Button>
          )}
        </div>
        {(data?.items.length ?? 0) === 0 && <Body1>Nothing yet.</Body1>}
        {data?.items.map((n) => (
          <button
            key={n.id}
            type="button"
            className={mergeClasses(styles.item, !n.isRead && styles.unread)}
            onClick={() => {
              if (!n.isRead) markRead.mutate(n.id)
              setOpen(false)
              void navigate(n.link)
            }}
          >
            <Body1>
              <strong>{n.title}</strong>
            </Body1>
            <Caption1 className={styles.body}>{n.body}</Caption1>
            <Caption1>{new Date(n.createdAtUtc).toLocaleString()}</Caption1>
          </button>
        ))}
      </PopoverSurface>
    </Popover>
  )
}
