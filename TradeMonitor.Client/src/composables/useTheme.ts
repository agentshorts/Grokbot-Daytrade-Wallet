import { ref } from 'vue'

const STORAGE_KEY = 'color-scheme'

export const isDark = ref(typeof document !== 'undefined' && document.documentElement.classList.contains('dark'))

function apply(dark: boolean) {
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.style.setProperty('color-scheme', dark ? 'dark' : 'light')
    localStorage.setItem(STORAGE_KEY, dark ? 'dark' : 'light')
    isDark.value = dark
}

export function syncTheme() {
    if (typeof localStorage === 'undefined') return
    const dark = localStorage.getItem(STORAGE_KEY) === 'dark' || document.documentElement.classList.contains('dark')
    apply(dark)
}

export function toggleTheme() {
    apply(!document.documentElement.classList.contains('dark'))
}

export function useTheme() {
    return { isDark, toggleTheme, syncTheme }
}
