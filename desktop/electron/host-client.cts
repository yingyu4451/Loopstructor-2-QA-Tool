import { ChildProcessWithoutNullStreams, spawn, spawnSync } from 'node:child_process'
import { createInterface } from 'node:readline'
import { EventEmitter } from 'node:events'
import path from 'node:path'
import fs from 'node:fs'

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (reason: Error) => void
  timer: NodeJS.Timeout
}

async function waitForProcessExit(child: ChildProcessWithoutNullStreams, timeoutMs: number): Promise<boolean> {
  if (child.exitCode !== null) return true
  return await new Promise<boolean>((resolve) => {
    let settled = false
    const finish = (exited: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      child.off('exit', onExit)
      resolve(exited)
    }
    const onExit = () => finish(true)
    const timer = setTimeout(() => finish(false), timeoutMs)
    child.once('exit', onExit)
    if (child.exitCode !== null) finish(true)
  })
}

export async function stopChildProcess(
  child: ChildProcessWithoutNullStreams,
  gracefulTimeoutMs = 3000,
  forcedTimeoutMs = 1000,
): Promise<void> {
  if (child.exitCode !== null) return
  if (child.stdin.writable) child.stdin.end()
  if (await waitForProcessExit(child, gracefulTimeoutMs)) return
  child.kill()
  await waitForProcessExit(child, forcedTimeoutMs)
}

export interface HostProcessEntry {
  processId: number
  parentProcessId: number
  executablePath: string | null
}

export function parseHostProcessList(raw: string): HostProcessEntry[] {
  const text = raw.trim()
  if (!text) return []

  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return []
  }

  const rows = Array.isArray(parsed) ? parsed : [parsed]
  const entries: HostProcessEntry[] = []
  for (const row of rows) {
    if (row === null || typeof row !== 'object') continue
    const source = row as Record<string, unknown>
    const processId = Number(source.ProcessId ?? source.processId)
    const parentProcessId = Number(source.ParentProcessId ?? source.parentProcessId)
    const executablePathValue = source.ExecutablePath ?? source.executablePath
    if (!Number.isInteger(processId) || processId <= 0) continue
    entries.push({
      processId,
      parentProcessId: Number.isInteger(parentProcessId) && parentProcessId >= 0 ? parentProcessId : 0,
      executablePath:
        typeof executablePathValue === 'string' && executablePathValue.length > 0 ? executablePathValue : null,
    })
  }
  return entries
}

export function selectOrphanedHosts(
  entries: readonly HostProcessEntry[],
  managerDirectory: string,
  isProcessAlive: (processId: number) => boolean,
): HostProcessEntry[] {
  const expectedDirectory = path.resolve(managerDirectory).toLowerCase()
  return entries.filter((entry) => {
    if (!entry.executablePath) return false
    if (path.dirname(path.resolve(entry.executablePath)).toLowerCase() !== expectedDirectory) return false
    if (entry.parentProcessId > 0 && isProcessAlive(entry.parentProcessId)) return false
    return true
  })
}

function isWindowsProcessAlive(processId: number): boolean {
  if (processId <= 0) return false
  try {
    process.kill(processId, 0)
    return true
  } catch (error) {
    // EPERM means the process exists but we may not signal it — still alive.
    return (error as NodeJS.ErrnoException).code === 'EPERM'
  }
}

const hostProcessQuery =
  "Get-CimInstance Win32_Process -Filter \"Name='Loopstructor.AutoPlayer.Host.exe'\" | Select-Object ProcessId,ParentProcessId,ExecutablePath | ConvertTo-Json -Compress"

export function cleanupOrphanedHosts(
  managerDirectory: string,
  log: (message: string) => void = () => {},
): number {
  if (process.platform !== 'win32') return 0

  let stdout: string
  try {
    const result = spawnSync(
      'powershell.exe',
      ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', hostProcessQuery],
      { encoding: 'utf8', windowsHide: true, timeout: 15000 },
    )
    stdout = typeof result.stdout === 'string' ? result.stdout : ''
  } catch {
    return 0
  }

  const entries = parseHostProcessList(stdout)
  const orphans = selectOrphanedHosts(entries, managerDirectory, isWindowsProcessAlive)
  for (const orphan of orphans) {
    try {
      spawnSync('taskkill.exe', ['/F', '/PID', String(orphan.processId)], {
        encoding: 'utf8',
        windowsHide: true,
        timeout: 10000,
      })
      log(`已清理上次运行遗留的 Host 进程 ${orphan.processId}（父进程 ${orphan.parentProcessId} 已退出）。`)
    } catch {
      // Killing an orphan is best-effort; it must never block Manager start-up.
    }
  }
  return orphans.length
}

