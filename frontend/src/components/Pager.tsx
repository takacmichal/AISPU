import { MESSAGES } from '../lib/messages'
import './ui.css'
import { pageItems } from './uiHelpers'

interface PagerProps {
  /** 1-based. */
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

/** Pagination from the wireframe (K-01): "Zobrazené 1–10 z 248" and numbered pages. */
export function Pager({ page, pageSize, totalCount, onPageChange }: PagerProps) {
  const T = MESSAGES.pager
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(totalCount, page * pageSize)
  return (
    <nav aria-label={T.label} className="pager">
      <span className="pager__summary">{totalCount === 0 ? T.empty : T.summary(from, to, totalCount)}</span>
      <div className="pager__pages">
        <button type="button" className="pager__btn" aria-label={T.previous} disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          ‹
        </button>
        {pageItems(page, pageCount).map((item, i) =>
          item === 'gap' ? (
            <span key={`gap-${i}`} className="pager__gap" aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={item}
              type="button"
              className="pager__btn"
              aria-label={T.page(item)}
              aria-current={item === page ? 'page' : undefined}
              onClick={() => item !== page && onPageChange(item)}
            >
              {item}
            </button>
          ),
        )}
        <button type="button" className="pager__btn" aria-label={T.next} disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>
          ›
        </button>
      </div>
    </nav>
  )
}
