export type FilterField = string | number | null | undefined

export function filterQuery(text: string): string {
    return text.trim().toLowerCase()
}

export function rowMatches(query: string, fields: readonly FilterField[]): boolean {
    const tokens = filterQuery(query).split(/\s+/).filter(Boolean)
    if (tokens.length === 0) return true
    const haystack = fields
        .filter((field) => field != null && String(field) !== '')
        .map((field) => String(field).toLowerCase())
        .join(' ')
    return tokens.every((token) => haystack.includes(token))
}

export function filterRows<T>(rows: readonly T[], query: string, fieldsOf: (row: T) => readonly FilterField[]): T[] {
    if (filterQuery(query) === '') return rows as T[]
    return rows.filter((row) => rowMatches(query, fieldsOf(row)))
}
