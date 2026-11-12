// Sidebar open state, adapted from TailAdmin Vue (MIT).
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

const open = ref(true)
const openMobile = ref(false)
const isMobile = ref(false)

export function useSidebar() {
    function toggleSidebar() {
        if (window.innerWidth < 1280) openMobile.value = !openMobile.value
        else open.value = !open.value
    }

    function closeMobile() {
        openMobile.value = false
    }

    return { open, openMobile, isMobile, toggleSidebar, closeMobile }
}

export function installSidebar() {
    const router = useRouter()
    let removeHook: (() => void) | undefined

    const onResize = () => {
        isMobile.value = window.innerWidth < 1280
        if (!isMobile.value) openMobile.value = false
    }

    const onKey = (event: KeyboardEvent) => {
        if (event.key.toLowerCase() === 'b' && (event.metaKey || event.ctrlKey)) {
            event.preventDefault()
            if (window.innerWidth < 1280) openMobile.value = !openMobile.value
            else open.value = !open.value
        }
    }

    onMounted(() => {
        onResize()
        window.addEventListener('resize', onResize)
        window.addEventListener('keydown', onKey)
        removeHook = router.afterEach(() => {
            openMobile.value = false
        })
    })

    onUnmounted(() => {
        window.removeEventListener('resize', onResize)
        window.removeEventListener('keydown', onKey)
        removeHook?.()
        document.body.classList.remove('overflow-hidden')
    })

    watch(openMobile, (value) => {
        document.body.classList.toggle('overflow-hidden', value)
    })
}
