import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ProductionAreaChart } from '../../components/charts/ProductionAreaChart'
import { BrandGlyph, MenuIcon } from '../../layout/NavIcon'
import { InquiryForm } from './InquiryForm'
import type { SendInquiry } from './inquiry'
import { illustrativeDay } from './sampleData'
import { LANDING_TEXTS as T } from './texts'
import { YieldCalculator } from './YieldCalculator'
import './landing.css'

const HERO_DAY = illustrativeDay(3.8)
const DIP_DAY = illustrativeDay(6.2, true)

const SERVICE_ICONS: Record<string, ReactNode> = {
  design: (
    <>
      <path d="M3 22l4-14h12l4 14z" />
      <path d="M5 15h16M11 8l-1.5 14M15 8l1.5 14" />
      <path d="M16 4l2 2 4-4" />
    </>
  ),
  install: <path d="M15.5 4.5a5 5 0 0 0-6.4 6.4L3 17v4h4l6.1-6.1a5 5 0 0 0 6.4-6.4l-3 3-3.5-.5-.5-3.5z" />,
  service: (
    <>
      <path d="M3 20h20" />
      <path d="M3 16l5-5 4 3 6-7 5 4" />
    </>
  ),
}

function CheckIcon() {
  return (
    <svg width="20" height="20" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 10.5l4 4 8-9" />
    </svg>
  )
}

function Brand() {
  return (
    <>
      <BrandGlyph />
      <span className="landing-brand__name">SolarOps</span>
    </>
  )
}

function SiteHeader() {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(false)
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  const close = () => setOpen(false)

  return (
    <header className="landing-header">
      <div className="landing-header__bar landing-wrap">
        <a href="#obsah" className="landing-brand" aria-label={T.homeLabel}>
          <Brand />
        </a>
        <nav aria-label={T.nav.label} className="landing-header__links">
          {T.nav.links.map((link) => (
            <a key={link.href} href={link.href}>
              {link.label}
            </a>
          ))}
        </nav>
        <div className="landing-header__actions">
          <Link to="/login" className="btn landing-header__cta">
            {T.signIn}
          </Link>
          <a href="#kontakt" className="btn btn--primary landing-header__cta">
            {T.inquiryCta}
          </a>
          <button
            type="button"
            className="icon-btn landing-header__burger"
            aria-expanded={open}
            aria-controls="landing-mobile-nav"
            aria-label={open ? T.nav.close : T.nav.open}
            onClick={() => setOpen((value) => !value)}
          >
            <MenuIcon />
          </button>
        </div>
      </div>
      {open && (
        <nav id="landing-mobile-nav" aria-label={T.nav.mobileLabel} className="landing-header__mobile landing-wrap">
          {T.nav.links.map((link) => (
            <a key={link.href} href={link.href} onClick={close}>
              {link.label}
            </a>
          ))}
          <div className="landing-header__mobile-actions">
            <Link to="/login" className="btn btn--lg">
              {T.signIn}
            </Link>
            <a href="#kontakt" className="btn btn--primary btn--lg" onClick={close}>
              {T.inquiryCta}
            </a>
          </div>
        </nav>
      )}
    </header>
  )
}

