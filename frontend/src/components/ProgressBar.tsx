import './ui.css'

interface ProgressBarProps {
  /** Accessible name, e.g. "Lehota distribučky". */
  label: string
  value: number
  max?: number
  /** Human readable value shown next to the bar and read by screen readers. */
  valueText?: string
  /** Hide the visible label (it still names the bar). */
  hideLabel?: boolean
}

/** Horizontal bar for deadlines, SLA and plan fulfilment. A value over max (e.g. overdue) is drawn full and dark. */
export function ProgressBar({ label, value, max = 100, valueText, hideLabel = true }: ProgressBarProps) {
  const over = value > max
  const width = max > 0 ? Math.min(100, Math.max(0, (value / max) * 100)) : 0
  return (
    <div className="progress-field">
      {(!hideLabel || valueText) && (
        <div className="progress-field__text">
          {!hideLabel && <span>{label}</span>}
          {valueText && <span className="progress-field__value">{valueText}</span>}
        </div>
      )}
      <div
        role="progressbar"
        aria-label={label}
        aria-valuemin={0}
        aria-valuemax={max}
        aria-valuenow={value}
        aria-valuetext={valueText}
        className={over ? 'progress progress--over' : 'progress'}
      >
        <span className="progress__fill" style={{ width: `${width}%` }} />
      </div>
    </div>
  )
}
