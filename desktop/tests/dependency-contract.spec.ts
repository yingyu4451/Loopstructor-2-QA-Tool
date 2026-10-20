import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const root = process.cwd()

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

  it('ships offline Iconify MDI icons and no Lucide packages', () => {
    const packageJson = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8'))
    expect(Object.keys(packageJson.dependencies).sort())
      .toEqual(['@iconify-icons/mdi', '@iconify/vue', 'gsap', 'pinia', 'vue'])
    expect(packageJson.dependencies['@iconify/vue']).toBe('5.0.4')
    expect(packageJson.dependencies['@iconify-icons/mdi']).toBe('2.0.0')
    expect(packageJson.dependencies['@lucide/vue']).toBeUndefined()
    expect(packageJson.devDependencies['@lucide/vue']).toBeUndefined()
    expect(packageJson.dependencies['lucide-vue-next']).toBeUndefined()
    expect(packageJson.devDependencies['lucide-vue-next']).toBeUndefined()

    const specifiers = [
      ...Object.values(packageJson.dependencies) as string[],
      ...Object.values(packageJson.devDependencies) as string[],
    ]
    expect(specifiers.every((specifier) => /^\d+\.\d+\.\d+$/.test(specifier))).toBe(true)

    const facadePath = resolve(root, 'src/icons.ts')
    expect(existsSync(facadePath)).toBe(true)
    const facade = readFileSync(facadePath, 'utf8')
    expect(facade).toContain("from '@iconify/vue'")
    expect(facade).toContain("from '@iconify-icons/mdi/")

    const files = sourceFiles('src')
    expect(files.length).toBeGreaterThan(0)
    const sources = files.map((file) => readFileSync(file, 'utf8'))
    expect(sources.filter((source) => source.includes("from '../icons'")).length)
      .toBeGreaterThanOrEqual(17)
    expect(sources.filter((source) => source.includes('@lucide/vue')).length).toBe(0)
    expect(sources.filter((source) => source.includes('lucide-vue-next')).length).toBe(0)

    const lockfile = readFileSync(resolve(root, 'pnpm-lock.yaml'), 'utf8')
    expect(lockfile).toContain('@iconify/vue')
    expect(lockfile).toContain('@iconify-icons/mdi')
    expect(lockfile).not.toContain('@lucide/vue')
    expect(lockfile).not.toContain('lucide-vue-next')
  })

  it('keeps packaged updates incremental', () => {
    const packageJson = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8'))
    const build = packageJson.build

    expect(build.disableAsarIntegrity).toBe(true)
    expect(build.files).toContain('!node_modules/**/*')
    expect(build.asarUnpack).toEqual([
      'dist/assets/*.png',
      'dist/assets/*.jpg',
      'dist/assets/*.ttf',
    ])

    const minor = packageJson.version.split('.').slice(0, 2).join('.')
    expect(build.extraMetadata.shortVersion).toBe(`${minor}.0`)
    expect(build.extraMetadata.shortVersionWindows).toBe(`${minor}.0.0`)
  })

  it('keeps automation control behind the typed preload whitelist', () => {
    const preload = readFileSync(resolve(root, 'electron/preload.cts'), 'utf8')
    expect(preload).toContain("ipcRenderer.invoke('automation:start')")
    expect(preload).toContain("ipcRenderer.invoke('automation:querySetup')")
    expect(preload).not.toContain('ipcRenderer: ipcRenderer')
  })
})
