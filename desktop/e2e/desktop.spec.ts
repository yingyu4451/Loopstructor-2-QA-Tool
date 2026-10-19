import { _electron as electron, expect, test } from '@playwright/test'
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'

const repositoryRoot = resolve(process.cwd(), '..')
const releaseHostPath = resolve(repositoryRoot, 'src/Loopstructor.AutoPlayer.Host/bin/Release/net8.0-windows/Loopstructor.AutoPlayer.Host.exe')
const hostPath = existsSync(releaseHostPath)
  ? releaseHostPath
  : resolve(repositoryRoot, 'src/Loopstructor.AutoPlayer.Host/bin/Debug/net8.0-windows/Loopstructor.AutoPlayer.Host.exe')
const desktopPackage = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf8')) as { version: string }
const screenshotRoot = resolve(repositoryRoot, `artifacts/ui/v${desktopPackage.version}-electron`)

test('unified desktop is sandboxed and responsive across every route', async () => {
  const dataRoot = mkdtempSync(resolve(tmpdir(), 'loopstructor-electron-e2e-'))
  mkdirSync(screenshotRoot, { recursive: true })
  const app = await electron.launch({
    args: ['.'],
    cwd: process.cwd(),
    env: {
      ...process.env,
      LOCALAPPDATA: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_DESKTOP_USER_DATA_ROOT: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_HOST_PATH: hostPath,
      LOOPSTRUCTOR_AUTOPLAYER_HOST_DATA_ROOT: dataRoot,
    },
  })

  try {
    const page = await app.firstWindow()
    const rendererErrors: string[] = []
    page.on('pageerror', (error) => rendererErrors.push(error.message))
    await expect(page.getByText('Loopstructor 2 QA Tool', { exact: true })).toBeVisible()
    expect(await page.evaluate(() => typeof (globalThis as { require?: unknown }).require)).toBe('undefined')
    const directSnapshot = await page.evaluate(async () => {
      try {
        return { value: await window.loopstructorDesktop.getSnapshot(), error: '' }
      } catch (error) {
        return { value: null, error: error instanceof Error ? error.message : String(error) }
      }
    })
    expect(directSnapshot.error, rendererErrors.join('\n')).toBe('')
    expect(directSnapshot.value?.protocolVersion).toBe(1)
    await expect(page.locator('.titlebar-status')).not.toContainText('正在启动 Host', { timeout: 15_000 })
    const brandLogo = page.locator('.brand-logo')
    await expect.poll(() => brandLogo.evaluate((element: HTMLImageElement) =>
      element.complete && element.naturalWidth === 256 && element.naturalHeight === 256)).toBe(true)
    await expect(brandLogo).toHaveCSS('object-fit', 'contain')
    await expect(brandLogo).toHaveCSS('border-radius', '0px')
    await expect(brandLogo).toHaveCSS('border-top-width', '0px')

    const routes = ['游戏与插件', '存档', '自动游玩', '战车', '道具', '遗物', '战斗', '对象属性', '生成', '日志与状态', '界面与更新']
    await page.getByRole('button', { name: '战车', exact: true }).click()
    await page.waitForTimeout(1_100)
    await expect(page.locator('.nav-item.active')).toHaveAttribute('aria-label', '战车')
    await expect(page.getByText('开启作弊', { exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: '自动游玩', exact: true }).click()
    await expect(page.getByRole('status').getByText('自动游玩尚未完成', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '开始', exact: true })).toBeVisible()
    await page.getByRole('button', { name: '存档', exact: true }).click()
    await expect(page.getByText('存档保险库', { exact: true })).toBeVisible()
    await expect(page.getByText('自动备份存档', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '打开目录', exact: true })).toBeVisible()
    await page.getByRole('button', { name: '界面与更新', exact: true }).click()
    await page.getByRole('button', { name: '应用界面设置', exact: true }).click()
    await expect(page.locator('.app-shell')).toHaveAttribute('data-skin', 'skyspine')
    await page.getByRole('radio', { name: /自定义/ }).check()
    await page.getByRole('slider').fill('125')
    await page.getByRole('button', { name: '应用界面设置', exact: true }).click()
    await page.waitForTimeout(1_200)
    await expect(page.locator('.nav-item.active')).toHaveAttribute('aria-label', '界面与更新')
    expect(await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.webContents.getZoomFactor())).toBeCloseTo(1.25, 2)
    await page.getByRole('radio', { name: /跟随系统 DPI/ }).check()
    await page.getByRole('button', { name: '应用界面设置', exact: true }).click()
    await expect(page.locator('.nav-item.active')).toHaveAttribute('aria-label', '界面与更新')
    expect(await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.webContents.getZoomFactor())).toBeCloseTo(1, 2)
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await page.waitForTimeout(3_500)
    const views = [
      { width: 980, height: 680, zoom: 1 },
      { width: 1280, height: 860, zoom: 1 },
      { width: 980, height: 680, zoom: 1.25 },
    ]
    for (const view of views) {
      await app.evaluate(({ BrowserWindow }, nextView) => {
        const browserWindow = BrowserWindow.getAllWindows()[0]
        browserWindow?.setSize(nextView.width, nextView.height)
        browserWindow?.webContents.setZoomFactor(nextView.zoom)
      }, view)
      if (view.width === 980) {
        const navLabels = page.locator('.nav-label')
        await expect(navLabels).toHaveCount(routes.length)
        for (let index = 0; index < routes.length; index += 1) {
          await expect(navLabels.nth(index)).toBeVisible()
        }
      }
      await expect(page.locator('.workbench-caption')).toHaveCount(0)
      for (const route of routes) {
        await page.getByRole('button', { name: route, exact: true }).click()
        await expect(page.locator('.nav-item.active')).toHaveAttribute('aria-label', route)
        await page.waitForTimeout(280)
        if (await page.locator('.page-host > *').count() === 0) {
          throw new Error(`${route} 页面没有渲染。${rendererErrors.join('\n')}`)
        }
        await expect(page.locator('.page-host > *')).toBeVisible()
        await expect(page.locator('.cheat-control-bar')).toHaveCount(0)
        await expect(page.getByText('作弊运行控制', { exact: true })).toHaveCount(0)
        const geometry = await page.evaluate(() => {
          const pageHost = document.querySelector<HTMLElement>('.page-host')!
          const hostBounds = pageHost.getBoundingClientRect()
          const clippedSections = Array.from(pageHost.querySelectorAll<HTMLElement>('.mechanical-section'))
            .map((section, index) => {
              const bounds = section.getBoundingClientRect()
              return { index, left: bounds.left, right: bounds.right }
            })
            .filter(bounds => bounds.left < hostBounds.left - 1 || bounds.right > hostBounds.right + 1)
          const clippedSectionContents = Array.from(pageHost.querySelectorAll<HTMLElement>('.mechanical-section'))
            .map((section, index) => ({
              index,
              clientHeight: section.clientHeight,
              scrollHeight: section.scrollHeight,
            }))
            .filter(section => section.scrollHeight > section.clientHeight + 1)
          return {
            clientWidth: pageHost.clientWidth,
            scrollWidth: pageHost.scrollWidth,
            visualViewportWidth: window.visualViewport?.width ?? window.innerWidth,
            documentClientWidth: document.documentElement.clientWidth,
            documentScrollWidth: document.documentElement.scrollWidth,
            hostLeft: hostBounds.left,
            hostRight: hostBounds.right,
            clippedSections,
            clippedSectionContents,
          }
        })
        expect(
          geometry.documentScrollWidth,
          `${route} 在 ${view.width}×${view.height} / ${view.zoom * 100}% 下使应用壳超出 viewport`,
        ).toBeLessThanOrEqual(geometry.documentClientWidth + 1)
        expect(geometry.hostLeft).toBeGreaterThanOrEqual(-1)
        expect(
          geometry.hostRight,
          `${route} 在 ${view.width}×${view.height} / ${view.zoom * 100}% 下使页面内容区越出 viewport`,
        ).toBeLessThanOrEqual(geometry.visualViewportWidth + 1)
        expect(
          geometry.scrollWidth,
          `${route} 在 ${view.width}×${view.height} / ${view.zoom * 100}% 下发生横向溢出`,
        ).toBeLessThanOrEqual(geometry.clientWidth + 1)
        expect(
          geometry.clippedSections,
          `${route} 在 ${view.width}×${view.height} / ${view.zoom * 100}% 下存在被裁切的机械边框`,
        ).toEqual([])
        expect(
          geometry.clippedSectionContents,
          `${route} 在 ${view.width}×${view.height} / ${view.zoom * 100}% 下存在溢出机械边框的内容`,
        ).toEqual([])
        const safeName = route.replaceAll('/', '-')
        const zoomSuffix = view.zoom === 1 ? '' : `-${view.zoom * 100}pct`
        const screenshotBase64 = await app.evaluate(async ({ BrowserWindow }) =>
          (await BrowserWindow.getAllWindows()[0]!.webContents.capturePage()).toPNG().toString('base64'))
        writeFileSync(
          resolve(screenshotRoot, `${view.width}x${view.height}${zoomSuffix}-${safeName}.png`),
          Buffer.from(screenshotBase64, 'base64'),
        )
      }
      if (view.zoom === 1) {
        await page.getByRole('button', { name: '道具', exact: true }).click()
        const itemLayout = await page.evaluate(() => {
          const workspace = document.querySelector<HTMLElement>('.items-workspace')!
          const workspaceBounds = workspace.getBoundingClientRect()
          const sections = Array.from(workspace.querySelectorAll<HTMLElement>(':scope > .inventory-column'))
          const sectionBounds = sections.map(section => section.getBoundingClientRect())
          return {
            overflowY: getComputedStyle(workspace).overflowY,
            clientHeight: workspace.clientHeight,
            scrollHeight: workspace.scrollHeight,
            topOffsets: sectionBounds.map(bounds => Math.abs(bounds.top - workspaceBounds.top)),
            bottomOffsets: sectionBounds.map(bounds => Math.abs(bounds.bottom - workspaceBounds.bottom)),
          }
        })
        if (view.width === 1280) {
          expect(itemLayout.topOffsets.every(offset => offset <= 1)).toBe(true)
          expect(itemLayout.bottomOffsets.every(offset => offset <= 1)).toBe(true)
        } else {
          expect(itemLayout.overflowY).toBe('auto')
          expect(itemLayout.scrollHeight).toBeGreaterThan(itemLayout.clientHeight)
        }
      }
    }
    expect(rendererErrors).toEqual([])
  } finally {
    await app.close()
    rmSync(dataRoot, { recursive: true, force: true })
  }
})

