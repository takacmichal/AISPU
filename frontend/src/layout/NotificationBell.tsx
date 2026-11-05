import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { Dialog } from '../components/Dialog'
import { Spinner } from '../components/Spinner'
import { readSeenIds, useNotifications, writeSeenIds, type NotificationItem } from '../features/notifications/notifications'
import { formatRelative } from '../lib/format'
import { MESSAGES } from '../lib/messages'
import { BellIcon } from './NavIcon'
import { LAYOUT_TEXTS as T } from './texts'

/** Items the bell still shows as unread: everything loaded that the user has not seen yet. */
function countUnread(items: NotificationItem[] | undefined, seen: Set<string>): number {
  return items ? items.filter((item) => !seen.has(item.id)).length : 0
}

/**
 * Top bar bell that opens a modal dialog with the notification list.
 * Opening it marks the loaded items as seen. Escape, the close button and a backdrop click close it and return focus to the bell.
 */
export function NotificationBell() {
  const { items, failed, reload } = useNotifications()
  const [open, setOpen] = useState(false)
  const [seenIds, setSeenIds] = useState<string[]>(readSeenIds)
  // While the dialog is open every loaded item counts as seen, including ones that arrive with a later poll.
  const seen = useMemo(() => new Set(open && items ? [...seenIds, ...items.map((item) => item.id)] : seenIds), [open, items, seenIds])
  const unread = countUnread(items, seen)
  const buttonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    writeSeenIds(seenIds)
  }, [seenIds])

  /** Stores the ids of the loaded items as seen; called when the dialog opens and when it closes. */
  function persistLoadedAsSeen() {
    if (!items) return
    const ids = items.map((item) => item.id)
    setSeenIds((previous) => [...previous.filter((id) => !ids.includes(id)), ...ids])
  }

  function openDialog() {
    persistLoadedAsSeen()
    setOpen(true)
  }

  function closeDialog() {
    persistLoadedAsSeen()
    setOpen(false)
    buttonRef.current?.focus()
  }

  const label = T.bell.label(unread)

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className="topbar__round notification-bell__trigger"
        aria-label={label}
        title={T.notifications}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={openDialog}
      >
        <BellIcon />
        {unread > 0 && (
          <span className="topbar__badge" aria-hidden="true">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>
      {/* Portal: the top bar uses backdrop-filter, which would otherwise confine the fixed backdrop to the bar. */}
      {open &&
        createPortal(
          <Dialog title={T.bell.panelLabel} onClose={closeDialog} dismissOnBackdrop>
            <NotificationList items={items} failed={failed} onRetry={reload} onOpenLink={closeDialog} />
            <div className="dialog__actions">
              <button type="button" className="btn btn--ghost" onClick={closeDialog}>
                {MESSAGES.close}
              </button>
            </div>
          </Dialog>,
          document.body,
        )}
    </>
  )
}

interface NotificationListProps {
  items?: NotificationItem[]
  failed: boolean
  onRetry: () => void
  onOpenLink: () => void
}

function NotificationList({ items, failed, onRetry, onOpenLink }: NotificationListProps) {
  if (items === undefined && !failed) return <Spinner label={T.bell.loading} />
  if (items === undefined) {
    return (
      <div className="notification-bell__state" role="alert">
        <p>{T.bell.error}</p>
        <button type="button" className="btn btn--sm" onClick={onRetry}>
          {T.bell.retry}
        </button>
      </div>
    )
  }
  if (items.length === 0) return <p className="notification-bell__state">{T.bell.empty}</p>
  return (
    <ul className="notification-bell__list">
      {items.map((item) => (
        <li key={item.id}>
          <Link to={item.link} className="notification-bell__item" onClick={onOpenLink}>
            <span className="notification-bell__kind">{T.bell.kind[item.kind]}</span>
            <span className="notification-bell__item-title">{item.title}</span>
            <time dateTime={item.createdAt} className="notification-bell__time">
              {formatRelative(item.createdAt)}
            </time>
          </Link>
        </li>
      ))}
    </ul>
  )
}
