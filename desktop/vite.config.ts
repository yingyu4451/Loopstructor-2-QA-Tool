import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import managerPackage from './package.json' with { type: 'json' }

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  base: './',
  define: {
    __APP_VERSION__: JSON.stringify(managerPackage.version),
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  test: {
    environment: 'happy-dom',
    globals: true,
    include: ['tests/**/*.spec.ts'],
  },
})