function Hero() {
  const H = T.hero
  return (
    <section className="landing-wrap hero" aria-labelledby="hero-h">
      <p className="landing-eyebrow">
        <span className="landing-dot" aria-hidden="true" />
        {H.eyebrow}
      </p>
      <h1 id="hero-h" className="hero__title">
        {H.titleStart}{' '}
        <span className="hero__chip hero__chip--panel" aria-hidden="true">
          <svg width="70%" height="70%" viewBox="0 0 120 60" fill="none" strokeLinejoin="round">
            <path d="M14 50L40 14h66L82 50z" fill="#0b2a44" stroke="#0b2a44" strokeWidth="2" />
            <path d="M30 27.5h68M22 39h68M57 14L45 50M82 14L67 50" stroke="#0096f7" strokeWidth="2" />
            <path d="M10 54h80" stroke="#375033" strokeWidth="3" strokeLinecap="round" />
          </svg>
        </span>{' '}
        {H.titleMiddle}{' '}
        <span className="hero__chip hero__chip--sun" aria-hidden="true">
          <svg width="56%" height="56%" viewBox="0 0 40 40" fill="none" stroke="#fff" strokeWidth="3" strokeLinecap="round">
            <circle cx="20" cy="20" r="7" />
            <path d="M20 3v5M20 32v5M3 20h5M32 20h5M8 8l3.5 3.5M28.5 28.5L32 32M8 32l3.5-3.5M28.5 11.5L32 8" />
          </svg>
        </span>
      </h1>
      <div className="hero__intro">
        <p className="hero__lead">{H.lead}</p>
        <div className="hero__actions">
          <a href="#kontakt" className="btn btn--primary btn--lg">
            {H.primary}
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
              <path d="M3 8h10M9 4l4 4-4 4" />
            </svg>
          </a>
          <Link to="/login" className="btn btn--lg">
            {H.secondary}
          </Link>
        </div>
      </div>

      <figure className="hero__preview">
        <figcaption className="sr-only">{H.previewLabel}</figcaption>
        <div className="hero__screen">
          <div className="hero__side" aria-hidden="true">
            <span className="hero__side-brand">SolarOps</span>
            {H.previewNav.map((item, index) => (
              <span key={item} className={index === 0 ? 'hero__side-item hero__side-item--active' : 'hero__side-item'}>
                {item}
              </span>
            ))}
          </div>
          <div className="hero__panel">
            <div className="hero__panel-head">
              <div>
                <p className="landing-muted">{H.plantMeta}</p>
                <p className="hero__panel-title">{H.plantTitle}</p>
              </div>
              <span className="badge badge--success">{H.plantStatus}</span>
            </div>
            <ul className="hero__kpis">
              {H.kpis.map((kpi) => (
                <li key={kpi.label} className="hero__kpi">
                  <span className="hero__kpi-label">{kpi.label}</span>
                  <span className="hero__kpi-value">
                    {kpi.value} <span>{kpi.unit}</span>
                  </span>
                </li>
              ))}
            </ul>
            <ProductionAreaChart title={H.chartTitle} summary={H.chartSummary} unit="kW" points={HERO_DAY} xLabelCount={5} />
          </div>
        </div>
      </figure>
    </section>
  )
}

function Numbers() {
  return (
    <section className="landing-wrap landing-section" aria-labelledby="cisla-h">
      <h2 id="cisla-h" className="sr-only">
        {T.numbers.title}
      </h2>
      <ul className="numbers">
        {T.numbers.items.map((item) => (
          <li key={item.label}>
            <span className="numbers__value">{item.value}</span>
            <span className="numbers__label">{item.label}</span>
          </li>
        ))}
      </ul>
      <p className="landing-note">{T.numbers.note}</p>
    </section>
  )
}

function Services() {
  const S = T.services
  return (
    <section id="sluzby" className="landing-wrap landing-section" aria-labelledby="sluzby-h">
      <div className="landing-section__head">
        <h2 id="sluzby-h" className="landing-h2">
          {S.title}
        </h2>
        <p className="landing-lead">{S.lead}</p>
      </div>
      <div className="services">
        {S.items.map((item) => (
          <article key={item.title} className="service">
            <span className="service__icon" aria-hidden="true">
              <svg width="26" height="26" viewBox="0 0 26 26" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                {SERVICE_ICONS[item.icon]}
              </svg>
            </span>
            <h3 className="landing-h3">{item.title}</h3>
            <ul className="service__points">
              {item.points.map((point) => (
                <li key={point}>{point}</li>
              ))}
            </ul>
          </article>
        ))}
      </div>
    </section>
  )
}

