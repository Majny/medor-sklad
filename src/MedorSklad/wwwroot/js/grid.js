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
    // Nápověda ke klávesnici: viditelná v souhrnu a zároveň popis (aria-describedby) každého pole.
    const keyboardHint = element("span", "summary-hint", "Enter uloží a přejde dál, Esc vrátí hodnotu");
    keyboardHint.id = "kbd-hint";

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
        rows = [];
        renderSummary();
        const title = element("p", "state-title", "Data se nepodařilo načíst.");
        title.setAttribute("role", "alert");
        body.replaceChildren(messageRow(title, element("p", "", message), button("Zkusit znovu", handlers.onRetry, "secondary")));
    }

    /** @param {StockRow[]} newRows v pořadí ze serveru */
    function setRows(newRows) {
        rows = newRows;
        const editorHandlers = { onRowChanged: refreshRow, announce: handlers.announce };
        rowElements = new Map(rows.map(row => [row, buildRow(row, editorHandlers)]));
        hasData = rows.length > 0;
        table.removeAttribute("aria-busy");
        if (hasData) applySort();
        else if (handlers.canReset()) {
            body.replaceChildren(messageRow(
                element("p", "state-title", "Filtru neodpovídá žádná položka."),
                element("p", "", "Zkuste zrušit část výběru."),
                button("Zrušit filtr", handlers.onReset, "secondary")));
        } else body.replaceChildren(messageRow(element("p", "state-title", "Sklad je prázdný.")));
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

    /** Souhrn jako dvojice číslo + popis; "vyprodáno" a "dochází" slouží i jako legenda odznaků. */
    function renderSummary() {
        summary.hidden = rows.length === 0;
        if (rows.length === 0) { summary.replaceChildren(); return; }
        const clients = new Set(rows.map(row => row.clientId)).size;
        const total = rows.reduce((sum, row) => sum + row.quantity, 0);
        const soldOut = rows.filter(row => row.quantity === 0).length;
        const low = rows.filter(row => isLow(row.quantity)).length;
        const stats = [
            stat("stat", ...countWithNoun(rows.length, ["položka", "položky", "položek"])),
            stat("stat", ...countWithNoun(clients, ["klient", "klienti", "klientů"])),
        ];
        if (total > 0) stats.push(stat("stat", "celkem ", bold(total), " ks"));
        if (soldOut > 0) stats.push(stat("stat stat-zero", "vyprodáno ", bold(soldOut)));
        if (low > 0) stats.push(stat("stat stat-low", "dochází ", bold(low)));
        summary.replaceChildren(...stats, keyboardHint);
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
    categories.textContent = row.categories.join(", ");
    const product = element("td", "product");
    product.append(element("span", "", row.productName), element("span", "badge-zero", "Vyprodáno"), element("span", "badge-low", "Dochází"),
        element("span", "product-cats", row.categories.join(", "))); // v úzkém okně místo sloupce Kategorie
    const quantity = element("td", "num");
    quantity.append(createQuantityEditor(row, editorHandlers));
    tr.append(client, categories, product, quantity);
    updateLevel(tr, row);
    return tr;
}

const isLow = (/** @type {number} */ quantity) => quantity > 0 && quantity < LOW_STOCK;

/** Nula = červený odznak "Vyprodáno", méně než 10 kusů = jantarový odznak "Dochází". */
function updateLevel(/** @type {HTMLTableRowElement} */ tr, /** @type {StockRow} */ row) {
    tr.classList.toggle("is-zero", row.quantity === 0);
    tr.classList.toggle("is-low", isLow(row.quantity));
}

/** @param {number} value */
function bold(value) {
    return element("b", "", formatNumber(value));
}

/**
 * Číslo tučně a za ním správný český tvar, například [<b>120</b>, " položek"].
 * @param {number} count
 * @param {[string, string, string]} forms
 * @returns {[HTMLElement, string]}
 */
function countWithNoun(count, forms) {
    const noun = plural(count, forms).slice(formatNumber(count).length);
    return [bold(count), noun];
}

/**
 * @param {string} className
 * @param {(string | Node)[]} content
 */
function stat(className, ...content) {
    const span = element("span", className);
    span.append(...content);
    return span;
}

/** Řádek přes celou tabulku pro načítání, chybu a prázdný výsledek. @param {(string | Node)[]} content */
function messageRow(...content) {
    const tr = document.createElement("tr");
    const td = /** @type {HTMLTableCellElement} */ (element("td", "state-cell"));
    td.colSpan = 4;
    const state = element("div", "state");
    state.append(...content);
    td.append(state);
    tr.append(td);
    return tr;
}
