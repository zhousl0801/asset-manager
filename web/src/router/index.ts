import { createRouter, createWebHashHistory } from 'vue-router'
import HomeView from '../views/HomeView.vue'

const router = createRouter({
  history: createWebHashHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/plans', name: 'plans', component: () => import('../views/PlanView.vue') },
    {
      path: '/',
      name: 'home',
      component: HomeView
    },
    {
      path: '/setup',
      name: 'setup',
      component: () => import('../views/SetupView.vue')
    },
    {
      path: '/assets',
      name: 'assets',
      component: () => import('../views/AssetsView.vue')
    },
    {
      path: '/ledger',
      name: 'ledger',
      component: () => import('../views/LedgerView.vue')
    },
    {
      path: '/mine',
      name: 'mine',
      component: () => import('../views/MineView.vue')
    },
    {
      path: '/categories',
      name: 'categories',
      component: () => import('../views/CategoriesView.vue')
    },
    {
      path: '/security',
      name: 'security',
      component: () => import('../views/SecurityView.vue')
    },
    {
      path: '/backup',
      name: 'backup',
      component: () => import('../views/BackupView.vue')
    },
    {
      path: '/quotes',
      name: 'quotes',
      component: () => import('../views/QuoteStatusView.vue')
    },
    {
      path: '/cash',
      name: 'cash',
      component: () => import('../views/CashView.vue')
    },
    {
      path: '/provident',
      name: 'provident',
      component: () => import('../views/ProvidentView.vue')
    },
    {
      path: '/gold',
      name: 'gold',
      component: () => import('../views/GoldView.vue')
    },
    {
      path: '/funds',
      name: 'funds',
      component: () => import('../views/FundsView.vue')
    },
    {
      path: '/entry',
      name: 'entry',
      component: () => import('../views/EntryView.vue')
    }
  ]
})

export default router
