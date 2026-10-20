import type { ManagerSettings } from './types'

/**
 * Vue wraps pinia state in reactive proxies; structuredClone throws DataCloneError on
 * proxies nested inside the settings object (for example the persistentCheats array).
 * Settings are JSON-safe, so round-trip through JSON to get a detached plain copy.
 */
export const cloneSettings = (settings: ManagerSettings): ManagerSettings =>
  JSON.parse(JSON.stringify(settings)) as ManagerSettings