test('installs, authenticates, and disconnects the Unity Editor bridge', async () => {
  const dataRoot = mkdtempSync(resolve(tmpdir(), 'loopstructor-editor-bridge-e2e-'))
  const projectRoot = resolve(dataRoot, 'Loopstructor2')
  const instancesRoot = resolve(dataRoot, 'editor-instances')
  const artifactRoot = resolve(dataRoot, 'artifacts/editor/e2e')
  const instanceId = `editor-e2e-${process.pid}`
  const token = 'e2e-private-token-'.padEnd(64, 'x')
  const assemblySha256 = 'a'.repeat(64)
  const iconBytes = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+AvzZ2QAAAABJRU5ErkJggg==', 'base64')
  const iconFile = 'catalog-icons/e2e.png'
  const iconSha256 = createHash('sha256').update(iconBytes).digest('hex')
  const observedCommands: string[] = []
  let observedAuthorization = ''
  mkdirSync(resolve(projectRoot, 'Assets'), { recursive: true })
  mkdirSync(resolve(projectRoot, 'Packages'), { recursive: true })
  mkdirSync(resolve(projectRoot, 'ProjectSettings'), { recursive: true })
  mkdirSync(instancesRoot, { recursive: true })
  mkdirSync(artifactRoot, { recursive: true })
  mkdirSync(resolve(artifactRoot, 'catalog-icons'), { recursive: true })
  writeFileSync(resolve(artifactRoot, iconFile), iconBytes)
  writeFileSync(resolve(projectRoot, 'ProjectSettings/ProjectVersion.txt'), 'm_EditorVersion: 2022.3.62f3c1\n')

  const bridge = createServer((request, response) => {
    observedAuthorization = request.headers.authorization ?? ''
    if (observedAuthorization !== `Bearer ${token}`) {
      response.writeHead(401, { 'content-type': 'application/json' })
      response.end('{"success":false,"error":"unauthorized"}')
      return
    }
    if (request.method === 'GET' && request.url === '/api/status') {
      response.writeHead(200, { 'content-type': 'application/json' })
      response.end(JSON.stringify({
        success: true, schemaVersion: 1, protocolVersion: 1, instanceId, processId: process.pid, projectPath: projectRoot,
        unityVersion: '2022.3.62f3c1', gameVersion: '1.390', sceneName: 'StartGameScene',
        mode: 'editor-play', runtimeReady: true, compiling: false, assemblySha256,
        message: 'Unity Editor Play Mode 运行控制已启动。',
      }))
      return
    }
    if (request.method === 'POST' && request.url === '/api/command') {
      let body = ''
      request.setEncoding('utf8')
      request.on('data', chunk => { body += chunk })
      request.on('end', () => {
        const command = (JSON.parse(body) as { command: string }).command
        observedCommands.push(command)
        response.writeHead(200, { 'content-type': 'application/json' })
        response.end(JSON.stringify({
          success: true,
          message: 'ok',
          data: command === 'cheat.queryCatalog'
            ? {
                vehicles: [{ id: 'Vehicle_Shell_L1', name: '测试战车', iconFile, iconSha256 }],
                enchantments: [], disposables: [], relics: [], enemies: [], catapultPoints: [],
              }
            : { available: true, enabled: true, enemyIdsVisible: false, enemyBuffsVisible: false, baseGodMode: false, mapSkipEnabled: false },
        }))
      })
      return
    }
    response.writeHead(404, { 'content-type': 'application/json' })
    response.end('{"success":false,"error":"route_not_found"}')
  })
  await new Promise<void>((accept, reject) => {
    bridge.once('error', reject)
    bridge.listen(0, '127.0.0.1', accept)
  })
  const port = (bridge.address() as AddressInfo).port
  const instancePath = resolve(instancesRoot, `${instanceId}.json`)
  const writeHeartbeat = () => writeFileSync(instancePath, JSON.stringify({
    schemaVersion: 1, protocolVersion: 1, instanceId, kind: 'editor', processId: process.pid,
    displayName: 'Unity Editor · Loopstructor2', projectPath: projectRoot,
    unityExecutablePath: process.execPath, unityVersion: '2022.3.62f3c1', gameVersion: '1.390',
    sceneName: 'StartGameScene', mode: 'editor-play', runtimeReady: true, port, token,
    assemblySha256, artifactRoot, lastSeenAt: new Date().toISOString(),
  }))
  writeHeartbeat()
  const heartbeat = setInterval(writeHeartbeat, 2_000)

  const app = await electron.launch({
    args: ['.'],
    cwd: process.cwd(),
    env: {
      ...process.env,
      LOCALAPPDATA: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_DESKTOP_USER_DATA_ROOT: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_HOST_PATH: hostPath,
      LOOPSTRUCTOR_AUTOPLAYER_HOST_DATA_ROOT: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_DISTRIBUTION_ROOT: repositoryRoot,
    },
  })

  try {
    const page = await app.firstWindow()
    await expect(page.getByText('Loopstructor 2 QA Tool', { exact: true })).toBeVisible()
    await expect(page.locator('.titlebar-status')).not.toContainText('正在启动 Host', { timeout: 15_000 })
    const inspection = await page.evaluate(project => window.loopstructorDesktop.validateUnityProject(project), projectRoot)
    expect(inspection.valid).toBe(true)
    const installed = await page.evaluate(() => window.loopstructorDesktop.installEditorBridge())
    expect(installed.success).toBe(true)
    const managedRoot = resolve(projectRoot, 'Packages/com.loopstructor.qa-editor-bridge/Editor/Managed')
    expect(existsSync(resolve(managedRoot, 'Loopstructor.AutoPlayer.EditorBridge.Runtime.dll'))).toBe(true)
    expect(existsSync(resolve(managedRoot, 'Loopstructor.AutoPlayer.Core.dll'))).toBe(true)
    expect(existsSync(resolve(managedRoot, 'BepInEx.dll'))).toBe(false)
    expect(existsSync(resolve(managedRoot, '0Harmony.dll'))).toBe(false)

    await expect.poll(async () => (await page.evaluate(() => window.loopstructorDesktop.listEditorInstances())).length).toBe(1)
    await expect(page.getByText('Unity Editor · Loopstructor2', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: '连接', exact: true }).click()
    await expect(page.locator('.titlebar-status')).toContainText('Unity Editor Play Mode 已安全连接')

    const state = await page.evaluate(() => window.loopstructorDesktop.cheatCommand('cheat.queryState', {}))
    expect(state.success).toBe(true)
    expect(state.data?.enabled).toBe(true)
    const catalog = await page.evaluate(() => window.loopstructorDesktop.cheatCommand('cheat.queryCatalog', {}))
    expect(catalog.success).toBe(true)
    expect(catalog.data?.vehicles?.[0]?.iconDataUrl).toBe(`data:image/png;base64,${iconBytes.toString('base64')}`)
    const snapshot = await page.evaluate(() => window.loopstructorDesktop.getSnapshot())
    const publicJson = JSON.stringify(snapshot)
    expect(snapshot.connection.trusted).toBe(true)
    expect(publicJson).not.toContain(token)
    expect(publicJson).not.toContain('"port"')
    expect(publicJson).not.toContain('pipeName')
    expect(observedAuthorization).toBe(`Bearer ${token}`)
    expect(observedCommands).toContain('cheat.queryState')
    await page.getByRole('button', { name: '自动游玩', exact: true }).click()
    await expect(page.getByText('Editor 不支持自动游玩', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '开始', exact: true })).toBeDisabled()

    clearInterval(heartbeat)
    rmSync(instancePath, { force: true })
    await expect.poll(async () => (await page.evaluate(() => window.loopstructorDesktop.getSnapshot())).connection.trusted).toBe(false)
    await expect(page.locator('.titlebar-status')).toContainText('Unity Editor 连接已断开', { timeout: 15_000 })
  } finally {
    clearInterval(heartbeat)
    await app.close()
    await new Promise<void>(resolveClose => bridge.close(() => resolveClose()))
    rmSync(dataRoot, { recursive: true, force: true })
  }
})

