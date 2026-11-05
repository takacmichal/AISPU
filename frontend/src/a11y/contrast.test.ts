import { describe, expect, it } from 'vitest'
import css from '../index.css?raw'

/**
 * WCAG 2.1 AA check of the colour tokens in `index.css` (:root): text pairs need 4.5 : 1, UI parts
 * (focus ring, input border) 3 : 1. Reads the real stylesheet, so a token change that breaks contrast fails here.
 */
type Rgb = [number, number, number]

const root = css.slice(css.indexOf(':root {'), css.indexOf('\n}', css.indexOf(':root {')))
const tokens = new Map<string, string>([...root.matchAll(/--([\w-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]))

/** Resolves a token to an opaque colour; translucent `rgb(0 0 0 / 8%)` is composited over `base`. */
function color(name: string, base: Rgb = [255, 255, 255]): Rgb {
  const value = tokens.get(name)
  if (!value) throw new Error(`Unknown token --${name}`)
  const hex = value.match(/^#([0-9a-f]{6})$/i)
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)) as Rgb
  const rgba = value.match(/^rgb\((\d+) (\d+) (\d+) \/ (\d+)%\)$/)
  if (rgba) {
    const alpha = Number(rgba[4]) / 100
    return [1, 2, 3].map((i, k) => Math.round(Number(rgba[i]) * alpha + base[k] * (1 - alpha))) as Rgb
  }
  const ref = value.match(/^var\(--([\w-]+)\)$/)
  if (ref) return color(ref[1], base)
  throw new Error(`Unsupported colour --${name}: ${value}`)
}

function luminance([r, g, b]: Rgb): number {
  const [lr, lg, lb] = [r, g, b].map((c) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * lr + 0.7152 * lg + 0.0722 * lb
}

function contrast(fg: Rgb, bg: Rgb): number {
  const [a, b] = [luminance(fg), luminance(bg)].sort((x, y) => y - x)
  return (a + 0.05) / (b + 0.05)
}

const ratio = (fg: string, bg: string) => contrast(color(fg, color(bg)), color(bg))

describe('colour contrast of the design tokens (WCAG AA)', () => {
  it.each([
    // Text on the surfaces
    ['color-text-body', 'color-bg'],
    ['color-text-muted', 'color-bg'],
    ['color-text-muted', 'color-surface'],
    ['color-text-muted', 'color-sand'],
    ['color-text-muted', 'color-info-bg'],
    ['color-primary', 'color-surface'],
    ['color-primary', 'color-bg'],
    // Buttons
    ['color-on-primary', 'color-primary'],
    ['color-on-primary', 'color-primary-hover'],
    ['color-text', 'color-secondary'],
    ['color-on-inverse', 'color-inverse'],
    ['color-on-inverse-muted', 'color-inverse'],
    // Badge, alert and step tones
    ['color-success', 'color-success-bg'],
    ['color-info', 'color-info-bg'],
    ['color-warning', 'color-warning-bg'],
    ['color-danger', 'color-danger-bg'],
    ['color-danger', 'color-danger-field-bg'],
    ['color-danger', 'color-surface'],
  ])('%s on %s has at least 4.5 : 1', (fg, bg) => {
    expect(ratio(fg, bg)).toBeGreaterThanOrEqual(4.5)
  })

  it('keeps the neutral badge readable (body text on the 5 % tint over white and cream)', () => {
    for (const base of ['color-surface', 'color-bg']) {
      expect(contrast(color('color-text-body'), color('surface-tint', color(base)))).toBeGreaterThanOrEqual(4.5)
    }
  })

  it.each([
    ['color-accent', 'color-surface'],
    ['color-input-border', 'color-surface'],
    ['color-input-border', 'color-bg'],
    ['color-danger-border', 'color-danger-field-bg'],
  ])('UI part %s on %s has at least 3 : 1', (fg, bg) => {
    expect(ratio(fg, bg)).toBeGreaterThanOrEqual(3)
  })
})
