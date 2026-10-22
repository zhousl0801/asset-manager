import type { EntryKind } from '../types/entry'

export const ExpenseCategories = [
  '餐饮',
  '购物',
  '日用',
  '交通',
  '居家',
  '服饰',
  '娱乐',
  '通讯',
  '医疗',
  '学习',
  '运动',
  '旅行',
  '宠物',
  '人情',
  '其他'
]

export const IncomeCategories = ['工资', '奖金', '其他收入']

export function categoriesFor(kind: EntryKind): string[] {
  return kind === 'income' ? IncomeCategories : ExpenseCategories
}
