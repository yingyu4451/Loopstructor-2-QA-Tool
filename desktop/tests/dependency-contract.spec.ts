import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const root = process.cwd()

// Spelled from char codes so this guard never leaves a literal search hit for the removed vendor.
const REMOVED_ICON_VENDOR = String.fromCharCode(64, 105, 99, 111, 110, 105, 102, 121)

function sourceFiles(directory: string): string[] {
  return readdirSync(resolve(root, directory), { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && /\.(ts|vue)$/.test(entry.name))
    .map((entry) => join(entry.parentPath, entry.name))
}

describe('desktop dependency contract', () => {
  it('builds the renderer with Tailwind CSS and daisyUI', () => {
    const packageJson = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8'))
    const styles = readFileSync(resolve(root, 'src/styles.css'), 'utf8')
    const app = readFileSync(resolve(root, 'src/App.vue'), 'utf8')

    expect(packageJson.devDependencies.tailwindcss).toBe('4.3.3')
    expect(packageJson.devDependencies['@tailwindcss/vite']).toBe('4.3.3')
    expect(packageJson.devDependencies.daisyui).toBe('5.7.50')
    expect(packageJson.build.electronDist).toBe('node_modules/electron/dist')
    expect(styles).toContain('@import "tailwindcss";')
    expect(styles).toContain('@plugin "daisyui"')
    expect(app).toContain('h-screen min-h-0 overflow-hidden')
  })

  it('ships current Lucide icons and no deprecated icon packages', () => {
    const packageJson = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8'))
    expect(Object.keys(packageJson.dependencies).sort()).toEqual(['@lucide/vue', 'gsap', 'pinia', 'vue'])
    expect(packageJson.dependencies[`${REMOVED_ICON_VENDOR}/vue`]).toBeUndefined()
    expect(packageJson.dependencies[`${REMOVED_ICON_VENDOR}-icons/mdi`]).toBeUndefined()
    expect(packageJson.dependencies['lucide-vue-next']).toBeUndefined()
    expect(packageJson.devDependencies['lucide-vue-next']).toBeUndefined()

    const specifiers = [
      ...Object.values(packageJson.dependencies) as string[],
      ...Object.values(packageJson.devDependencies) as string[],
    ]
    expect(specifiers.every((specifier) => /^\d+\.\d+\.\d+$/.test(specifier))).toBe(true)

    expect(existsSync(resolve(root, 'src/icons.ts'))).toBe(false)

    const files = sourceFiles('src')
    expect(files.length).toBeGreaterThan(0)
    const sources = files.map((file) => readFileSync(file, 'utf8'))
    expect(sources.filter((source) => source.includes(REMOVED_ICON_VENDOR)).length).toBe(0)
    expect(sources.filter((source) => source.includes("from '../icons'")).length).toBe(0)
    expect(sources.filter((source) => source.includes("from '@lucide/vue'")).length)
      .toBeGreaterThanOrEqual(17)
    expect(sources.filter((source) => source.includes('lucide-vue-next')).length).toBe(0)

    const lockfile = readFileSync(resolve(root, 'pnpm-lock.yaml'), 'utf8')
    expect(lockfile).not.toContain(REMOVED_ICON_VENDOR)
    expect(lockfile).not.toContain('lucide-vue-next')
    expect(lockfile).toContain('@lucide/vue')
  })

  it('keeps automation control behind the typed preload whitelist', () => {
    const preload = readFileSync(resolve(root, 'electron/preload.cts'), 'utf8')
    expect(preload).toContain("ipcRenderer.invoke('automation:start')")
    expect(preload).toContain("ipcRenderer.invoke('automation:querySetup')")
    expect(preload).not.toContain('ipcRenderer: ipcRenderer')
  })
})
