import { useEffect } from 'react'
import { useBlocker } from 'react-router-dom'

/**
 * Warns before leaving a page with unsaved changes: browser unload/refresh and in-app navigation.
 * Needs a data router (createBrowserRouter). `shouldBlock` is evaluated at navigation time.
 */
export function useUnsavedChanges(shouldBlock: () => boolean, message = 'Máte neuložené zmeny. Naozaj chcete odísť?') {
  const blocker = useBlocker(shouldBlock)

  useEffect(() => {
    if (blocker.state !== 'blocked') return
    if (window.confirm(message)) blocker.proceed()
    else blocker.reset()
  }, [blocker, message])

  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!shouldBlock()) return
      event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [shouldBlock])
}
