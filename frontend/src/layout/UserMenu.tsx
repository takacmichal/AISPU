import { useEffect, useId, useRef, useState } from 'react'
import { Avatar } from '../components/Avatar'
import { ChevronIcon, SignOutIcon } from './NavIcon'
import { LAYOUT_TEXTS as T } from './texts'

interface UserMenuProps {
  name: string
  role?: string
  onSignOut: () => void
}

/** User button in the top bar with a small menu (menu button pattern: Escape and outside click close it). */
export function UserMenu({ name, role, onSignOut }: UserMenuProps) {
  const [open, setOpen] = useState(false)
  const menuId = useId()
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const firstItemRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    firstItemRef.current?.focus()
    const onPointer = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  function close() {
    setOpen(false)
    buttonRef.current?.focus()
  }

  return (
    <div ref={rootRef} className="user-menu" onKeyDown={(event) => event.key === 'Escape' && open && close()}>
      <button
        ref={buttonRef}
        type="button"
        className="user-menu__trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((value) => !value)}
      >
        <Avatar name={name} size="sm" />
        <span className="user-menu__text">
          <span className="user-menu__name">{name}</span>
          {role && <span className="user-menu__role">{role}</span>}
        </span>
        <ChevronIcon />
      </button>
      {open && (
        <div id={menuId} role="menu" aria-label={T.userMenu} className="user-menu__menu">
          <button
            ref={firstItemRef}
            type="button"
            role="menuitem"
            className="user-menu__item"
            onClick={() => {
              setOpen(false)
              onSignOut()
            }}
          >
            <SignOutIcon />
            {T.signOut}
          </button>
        </div>
      )}
    </div>
  )
}
