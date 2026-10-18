import { mount } from '@vue/test-utils'
import type { Component } from 'vue'
import { tooltipDirective } from '../../src/directives/tooltip'

type MountOptions = Parameters<typeof mount>[1]

/**
 * Mounts a component with the real `v-tooltip` directive registered so Vue Test Utils does not
 * emit "Failed to resolve directive: tooltip" warnings. Extra directives supplied by a test are
 * merged on top of the shared defaults.
 */
export function mountWithTooltip(component: Component, options: MountOptions = {}) {
  return mount(component, {
    ...options,
    global: {
      ...options?.global,
      directives: { tooltip: tooltipDirective, ...options?.global?.directives },
    },
  })
}