function Steps() {
  const S = T.steps
  return (
    <section id="ako" className="landing-wrap landing-section" aria-labelledby="ako-h">
      <h2 id="ako-h" className="landing-h2">
        {S.title}
      </h2>
      <p className="landing-lead landing-lead--below">{S.lead}</p>
      <ol className="steps" aria-labelledby="ako-h">
        {S.items.map((step, index) => (
          <li key={step.title} className="step">
            <div className="step__marker">
              <span className={index === S.items.length - 1 ? 'step__number step__number--last' : 'step__number'}>
                {String(index + 1).padStart(2, '0')}
              </span>
              {index < S.items.length - 1 && <span className="step__line" aria-hidden="true" />}
            </div>
            <h3 className="step__title">{step.title}</h3>
            <p className="step__text">{step.text}</p>
            <span className="step__who">{step.who}</span>
          </li>
        ))}
      </ol>
    </section>
  )
}

function MonitoringBand() {
  const M = T.monitoring
  return (
    <section id="monitoring" className="band" aria-labelledby="mon-h">
      <div className="landing-wrap band__inner">
        <p className="landing-eyebrow band__eyebrow">
          <span className="landing-dot band__dot" aria-hidden="true" />
          {M.eyebrow}
        </p>
        <div className="landing-section__head">
          <h2 id="mon-h" className="landing-h2 band__title">
            {M.title}
          </h2>
          <p className="band__lead">{M.lead}</p>
        </div>
        <div className="band__grid">
          <div className="band__chart">
            <p className="band__chart-title">{M.chartTitle}</p>
            <ProductionAreaChart title={M.chartTitle} summary={M.chartSummary} unit="kW" variant="dark" points={DIP_DAY} minUnderPoints={4} xLabelCount={5} />
          </div>
          <div className="band__ticket">
            <p className="band__ticket-intro">{M.ticketIntro}</p>
            <article className="ticket-card">
              <div className="ticket-card__head">
                <span className="landing-muted">{M.ticketMeta}</span>
                <span className="badge badge--error">{M.ticketStatus}</span>
              </div>
              <h3 className="landing-h3">{M.ticketTitle}</h3>
              <p className="ticket-card__text">{M.ticketText}</p>
              <div className="ticket-card__tags">
                <span className="badge badge--info">{M.ticketSource}</span>
                <span className="badge badge--attention">{M.ticketPriority}</span>
              </div>
              <p className="ticket-card__next">
                <span>{M.ticketNextLabel}</span>
                <strong>{M.ticketNext}</strong>
              </p>
            </article>
          </div>
        </div>
        <p className="landing-note band__note">{M.note}</p>
      </div>
    </section>
  )
}

function Portal() {
  const P = T.portal
  return (
    <section className="landing-wrap landing-section" aria-labelledby="portal-h">
      <div className="portal">
        <div className="portal__copy">
          <h2 id="portal-h" className="landing-h2">
            {P.title}
          </h2>
          <p className="landing-lead portal__lead">{P.lead}</p>
          <ul className="portal__points">
            {P.points.map((point) => (
              <li key={point}>
                <CheckIcon />
                {point}
              </li>
            ))}
          </ul>
        </div>
        <figure className="portal__preview">
          <figcaption className="sr-only">{P.previewLabel}</figcaption>
          <div className="portal__card">
            <div className="portal__card-head">
              <div>
                <p className="landing-muted">{P.meta}</p>
                <p className="hero__panel-title">{P.plant}</p>
                <p className="landing-muted">{P.eic}</p>
              </div>
              {/* Part of the picture, not a working button. */}
              <span className="btn btn--primary btn--sm portal__fake-btn" aria-hidden="true">
                {P.report}
              </span>
            </div>
            <div>
              <p className="portal__progress-head">
                <span>{P.progressLabel}</span>
                <span className="landing-muted">{P.progressValue}</span>
              </p>
              <div className="portal__segments" aria-hidden="true">
                {Array.from({ length: 7 }, (_, i) => (
                  <span key={i} className={i < 5 ? 'is-done' : i === 5 ? 'is-current' : undefined} />
                ))}
              </div>
              <p className="portal__progress-step">
                {P.progressStep} · <span className="portal__progress-note">{P.progressNote}</span>
              </p>
            </div>
            <div className="portal__tiles">
              {P.tiles.map((tile) => (
                <div key={tile.label} className="portal__tile">
                  <span className="landing-muted">{tile.label}</span>
                  <span>{tile.value}</span>
                </div>
              ))}
            </div>
          </div>
        </figure>
      </div>
    </section>
  )
}

