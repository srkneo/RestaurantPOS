import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7266',
        changeOrigin: true,
        secure: false,
        // Dev server only: the API marks its login cookie Secure, and browsers drop Secure cookies on plain
        // http addresses other than localhost (a laptop or phone opening http://192.168.x.x:5173).
        configure: (proxy) => {
          proxy.on('proxyRes', (proxyRes) => {
            const cookies = proxyRes.headers['set-cookie'];
            if (cookies) proxyRes.headers['set-cookie'] = cookies.map((c) => c.replace(/;\s*secure/gi, ''));
          });
        }
      },
      '/orderHub': {
        target: 'https://localhost:7266',
        changeOrigin: true,
        ws: true,
        secure: false
      }
    }
  }
})