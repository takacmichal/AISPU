import { useRef, type KeyboardEvent } from 'react'
import './ui.css'

export interface SegmentOption<V extends string> {
  value: V
  label: string
  count?: number
}

interface SegmentedControlProps<V extends string> {
  /** Accessible name of the group. */
  label: string
  options: readonly SegmentOption<V>[]
  value: V
  onChange: (value: V) => void
  size?: 'md' | 'sm'
}

/**
 * Pill switch for views and filters (Kanban/Zoznam, Deň/Týždeň/Mesiac). Behaves like a radio group:
 * one tab stop, arrow keys move and select, Home/End jump to the ends.
 */
export function SegmentedControl<V extends string>({ label, options, value, onChange, size = 'md' }: SegmentedControlProps<V>) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const selected = Math.max(0, options.findIndex((o) => o.value === value))

  function select(index: number) {
    const next = (index + options.length) % options.length
    onChange(options[next].value)
    refs.current[next]?.focus()
  }

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const moves: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowDown: index + 1,
      ArrowLeft: index - 1,
      ArrowUp: index - 1,
      Home: 0,
      End: options.length - 1,
    }
    if (!(event.key in moves)) return
    event.preventDefault()
    select(moves[event.key])
  }

  return (
    <div role="radiogroup" aria-label={label} className={`segmented segmented--${size}`}>
      {options.map((option, index) => {
        const checked = index === selected
        return (
          <button
            key={option.value}
            ref={(el) => {
              refs.current[index] = el
            }}
            type="button"
            role="radio"
            aria-checked={checked}
            tabIndex={checked ? 0 : -1}
            className="segmented__option"
            onClick={() => onChange(option.value)}
            onKeyDown={(event) => onKeyDown(event, index)}
          >
            {option.label}
            {option.count !== undefined && <span className="segmented__count">{option.count}</span>}
          </button>
        )
      })}
    </div>
  )
}