test('reuses the Electron runtime for the updater mode', async () => {
  const dataRoot = mkdtempSync(resolve(tmpdir(), 'loopstructor-electron-updater-e2e-'))
  const readyRoot = resolve(tmpdir(), 'LoopstructorAutoPlayerUpdater')
  const readyFile = resolve(readyRoot, `ready-e2e-${process.pid}.signal`)
  mkdirSync(readyRoot, { recursive: true })
  rmSync(readyFile, { force: true })
  const app = await electron.launch({
    args: ['.', '--updater', '--window-ready-file', readyFile, '--desktop-staged-run'],
    cwd: process.cwd(),
    env: {
      ...process.env,
      LOCALAPPDATA: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_DESKTOP_USER_DATA_ROOT: dataRoot,
    },
  })

  try {
    const page = await app.firstWindow()
    await expect.poll(() => existsSync(readyFile)).toBe(true)
    await expect(page.getByText('更新未完成', { exact: true })).toBeVisible()
    expect(await page.evaluate(() => window.loopstructorDesktop.isUpdater)).toBe(true)
    await expect(page.locator('.updater-message')).toHaveText('apply 命令必须提供 --target <release-root>。')
    for (const size of [{ width: 680, height: 520 }, { width: 760, height: 600 }]) {
      await app.evaluate(({ BrowserWindow }, nextSize) => {
        BrowserWindow.getAllWindows()[0]?.setSize(nextSize.width, nextSize.height)
      }, size)
      await page.waitForTimeout(100)
      const layout = await page.evaluate(() => {
        const appRoot = document.querySelector<HTMLElement>('#app')!
        const card = document.querySelector<HTMLElement>('.updater-card')!
        const bounds = card.getBoundingClientRect()
        return {
          rootWidth: appRoot.clientWidth,
          rootScrollWidth: appRoot.scrollWidth,
          rootHeight: appRoot.clientHeight,
          rootScrollHeight: appRoot.scrollHeight,
          left: bounds.left,
          top: bounds.top,
          right: bounds.right,
          bottom: bounds.bottom,
        }
      })
      expect(layout.rootScrollWidth).toBeLessThanOrEqual(layout.rootWidth)
      expect(layout.rootScrollHeight).toBeLessThanOrEqual(layout.rootHeight)
      expect(layout.left).toBeGreaterThanOrEqual(0)
      expect(layout.top).toBeGreaterThanOrEqual(54)
      expect(layout.right).toBeLessThanOrEqual(layout.rootWidth)
      expect(layout.bottom).toBeLessThanOrEqual(layout.rootHeight)
      await page.screenshot({
        path: resolve(screenshotRoot, `${size.width}x${size.height}-更新窗口.png`),
        animations: 'disabled',
      })
    }
    await expect(page.getByRole('button', { name: '退出', exact: true })).toBeVisible()
  } finally {
    await app.close()
    rmSync(dataRoot, { recursive: true, force: true })
    rmSync(readyFile, { force: true })
  }
})

