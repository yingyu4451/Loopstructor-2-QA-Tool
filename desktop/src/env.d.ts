/// <reference types="vite/client" />

import type { DesktopApi } from './types'

declare global {
  /** Product version injected by Vite from desktop/package.json. */
  const __APP_VERSION__: string

  interface Window {
    loopstructorDesktop: DesktopApi
  }
}

export {}
