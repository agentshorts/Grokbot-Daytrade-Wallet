import { inject, type InjectionKey } from 'vue'
import { ledger, LedgerReadError, type LedgerClient } from '@/api'

/** Tests provide a fake client. The page uses the shared GET ledger client. */
export const ledgerClientKey: InjectionKey<LedgerClient> = Symbol('ledgerClient')

export function useLedger(): LedgerClient {
    return inject(ledgerClientKey, ledger)
}

export function explainLedgerError(err: unknown): { message: string; code: string } {
    if (err instanceof LedgerReadError) {
        return {
            message: err.message || 'Could not load the ledger.',
            code: err.status === 401 || err.status === 403 ? 'auth' : err.code || 'error',
        }
    }
    if (err instanceof Error && err.message) return { message: err.message, code: 'error' }
    return { message: 'Could not load the ledger.', code: 'error' }
}
