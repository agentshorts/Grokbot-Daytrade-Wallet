/** Archived wallets stay in the ledger. Lists and totals hide them until the viewer asks. */
export function visibleWallets<T extends { isArchived: boolean }>(rows: T[], showArchived: boolean): T[] {
    if (showArchived) return rows
    return rows.filter((row) => !row.isArchived)
}

/** Omit the flag when archived wallets should stay out of a ledger read. The API treats a missing flag as false. */
export function includeArchivedQuery(showArchived: boolean): { includeArchived?: true } {
    return showArchived ? { includeArchived: true } : {}
}
