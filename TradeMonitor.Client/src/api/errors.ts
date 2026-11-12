export class LedgerReadError extends Error {
    readonly status: number
    readonly code: string

    constructor(status: number, code: string, message: string) {
        super(message)
        this.name = 'LedgerReadError'
        this.status = status
        this.code = code
    }
}

export function isUnknownWallet(err: unknown): err is LedgerReadError {
    return err instanceof LedgerReadError && err.status === 404 && err.code === 'UnknownWallet'
}
