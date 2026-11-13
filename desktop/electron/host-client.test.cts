import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import path from 'node:path'
import test from 'node:test'
import { parseHostProcessList, selectOrphanedHosts, stopChildProcess } from './host-client.cjs'

class FakeChildProcess extends EventEmitter {
  exitCode: number | null = null
  killCalls = 0
  readonly stdin = {
    writable: true,
    end: () => {
      this.stdin.writable = false
      setTimeout(() => {
        this.exitCode = 0
        this.emit('exit', 0)
      }, 10)
    },
  }

  kill(): boolean {
    this.killCalls += 1
    this.exitCode = 1
    this.emit('exit', 1)
    return true
  }
}

class StubbornChildProcess extends FakeChildProcess {
  override readonly stdin = {
    writable: true,
    end: () => { this.stdin.writable = false },
  }
}

test('waits for the Host process to exit after closing stdin', async () => {
  const child = new FakeChildProcess()

  await stopChildProcess(child as never, 100, 100)

  assert.equal(child.exitCode, 0)
  assert.equal(child.killCalls, 0)
})

test('terminates the owned Host when graceful shutdown times out', async () => {
  const child = new StubbornChildProcess()

  await stopChildProcess(child as never, 10, 100)

  assert.equal(child.exitCode, 1)
  assert.equal(child.killCalls, 1)
})

test('parses the PowerShell host process list in both object and array shapes', () => {
  const single = JSON.stringify({
    ProcessId: 4242,
    ParentProcessId: 1111,
    ExecutablePath: 'C:\\Tools\\manager\\Loopstructor.AutoPlayer.Host.exe',
  })
  assert.deepEqual(parseHostProcessList(single), [
    {
      processId: 4242,
      parentProcessId: 1111,
      executablePath: 'C:\\Tools\\manager\\Loopstructor.AutoPlayer.Host.exe',
    },
  ])

  const multiple = JSON.stringify([
    { ProcessId: 1, ParentProcessId: 10, ExecutablePath: 'C:\\A\\Loopstructor.AutoPlayer.Host.exe' },
    { ProcessId: 2, ParentProcessId: 20, ExecutablePath: null },
  ])
  assert.equal(parseHostProcessList(multiple).length, 2)
  assert.equal(parseHostProcessList(multiple)[1].executablePath, null)
  assert.deepEqual(parseHostProcessList('   '), [])
  assert.deepEqual(parseHostProcessList('not json'), [])
})

test('selects only Hosts that live in this manager directory and lost their parent', () => {
  const managerDirectory = path.join('C:\\', 'Tools', 'manager')
  const entries = [
    {
      processId: 1,
      parentProcessId: 100,
      executablePath: path.join(managerDirectory, 'Loopstructor.AutoPlayer.Host.exe'),
    },
    {
      processId: 2,
      parentProcessId: 200,
      executablePath: path.join(managerDirectory, 'Loopstructor.AutoPlayer.Host.exe'),
    },
    {
      processId: 3,
      parentProcessId: 300,
      executablePath: path.join('C:\\', 'Other', 'Loopstructor.AutoPlayer.Host.exe'),
    },
    { processId: 4, parentProcessId: 400, executablePath: null },
  ]

  const aliveParents = new Set([100, 300])
  const orphans = selectOrphanedHosts(entries, managerDirectory, (processId) => aliveParents.has(processId))

  assert.deepEqual(
    orphans.map((entry) => entry.processId),
    [2],
  )
})
