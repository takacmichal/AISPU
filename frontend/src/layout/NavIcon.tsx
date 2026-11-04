import type { ReactNode } from 'react'

/** 16px single-color line icon; decorative, the surrounding link or button carries the accessible name. */
function Icon({ children }: { children: ReactNode }) {
  return (
    <svg
      className="nav-icon"
      width="16"
      height="16"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  )
}

/** Sidebar icon for a navigation path (falls back to a neutral dot). */
export function NavIcon({ path }: { path: string }) {
  switch (path) {
    case '/app':
      return (
        <Icon>
          <rect x="2" y="2" width="5" height="5" rx="1" />
          <rect x="9" y="2" width="5" height="5" rx="1" />
          <rect x="2" y="9" width="5" height="5" rx="1" />
          <rect x="9" y="9" width="5" height="5" rx="1" />
        </Icon>
      )
    case '/customers':
      return (
        <Icon>
          <circle cx="6" cy="5.5" r="2.5" />
          <path d="M1.75 13.5c.5-2.3 2.2-3.5 4.25-3.5s3.75 1.2 4.25 3.5" />
          <path d="M10.5 3.2a2.5 2.5 0 0 1 0 4.6M12 10.3c1.2.5 2 1.6 2.25 3.2" />
        </Icon>
      )
    case '/catalog':
      return (
        <Icon>
          <path d="M8 1.75 14 5 8 8.25 2 5 8 1.75Z" />
          <path d="m2 8 6 3.25L14 8M2 11l6 3.25L14 11" />
        </Icon>
      )
    case '/designs':
      return (
        <Icon>
          <path d="M2.5 11.5 5 4.5h6l2.5 7h-11Z" />
          <path d="M6.25 4.5 5.5 11.5M9.75 4.5l.75 7M3.6 8h8.8M8 11.5v2.5M5.5 14h5" />
        </Icon>
      )
    case '/quotes':
      return (
        <Icon>
          <path d="M4 1.75h5.5L12.5 4.75v9.5H4V1.75Z" />
          <path d="M9.25 1.75v3.25h3.25M6 8.5h4.5M6 11h3" />
        </Icon>
      )
    case '/contracts':
      return (
        <Icon>
          <path d="M3.5 1.75h9v12.5h-9z" />
          <path d="M5.75 5h4.5M5.75 7.5h4.5M5.75 11.25c.8-.9 1.4-.9 1.9 0 .4.7.9.7 1.5 0" />
        </Icon>
      )
    case '/installations':
      return (
        <Icon>
          <path d="M9.6 2.4a3 3 0 0 0-3.9 3.9L2 10a1.4 1.4 0 0 0 2 2l3.7-3.7a3 3 0 0 0 3.9-3.9L9.75 6.25l-1.5-.5-.5-1.5 1.85-1.85Z" />
        </Icon>
      )
    case '/monitoring':
      return (
        <Icon>
          <path d="M2 13.5h12M3 10.5l3-3.5 2.5 2 4.5-5.5" />
        </Icon>
      )
    case '/tickets':
      return (
        <Icon>
          <path d="M2 4.5h12v2a1.5 1.5 0 0 0 0 3v2H2v-2a1.5 1.5 0 0 0 0-3v-2Z" />
          <path d="M10 4.5v7" strokeDasharray="1.25 1.5" />
        </Icon>
      )
    case '/my-plants':
      return (
        <Icon>
          <circle cx="8" cy="8" r="2.75" />
          <path d="M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1.05 1.05M11.55 11.55l1.05 1.05M3.4 12.6l1.05-1.05M11.55 4.45l1.05-1.05" />
        </Icon>
      )
    case '/admin/users':
      return (
        <Icon>
          <path d="M8 1.75 13 3.5v4c0 3-2.1 5.1-5 6.75-2.9-1.65-5-3.75-5-6.75v-4l5-1.75Z" />
          <path d="m5.75 8 1.6 1.6 2.9-3.1" />
        </Icon>
      )
    default:
      return (
        <Icon>
          <circle cx="8" cy="8" r="2" />
        </Icon>
      )
  }
}

/** SolarOps logo glyph (sun over a panel), from the landing page design. */
export function BrandGlyph() {
  return (
    <svg
      className="brand-glyph"
      width="28"
      height="28"
      viewBox="0 0 32 32"
      fill="none"
      stroke="var(--color-text)"
      strokeWidth="1.5"
      strokeLinecap="round"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="16" cy="9" r="4" stroke="var(--color-accent)" />
      <path d="M16 2v1.5M9.5 4.5l1 1M22.5 4.5l-1 1M7 9h1.5M23.5 9H25" stroke="var(--color-accent)" />
      <path d="M6 28l3-12h14l3 12z" />
      <path d="M7.5 22h17M12.5 16l-1.2 12M19.5 16l1.2 12" />
    </svg>
  )
}

export function SignOutIcon() {
  return (
    <Icon>
      <path d="M6.5 2.5H3.5v11h3M10.5 5l3 3-3 3M13.25 8H6.5" />
    </Icon>
  )
}

export function MenuIcon() {
  return (
    <Icon>
      <path d="M2.5 4.5h11M2.5 8h11M2.5 11.5h11" />
    </Icon>
  )
}

export function SearchIcon() {
  return (
    <Icon>
      <circle cx="7" cy="7" r="4.5" />
      <path d="m10.5 10.5 3 3" />
    </Icon>
  )
}

export function ChevronIcon() {
  return (
    <Icon>
      <path d="m4.5 6.5 3.5 3.5 3.5-3.5" />
    </Icon>
  )
}

/** Double chevron; rotated by CSS when the sidebar is collapsed. */
export function CollapseIcon() {
  return (
    <Icon>
      <path d="M9.5 4.5 6 8l3.5 3.5M13 4.5 9.5 8l3.5 3.5" />
    </Icon>
  )
}
