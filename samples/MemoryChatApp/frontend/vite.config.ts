import { defineConfig } from 'vite'

export default defineConfig({
  server: {
    port: 3000,
    strictPort: true, // Required for ASP.NET SPA Proxy
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  }
})
