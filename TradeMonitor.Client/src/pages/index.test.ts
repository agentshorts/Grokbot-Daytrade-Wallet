import { describe, expect, it } from 'vite-plus/test'
import { mount } from '@vue/test-utils'
import HomePage from './index.vue'

describe('home page', () => {
    it('renders an empty TradeMonitor home page', () => {
        const wrapper = mount(HomePage)
        expect(wrapper.get('h1').text()).toBe('TradeMonitor')
        expect(wrapper.text()).toBe('TradeMonitor')
    })
})