test('flat copper panel keeps interaction, focus, and spacing states', async () => {
  const dataRoot = mkdtempSync(resolve(tmpdir(), 'loopstructor-electron-states-e2e-'))
  mkdirSync(screenshotRoot, { recursive: true })
  const app = await electron.launch({
    args: ['.'],
    cwd: process.cwd(),
    env: {
      ...process.env,
      LOCALAPPDATA: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_DESKTOP_USER_DATA_ROOT: dataRoot,
      LOOPSTRUCTOR_AUTOPLAYER_HOST_PATH: hostPath,
      LOOPSTRUCTOR_AUTOPLAYER_HOST_DATA_ROOT: dataRoot,
    },
  })

  try {
    const page = await app.firstWindow()
    await expect(page.getByText('Loopstructor 2 QA Tool', { exact: true })).toBeVisible()
    await expect(page.locator('.titlebar-status')).not.toContainText('正在启动 Host', { timeout: 15_000 })
    await app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.setSize(1280, 860))
    // Let the first Host snapshot land: a late snapshot re-render swaps DOM nodes
    // and silently drops the CSS :hover state in the middle of an assertion.
    await page.waitForTimeout(900)

    const activeNav = page.locator('.nav-item.active')
    await page.locator('.brand-logo').hover()
    const navBefore = await activeNav.evaluate(element => ({
      background: getComputedStyle(element).backgroundColor,
      shadow: getComputedStyle(element).boxShadow,
      face: getComputedStyle(element, '::before').backgroundImage,
      filter: getComputedStyle(element).filter,
    }))
    expect(navBefore.background).toBe('rgb(44, 36, 27)')
    expect(navBefore.shadow).toContain('inset')
    expect(navBefore.face).toBe('none')
    expect(navBefore.filter).toBe('none')
    await expect(page.locator('.nav-item.active .rail-cog')).toHaveCount(0)
    await expect.poll(async () => {
      await activeNav.hover()
      return activeNav.evaluate(element => ({
        background: getComputedStyle(element).backgroundColor,
        face: getComputedStyle(element, '::before').backgroundImage,
        filter: getComputedStyle(element).filter,
      }))
    }, { timeout: 8_000, message: '导航项 hover 状态应稳定出现' }).toMatchObject({
      background: 'rgb(55, 40, 24)',
      face: 'none',
      filter: 'none',
    })
    await page.screenshot({ path: resolve(screenshotRoot, '1280x860-state-nav-hover.png'), animations: 'disabled' })

    const secondary = page.getByRole('button', { name: '刷新连接', exact: true })
    const secondaryBefore = await secondary.evaluate(element => ({
      background: getComputedStyle(element).backgroundColor,
      face: getComputedStyle(element, '::before').backgroundImage,
    }))
    expect(secondaryBefore.background).toBe('rgb(51, 38, 28)')
    expect(secondaryBefore.face).toBe('none')
    await expect.poll(async () => {
      await secondary.hover()
      return secondary.evaluate(element => getComputedStyle(element).backgroundColor)
    }, { message: '次要按钮 hover 背景应完成过渡' }).toBe('rgb(61, 45, 31)')
    await secondary.focus()
    const focusedSecondary = await secondary.evaluate(element => ({
      outlineStyle: getComputedStyle(element).outlineStyle,
      outlineWidth: getComputedStyle(element).outlineWidth,
    }))
    // Chromium reports zoomed lengths fractionally when the page zoom factor is
    // fractional (125% system DPI here), so length assertions normalize by zoom.
    const zoom = await app.evaluate(({ BrowserWindow }) =>
      BrowserWindow.getAllWindows()[0]!.webContents.getZoomFactor())
    const cssLength = (value: string) => Number.parseFloat(value) * zoom

    expect(focusedSecondary.outlineStyle).toBe('solid')
    // Chromium snaps border/outline widths to whole device pixels, so a 2px ring
    // is reported as 1.6px or 2.4px when the display DPR is fractional (1.25).
    expect(Math.abs(Number.parseFloat(focusedSecondary.outlineWidth) - 2)).toBeLessThan(0.5)

    const assertFlatPrimaryButtons = async () => {
      const primaryButtons = page.locator('.button.primary:not(:disabled)')
      expect(await primaryButtons.count()).toBeGreaterThan(0)
      for (let index = 0; index < await primaryButtons.count(); index += 1) {
        const palette = await primaryButtons.nth(index).evaluate(element => ({
          color: getComputedStyle(element).color,
          background: getComputedStyle(element).backgroundColor,
          border: getComputedStyle(element).borderTopColor,
          face: getComputedStyle(element, '::before').backgroundImage,
        }))
        expect(palette).toMatchObject({
          color: 'rgb(255, 227, 162)',
          background: 'rgb(90, 58, 31)',
          border: 'rgb(214, 154, 69)',
        })
        expect(palette.face).toBe('none')
      }
    }

    await assertFlatPrimaryButtons()
    const directoryButton = page.getByRole('button', { name: '选择目录', exact: true })
    const directoryRest = await directoryButton.evaluate(element => ({
      color: getComputedStyle(element).color,
      background: getComputedStyle(element).backgroundColor,
      border: getComputedStyle(element).borderTopColor,
    }))
    expect(directoryRest).toMatchObject({
      color: 'rgb(255, 227, 162)',
      background: 'rgb(90, 58, 31)',
      border: 'rgb(214, 154, 69)',
    })
    await expect.poll(async () => {
      await directoryButton.hover()
      return directoryButton.evaluate(element => getComputedStyle(element).color)
    }, { message: '选择目录按钮的 hover 颜色应完成过渡' }).toBe('rgb(255, 240, 198)')

    const disabledLaunch = page.getByRole('button', { name: '启动游戏', exact: true })
    await expect(disabledLaunch).toBeDisabled()
    const disabledMaterial = await disabledLaunch.evaluate(element => ({
      face: getComputedStyle(element, '::before').backgroundImage,
      background: getComputedStyle(element).backgroundColor,
      border: getComputedStyle(element).borderTopColor,
    }))
    expect(disabledMaterial.face).toBe('none')
    expect(disabledMaterial.background).not.toBe('rgba(0, 0, 0, 0)')
    expect(disabledMaterial.border).not.toBe('rgba(0, 0, 0, 0)')
    await page.screenshot({ path: resolve(screenshotRoot, '1280x860-state-button-focus-disabled.png'), animations: 'disabled' })

    const spacing = await page.evaluate(() => {
      const pageHost = document.querySelector<HTMLElement>('.page-host')!
      const hostBounds = pageHost.getBoundingClientRect()
      const heading = pageHost.querySelector<HTMLElement>(':scope > *')!.getBoundingClientRect()
      const hostStyles = getComputedStyle(pageHost)
      const chrome = getComputedStyle(document.querySelector<HTMLElement>('.content-shell')!, '::before')
      return {
        paddingLeft: hostStyles.paddingLeft,
        paddingTop: hostStyles.paddingTop,
        contained: hostBounds.top <= heading.top && hostBounds.left <= heading.left,
        chromeDisplay: chrome.display,
      }
    })
    // UI scale can be a fractional system DPI factor (1.25 here); Chromium then
    // reports zoomed lengths fractionally, so length assertions stay tolerance-based.
    expect(Math.abs(cssLength(spacing.paddingLeft) - 22)).toBeLessThan(0.2)
    expect(Math.abs(cssLength(spacing.paddingTop) - 18)).toBeLessThan(0.2)
    expect(spacing.contained).toBe(true)
    expect(spacing.chromeDisplay).toBe('none')

    await page.getByRole('button', { name: '界面与更新', exact: true }).click()
    await assertFlatPrimaryButtons()
    await expect(page.getByRole('radio', { name: '天穹机械终端' })).toHaveCount(0)
    await expect(page.getByText('皮肤', { exact: true })).toHaveCount(0)
    await page.screenshot({ path: resolve(screenshotRoot, '1280x860-state-selected-hover.png'), animations: 'disabled' })

    await page.getByRole('button', { name: '战车', exact: true }).click()
    const activeTab = page.locator('.type-switcher button.active')
    await expect.poll(async () => {
      await activeTab.hover()
      return activeTab.evaluate(element => getComputedStyle(element).backgroundColor)
    }).toBe('rgb(44, 36, 27)')
    await expect(page.locator('.cheat-control-bar')).toHaveCount(0)
    await page.screenshot({ path: resolve(screenshotRoot, '1280x860-state-tab-hover.png'), animations: 'disabled' })

    const closeButton = page.getByRole('button', { name: '关闭', exact: true })
    const closeRest = await closeButton.evaluate(element => ({
      image: getComputedStyle(element).backgroundImage,
    }))
    expect(closeRest.image).toBe('none')
    await expect.poll(async () => {
      await closeButton.hover()
      return closeButton.evaluate(element => getComputedStyle(element).backgroundColor)
    }).toBe('rgb(143, 47, 38)')
  } finally {
    await app.close()
    rmSync(dataRoot, { recursive: true, force: true })
  }
})
