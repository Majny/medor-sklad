// @ts-check
// Tabulka skladu: řádky, řazení podle sloupců, oddělení skupin klientů, odznaky stavu zásob,
// souhrnný řádek a stavy načítání, chyby a prázdného výsledku.

import { button, element } from "./dom.js";
import { createQuantityEditor } from "./editing.js";
import { collator, formatNumber, plural } from "./format.js";

/**
 * @typedef {import("./api.js").StockRow} StockRow
 * @typedef {import("./url-state.js").Sort} Sort
 * @typedef {import("./url-state.js").SortKey} SortKey
 */

const LOW_STOCK = 10;

/** @type {Record<SortKey, (a: StockRow, b: StockRow) => number>} */
const COMPARERS = {
    client: (a, b) => collator.compare(a.clientName, b.clientName),
    categories: (a, b) => collator.compare(a.categories.join(", "), b.categories.join(", ")),
    product: (a, b) => collator.compare(a.productName, b.productName),
    quantity: (a, b) => a.quantity - b.quantity,
};

/**
 * Stabilní řazení: při shodě zůstává pořadí ze serveru (klient, potom produkt).
 * @param {StockRow[]} rows
 * @param {Sort} sort
 */
export function sortRows(rows, sort) {
    const sign = sort.dir === "asc" ? 1 : -1;
    return [...rows].sort((a, b) => sign * COMPARERS[sort.key](a, b));
}

/**
 * @param {HTMLTableElement} table
 * @param {HTMLElement} summary
 * @param {{ onSort: (key: SortKey) => void, onRetry: () => void, onReset: () => void, canReset: () => boolean,
 *           announce: (message: string, isError: boolean) => void }} handlers
 */
export function createGrid(table, summary, handlers) {
    const body = table.tBodies[0];
    /** @type {StockRow[]} */
    let rows = [];
    /** @type {Map<StockRow, HTMLTableRowElement>} */
    let rowElements = new Map();
    /** @type {Sort} */
    let sort = { key: "client", dir: "asc" };
    let hasData = false;

    for (const header of table.querySelectorAll("thead button[data-sort]")) {
        header.addEventListener("click", () => handlers.onSort(/** @type {SortKey} */ (/** @type {HTMLElement} */ (header).dataset.sort)));
    }

    function showLoading() {
        table.setAttribute("aria-busy", "true");
        if (!hasData) body.replaceChildren(messageRow("Načítám…"));
    }

    /** @param {string} message */
    function showError(message) {
        hasData = false;
        table.removeAttribute("aria-busy");
        summary.textContent = "";
        const text = element("p", "", `Data se nepodařilo načíst. ${message}`);
        text.setAttribute("role", "alert");
        body.replaceChildren(messageRow(text, button("Zkusit znovu", handlers.onRetry, "secondary")));
    }

    /** @param {StockRow[]} newRows v pořadí ze serveru */
    function setRows(newRows) {
        rows = newRows;
        const editorHandlers = { onRowChanged: refreshRow, announce: handlers.announce };
        rowElements = new Map(rows.map(row => [row, buildRow(row, editorHandlers)]));
        hasData = rows.length > 0;
        table.removeAttribute("aria-busy");
        if (hasData) applySort();
        else if (handlers.canReset()) body.replaceChildren(messageRow("Filtru neodpovídá žádná položka.", button("Zrušit filtr", handlers.onReset, "secondary")));
        else body.replaceChildren(messageRow("Sklad je prázdný."));
        renderSummary();
    }

    /** Přeřadí existující řádky (nestaví je znovu), takže rozpracované stavy editorů zůstanou. */
    function setSort(/** @type {Sort} */ newSort) {
        sort = newSort;
        for (const th of table.querySelectorAll("thead th[data-sort]")) {
            const active = /** @type {HTMLElement} */ (th).dataset.sort === sort.key;
            if (active) th.setAttribute("aria-sort", sort.dir === "asc" ? "ascending" : "descending");
            else th.removeAttribute("aria-sort");
        }
        if (hasData) applySort();
    }

    function applySort() {
        const grouped = sort.key === "client"; // jméno klienta jen na prvním řádku jeho skupiny
        const sorted = sortRows(rows, sort);
        sorted.forEach((row, i) => {
            const tr = /** @type {HTMLTableRowElement} */ (rowElements.get(row));
            const sameClient = i > 0 && sorted[i - 1].clientId === row.clientId;
            tr.classList.toggle("group-start", grouped && i > 0 && !sameClient);
            tr.classList.toggle("same-client", grouped && sameClient);
        });
        body.replaceChildren(...sorted.map(row => /** @type {HTMLTableRowElement} */ (rowElements.get(row))));
    }

    /** Po uložení: přepočítá odznak řádku a souhrn. Řádek se záměrně nepřeřadí, aby neuskočil pod kurzorem. */
    function refreshRow(/** @type {StockRow} */ row) {
        const tr = rowElements.get(row);
        if (tr) updateLevel(tr, row);
        renderSummary();
    }

    function renderSummary() {
        const clients = new Set(rows.map(row => row.clientId)).size;
        const total = rows.reduce((sum, row) => sum + row.quantity, 0);
        const soldOut = rows.filter(row => row.quantity === 0).length;
        summary.textContent = `${plural(rows.length, ["řádek", "řádky", "řádků"])}, ${plural(clients, ["klient", "klienti", "klientů"])}, celkem ${formatNumber(total)} ks`
            + (soldOut > 0 ? `, vyprodáno ${formatNumber(soldOut)}` : "");
    }

    return { showLoading, showError, setRows, setSort, refreshRow };
}

/**
 * @param {StockRow} row
 * @param {import("./editing.js").EditorHandlers} editorHandlers
 */
function buildRow(row, editorHandlers) {
    const tr = document.createElement("tr");
    const client = element("td", "client");
    client.append(element("span", "client-name", row.clientName));
    const categories = element("td", "categories");
    categories.append(...row.categories.map(name => element("span", "chip", name)));
    const product = element("td", "product");
    product.append(element("span", "", row.productName), element("span", "badge-zero", "Vyprodáno"));
    const quantity = element("td", "num");
    quantity.append(createQuantityEditor(row, editorHandlers));
    tr.append(client, categories, product, quantity);
    updateLevel(tr, row);
    return tr;
}

/** Nula = červený odznak "Vyprodáno", méně než 10 kusů = jantarový podklad. */
function updateLevel(/** @type {HTMLTableRowElement} */ tr, /** @type {StockRow} */ row) {
    tr.classList.toggle("is-zero", row.quantity === 0);
    tr.classList.toggle("is-low", row.quantity > 0 && row.quantity < LOW_STOCK);
}

/** @param {(string | Node)[]} content */
function messageRow(...content) {
    const tr = document.createElement("tr");
    const td = /** @type {HTMLTableCellElement} */ (element("td", "state-cell"));
    td.colSpan = 4;
    td.append(...content);
    tr.append(td);
    return tr;
}
