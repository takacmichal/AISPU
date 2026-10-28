import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => cleanup())

// Lazy route chunks are slow to transform on a cold CI runner.
configure({ asyncUtilTimeout: 5000 })
