import { useId, useState } from 'react'
import { SegmentedControl } from '../../components/SegmentedControl'
import { formatNumber } from '../../lib/format'
import { DEFAULT_REGION, KWP_RANGE, REGIONS, estimateYield, type Orientation } from './estimate'
import { LANDING_TEXTS } from './texts'

const T = LANDING_TEXTS.calculator

/** "Koľko vyrobí vaša strecha?" – power, orientation and region give a rough yearly yield and saving. */
export function YieldCalculator({ ctaHref }: { ctaHref: string }) {
  const id = useId()
  const [kwp, setKwp] = useState<number>(KWP_RANGE.initial)
  const [orientation, setOrientation] = useState<Orientation>('south')
  const [region, setRegion] = useState(DEFAULT_REGION)
  const { yearlyKwh, savingEur } = estimateYield({ kwp, orientation, region })
  const kwpText = `${formatNumber(kwp, 1)} kWp`
  // Round to tens: the estimate is not more precise than that.
  const yearly = formatNumber(Math.round(yearlyKwh / 10) * 10, 0)
  const saving = formatNumber(Math.round(savingEur / 10) * 10, 0)

  return (
    <section aria-labelledby={`${id}-title`} className="landing-wrap landing-section">
      <div className="calc">
        <div className="calc__inputs">
          <div>
            <h2 id={`${id}-title`} className="landing-h2 landing-h2--sm">
              {T.title}
            </h2>
            <p className="calc__lead">{T.lead}</p>
          </div>
          <div className="calc__field">
            <label htmlFor={`${id}-kwp`} className="calc__label">
              <span>{T.power}</span>
              <strong>{kwpText}</strong>
            </label>
            <input
              id={`${id}-kwp`}
              type="range"
              min={KWP_RANGE.min}
              max={KWP_RANGE.max}
              step={KWP_RANGE.step}
              value={kwp}
              aria-valuetext={kwpText}
              onChange={(event) => setKwp(Number(event.target.value))}
            />
          </div>
          <div className="calc__field">
            {/* Visible caption only; the radio group carries the same name for screen readers. */}
            <span className="calc__label" aria-hidden="true">
              {T.orientation}
            </span>
            <SegmentedControl
              label={T.orientation}
              value={orientation}
              onChange={setOrientation}
              options={[
                { value: 'south', label: T.south },
                { value: 'east-west', label: T.eastWest },
              ]}
            />
          </div>
          <div className="calc__field">
            <label htmlFor={`${id}-region`} className="calc__label">
              {T.region}
            </label>
            <select id={`${id}-region`} value={region} onChange={(event) => setRegion(event.target.value)}>
              {REGIONS.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </select>
          </div>
        </div>
        <div className="calc__result">
          <div aria-live="polite" aria-atomic="true" className="calc__numbers">
            <p className="calc__result-label">{T.yearly}</p>
            <p className="calc__yearly">
              {yearly}
              <span> kWh</span>
            </p>
            <p className="calc__saving">
              <span>{T.saving}</span>
              <strong>
                ≈ {saving} {T.perYear}
              </strong>
            </p>
          </div>
          <p className="calc__disclaimer">{T.disclaimer}</p>
          <a href={ctaHref} className="btn btn--primary btn--lg">
            {T.cta}
          </a>
        </div>
      </div>
    </section>
  )
}