function Faq() {
  return (
    <section className="landing-wrap landing-section faq" aria-labelledby="faq-h">
      <h2 id="faq-h" className="landing-h2">
        {T.faq.title}
      </h2>
      <div className="faq__list">
        {T.faq.items.map((item) => (
          <details key={item.q} className="faq__item">
            <summary>
              {item.q}
              <svg className="faq__plus" width="22" height="22" viewBox="0 0 22 22" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
                <path d="M11 4v14M4 11h14" />
              </svg>
            </summary>
            <p>{item.a}</p>
          </details>
        ))}
      </div>
    </section>
  )
}

function Contact({ sendInquiry }: { sendInquiry?: SendInquiry }) {
  const C = T.contact
  return (
    <section id="kontakt" className="landing-wrap landing-section landing-section--last" aria-labelledby="kontakt-h">
      <div className="contact">
        <div className="contact__copy">
          <h2 id="kontakt-h" className="landing-h2">
            {C.title}
          </h2>
          {sendInquiry ? (
            <>
              <p className="landing-lead portal__lead">{C.lead}</p>
              <p className="contact__account">
                {C.accountQuestion} <Link to="/register">{C.createAccount}</Link>
              </p>
            </>
          ) : (
            <p className="landing-lead portal__lead">{C.leadUnavailable}</p>
          )}
        </div>
        <div className="contact__form">
          {sendInquiry ? (
            <InquiryForm onSubmit={sendInquiry} />
          ) : (
            <div className="inquiry-sent">
              <h3 className="inquiry-sent__title">{C.unavailableTitle}</h3>
              <p className="inquiry-sent__text">{C.unavailableText}</p>
              <div className="btn-row">
                <Link to="/register" className="btn btn--primary btn--lg">
                  {C.createAccount}
                </Link>
                <Link to="/login" className="btn btn--lg">
                  {T.signIn}
                </Link>
              </div>
            </div>
          )}
        </div>
      </div>
    </section>
  )
}

function SiteFooter() {
  const F = T.footer
  return (
    <footer className="landing-footer">
      <div className="landing-wrap landing-footer__inner">
        <div className="landing-footer__cols">
          <div className="landing-footer__brand">
            <span className="landing-brand">
              <Brand />
            </span>
            <p>{F.tagline}</p>
          </div>
          <nav aria-label={F.pageLinks} className="landing-footer__col">
            <span className="landing-footer__heading">{F.pageLinks}</span>
            {T.nav.links.map((link) => (
              <a key={link.href} href={link.href}>
                {link.label}
              </a>
            ))}
          </nav>
          <nav aria-label={F.accountLinksLabel} className="landing-footer__col">
            <span className="landing-footer__heading">{F.account}</span>
            <Link to="/login">{F.login}</Link>
            <Link to="/register">{F.register}</Link>
          </nav>
        </div>
        <div className="landing-footer__bottom">
          <span>{F.copyright}</span>
          <span>{F.school}</span>
        </div>
      </div>
    </footer>
  )
}

interface LandingPageProps {
  /** Sends the inquiry form; without it the contact section leads to registration (no anonymous endpoint yet). */
  sendInquiry?: SendInquiry
}

/** Public landing page on "/" for visitors (Main.dc.html). Loaded lazily, so the app bundle stays small. */
export default function LandingPage({ sendInquiry }: LandingPageProps) {
  return (
    <div className="landing">
      <a href="#obsah" className="skip-link">
        {T.skip}
      </a>
      <SiteHeader />
      <main id="obsah" tabIndex={-1}>
        <Hero />
        <Numbers />
        <Services />
        <Steps />
        <MonitoringBand />
        <Portal />
        <YieldCalculator ctaHref="#kontakt" />
        <Faq />
        <Contact sendInquiry={sendInquiry} />
      </main>
      <SiteFooter />
    </div>
  )
}
