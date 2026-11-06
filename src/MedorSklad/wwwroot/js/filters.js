// @ts-check
// Postranní filtr: tři skupiny zaškrtávátek s hledáním a kaskádou, přepínač nulových stavů a zrušení filtru.

import { element } from "./dom.js";
import { createGroup } from "./filter-group.js";

/**
 * @typedef {import("./api.js").FilterOptions} FilterOptions
 * @typedef {import("./url-state.js").AppState} AppState
 * @typedef {{ categoryIds: Set<number> | null, productIds: Set<number> | null }} Scope null = bez omezení
 */

/** @type {import("./filter-group.js").GroupDef[]} */
const GROUPS = [
    { key: "clientIds", title: "Klienti", source: "clients", searchLabel: "Hledat klienta" },
    { key: "categoryIds", title: "Kategorie", source: "categories", searchLabel: "Hledat kategorii" },
    { key: "productIds", title: "Produkty", source: "products", searchLabel: "Hledat produkt" },
];

/**
 * Kaskáda filtru (čistá funkce). Vybraní klienti zúží kategorie na sjednocení svých kategorií,
 * efektivní kategorie (vybrané, jinak celý rozsah) pak zúží produkty.
 * @param {FilterOptions} options
 * @param {AppState} state
 * @returns {Scope}
 */
export function computeScope(options, state) {
    /** @type {Set<number> | null} */
    let categoryIds = null;
    if (state.clientIds.length > 0) {
        const clients = new Set(state.clientIds);
        categoryIds = new Set(options.clients.filter(c => clients.has(c.id)).flatMap(c => c.categoryIds));
    }
    const scope = categoryIds;
    const effective = state.categoryIds.length > 0
        ? new Set(state.categoryIds.filter(id => scope === null || scope.has(id)))
        : scope;
    const productIds = effective === null
        ? null
        : new Set(options.products.filter(p => p.categoryIds.some(id => effective.has(id))).map(p => p.id));
    return { categoryIds, productIds };
}

/** @param {AppState} state */
export function isFilterActive(state) {
    return state.clientIds.length + state.categoryIds.length + state.productIds.length > 0 || state.onlyOutOfStock;
}

/**
 * DOM se postaví jednou a update() jen přepíná viditelnost, takže fokus a posun seznamů zůstávají na místě.
 * @param {HTMLElement} sidebar
 * @param {FilterOptions} options
 * @param {AppState} state sdílený stav aplikace, modul ho přímo upravuje
 * @param {() => void} onChange
 */
export function createFilters(sidebar, options, state, onChange) {
    const onlyZero = /** @type {HTMLInputElement} */ (sidebar.querySelector("#only-zero"));
    const resetButton = /** @type {HTMLButtonElement} */ (sidebar.querySelector("#reset-filter"));
    const activeCount = /** @type {HTMLElement} */ (sidebar.querySelector("#filter-active-count"));
    const changed = () => { update(); onChange(); };

    // Neznámá id (např. ze starého odkazu) zahodíme, jinak by tiše vyprázdnila grid.
    for (const { key, source } of GROUPS) {
        const known = new Set(options[source].map(item => item.id));
        state[key] = state[key].filter(id => known.has(id));
    }

    const groups = GROUPS.map(def => createGroup(def, options[def.source], state, changed));
    /** @type {HTMLElement} */ (sidebar.querySelector("#filter-groups")).replaceChildren(...groups.map(g => g.element));

    onlyZero.disabled = false;
    onlyZero.addEventListener("change", () => { state.onlyOutOfStock = onlyZero.checked; changed(); });
    resetButton.addEventListener("click", reset);

    function update() {
        const scope = computeScope(options, state);
        groups.forEach(group => group.update(group.key === "clientIds" ? null : scope[group.key]));
        onlyZero.checked = state.onlyOutOfStock;
        resetButton.hidden = !isFilterActive(state);
        // Počet aktivních omezení v sbaleném filtru na úzkém okně.
        const active = state.clientIds.length + state.categoryIds.length + state.productIds.length + (state.onlyOutOfStock ? 1 : 0);
        activeCount.replaceChildren();
        if (active > 0) {
            const pill = element("span", "pill", String(active));
            pill.append(element("span", "sr-only", " aktivní"));
            activeCount.append(pill);
        }
    }

    function reset() {
        GROUPS.forEach(({ key }) => (state[key] = []));
        state.onlyOutOfStock = false;
        groups.forEach(group => group.clearSearch());
        changed();
    }

    update();
    return { update, reset };
}
