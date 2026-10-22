import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'prompt',
      manifest: {
        name: '资产管家',
        short_name: '资产管家',
        description: '个人资产观察与日常记账',
        lang: 'zh-CN',
        theme_color: '#002FA7',
        background_color: '#F6F7FB',
        display: 'standalone',
        start_url: '/',
        icons: [
          {
            src: '/favicon.svg',
            sizes: 'any',
            type: 'image/svg+xml',
            purpose: 'any'
          }
        ]
      }
    })
  ],
  server: {
    proxy: {
      '/api': {
        target: process.env.ASSET_KEEPER_API_PROXY ?? 'http://localhost:5069',
        changeOrigin: true
      }
    }
  }
})
