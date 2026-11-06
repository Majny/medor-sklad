// @ts-check
// Odesílání změn počtu kusů. Uložení jednoho řádku jdou za sebou (nikdy dvě najednou) a každé
// posílá verzi potvrzenou tím předchozím. Modul zároveň eviduje všechna rozpracovaná uložení.

import { ApiError, putQuantity } from "./api.js";

/**
 * @typedef {import("./api.js").StockRow} StockRow
 * @typedef {{
 *   onSaved: (quantity: number) => void,
 *   onConflict: (quantity: number, currentQuantity: number, currentVersion: number) => void,
 *   onError: (quantity: number, message: string) => void,
 * }} SaveHandlers
 */

/** @type {Set<Promise<void>>} */
const pending = new Set();
/** @type {(count: number) => void} */
let pendingListener = () => {};
let started = 0;

/** @param {(count: number) => void} listener volá se při každé změně počtu rozpracovaných uložení */
export function onPendingChange(listener) {
    pendingListener = listener;
}

export function pendingCount() {
    return pending.size;
}

/** Počet uložení od načtení stránky; podle změny main.js pozná, že se uložení předběhlo s načítáním gridu. */
export function savesStarted() {
    return started;
}

/** Počká na všechna rozpracovaná uložení (grid se před novým načtením nesmí předběhnout). */
export function whenSaved() {
    return Promise.allSettled([...pending]);
}

/**
 * Vrátí funkci save(quantity) pro jeden řádek. Úspěch přepíše quantity a version přímo v `row`.
 * @param {StockRow} row
 * @param {SaveHandlers} handlers
 */
export function createRowSaver(row, handlers) {
    let queue = Promise.resolve();

    /** @param {number} quantity */
    async function send(quantity) {
        try {
            const saved = await putQuantity(row.clientId, row.productId, { quantity, version: row.version });
            row.quantity = saved.quantity;
            row.version = saved.version;
            handlers.onSaved(quantity);
        } catch (error) {
            if (error instanceof ApiError && error.status === 409 && "currentVersion" in error.extensions) {
                const { currentQuantity, currentVersion } = error.extensions;
                handlers.onConflict(quantity, Number(currentQuantity), Number(currentVersion));
            } else {
                handlers.onError(quantity, error instanceof Error ? error.message : String(error));
            }
        }
    }

    /** @param {number} quantity */
    return function save(quantity) {
        started++;
        const task = queue.then(() => send(quantity));
        queue = task;
        pending.add(task);
        pendingListener(pending.size);
        task.finally(() => {
            pending.delete(task);
            pendingListener(pending.size);
        });
    };
}
