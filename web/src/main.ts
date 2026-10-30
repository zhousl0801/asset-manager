import { createPinia } from 'pinia'
import { createApp } from 'vue'
import '../../docs/design/ui-color-tokens.css'
import './styles/tokens.css'
import './style.css'
import App from './App.vue'
import router from './router'
import { initializeTheme } from './utils/theme'

initializeTheme()

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.mount('#app')