export class HostClient extends EventEmitter {
  private process?: ChildProcessWithoutNullStreams
  private readonly pending = new Map<string, PendingRequest>()
  private requestSequence = 0

  constructor(
    private readonly managerDirectory: string,
    private readonly distributionRoot: string,
  ) {
    super()
  }

  start(parentProcessId: number): void {
    if (this.process) return
    const configured = process.env.LOOPSTRUCTOR_AUTOPLAYER_HOST_PATH
    const packaged = path.join(this.managerDirectory, 'Loopstructor.AutoPlayer.Host.exe')
    const development = path.join(
      this.distributionRoot,
      'src',
      'Loopstructor.AutoPlayer.Host',
      'bin',
      'Debug',
      'net8.0-windows',
      'Loopstructor.AutoPlayer.Host.exe',
    )
    const executable = configured || (fs.existsSync(packaged) ? packaged : development)
    if (!fs.existsSync(executable)) {
      throw new Error(`找不到 .NET Host：${executable}`)
    }

    this.process = spawn(executable, ['--parent-pid', String(parentProcessId)], {
      cwd: this.managerDirectory,
      windowsHide: true,
      env: {
        ...process.env,
        LOOPSTRUCTOR_AUTOPLAYER_DISTRIBUTION_ROOT: this.distributionRoot,
      },
    })
    const lines = createInterface({ input: this.process.stdout })
    lines.on('line', (line) => this.handleLine(line))
    this.process.stderr.on('data', (chunk) => this.emit('diagnostic', String(chunk)))
    this.process.on('error', (error) => {
      this.emit('diagnostic', `无法启动 .NET Host：${error.message}`)
      this.rejectPending(new Error(`无法启动 .NET Host：${error.message}`))
      this.process = undefined
      this.emit('exit', null)
    })
    this.process.on('exit', (code) => {
      this.rejectPending(new Error(`.NET Host 已退出（代码 ${code ?? 'unknown'}）。`))
      this.process = undefined
      this.emit('exit', code)
    })
  }

  private rejectPending(error: Error): void {
    for (const request of this.pending.values()) {
      clearTimeout(request.timer)
      request.reject(error)
    }
    this.pending.clear()
  }

  async invoke(method: string, params?: unknown, timeoutMs = 45000): Promise<unknown> {
    if (!this.process || !this.process.stdin.writable) throw new Error('.NET Host 尚未启动。')
    const id = `${process.pid}-${Date.now()}-${++this.requestSequence}`
    const promise = new Promise<unknown>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new Error(`等待 Host 方法 ${method} 响应超时。`))
      }, timeoutMs)
      this.pending.set(id, { resolve, reject, timer })
    })
    this.process.stdin.write(`${JSON.stringify({ id, method, params: params ?? {} })}\n`)
    return promise
  }

  async stop(): Promise<void> {
    const child = this.process
    if (!child) return
    await stopChildProcess(child)
  }

  private handleLine(line: string): void {
    try {
      const message = JSON.parse(line) as {
        id?: string
        success?: boolean
        result?: unknown
        error?: string
        event?: string
        payload?: unknown
      }
      if (message.event) {
        this.emit('event', { event: message.event, payload: message.payload })
        return
      }
      if (!message.id) return
      const request = this.pending.get(message.id)
      if (!request) return
      clearTimeout(request.timer)
      this.pending.delete(message.id)
      if (message.success) request.resolve(message.result)
      else request.reject(new Error(message.error || 'Host 返回了未知错误。'))
    } catch (error) {
      this.emit('diagnostic', `Host 输出不是有效 JSON：${String(error)}`)
    }
  }
}
