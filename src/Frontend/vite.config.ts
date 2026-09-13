/// <reference types="vitest/config" />
import { createRequire } from 'node:module'
import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const repoRoot = path.resolve(import.meta.dirname, '../..')

// tests/Frontend.Tests/ lives outside this npm package (mirrors the .NET test-project
// convention used elsewhere in the repo), so Node's normal node_modules lookup — which
// walks up from the *importing file's* own directory — never reaches src/Frontend/node_modules.
// Resolving these explicitly from vite.config.ts's own location (which IS inside the package)
// sidesteps that instead of relying on Vite's dependency crawl to discover them one at a time.
const require = createRequire(import.meta.url)
function resolveFromHere(specifier: string): string {
  return require.resolve(specifier)
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    fs: {
      allow: [repoRoot],
    },
  },
  resolve: {
    alias: {
      'react/jsx-dev-runtime': resolveFromHere('react/jsx-dev-runtime'),
      'react/jsx-runtime': resolveFromHere('react/jsx-runtime'),
      'react-dom/client': resolveFromHere('react-dom/client'),
      '@testing-library/react': resolveFromHere('@testing-library/react'),
      '@testing-library/user-event': resolveFromHere('@testing-library/user-event'),
      '@testing-library/jest-dom': resolveFromHere('@testing-library/jest-dom'),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    include: ['../../tests/Frontend.Tests/**/*.test.{ts,tsx}'],
    server: {
      fs: {
        allow: [repoRoot],
      },
    },
  },
})
