import type { ReactNode } from 'react'
import { MESSAGES } from '../lib/messages'
import './ui.css'

export type StepStatus = 'done' | 'current' | 'upcoming' | 'blocked'

export interface Step {
  label: string
  status: StepStatus
  /** Shown after the status text, e.g. a date. */
  meta?: ReactNode
  /** Extra line below, e.g. why the step is blocked. */
  note?: ReactNode
}

interface StepperProps {
  /** Accessible name of the list. */
  label: string
  steps: readonly Step[]
  /** Vertical list for narrow places (cards); horizontal steppers switch to vertical on mobile anyway. */
  orientation?: 'horizontal' | 'vertical'
}

const MARKER: Record<StepStatus, (index: number) => string> = {
  done: () => '✓',
  blocked: () => '!',
  current: (i) => String(i + 1),
  upcoming: (i) => String(i + 1),
}

/** Process steps (offer, installation, connection request). The current or blocked step has aria-current="step". */
export function Stepper({ label, steps, orientation = 'horizontal' }: StepperProps) {
  return (
    <ol className={orientation === 'vertical' ? 'stepper stepper--vertical' : 'stepper'} aria-label={label}>
      {steps.map((step, index) => (
        <li
          key={`${index}-${step.label}`}
          className={`stepper__step stepper__step--${step.status}`}
          aria-current={step.status === 'current' || step.status === 'blocked' ? 'step' : undefined}
        >
          <span className="stepper__marker" aria-hidden="true">
            {MARKER[step.status](index)}
          </span>
          <span className="stepper__label">{step.label}</span>
          <span className="stepper__meta">
            {MESSAGES.stepStatus[step.status]}
            {step.meta && <> · {step.meta}</>}
          </span>
          {step.note && <span className="stepper__meta">{step.note}</span>}
        </li>
      ))}
    </ol>
  )
}
