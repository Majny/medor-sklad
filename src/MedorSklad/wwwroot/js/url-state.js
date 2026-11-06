// @ts-check
// Stav filtru a řazení v adrese stránky, aby obnovení, tlačítko Zpět i sdílený odkaz ukázaly totéž.
// Filtr má stejný formát jako dotaz na API (opakované klíče), navíc je tu řazení:
// ?clientIds=1&clientIds=3&categoryIds=2&onlyOutOfStock=true&sort=quantity:desc

/**
 * @typedef {"client" | "categories" | "product" | "quantity"} SortKey
 * @typedef {{ key: SortKey, dir: "asc" | "desc" }} Sort
 * @typedef {import("./api.js").StockFilter & { sort: Sort }} AppState
 */

/** Výchozí řazení odpovídá pořadí ze serveru: klient, potom produkt. */
export const DEFAULT_SORT = /** @type {Sort} */ ({ key: "client", dir: "asc" });

/** @type {SortKey[]} */
const SORT_KEYS = ["client", "categories", "product", "quantity"];

/**
 * @param {string} [search]
 * @returns {AppState}
 */
export function readState(search = location.search) {
    const params = new URLSearchParams(search);
    /** @param {string} key */
    const ids = key => [...new Set(params.getAll(key).map(Number).filter(id => Number.isInteger(id) && id > 0))];
    return {
        clientIds: ids("clientIds"),
        categoryIds: ids("categoryIds"),
        productIds: ids("productIds"),
        onlyOutOfStock: params.get("onlyOutOfStock") === "true",
        sort: parseSort(params.get("sort")),
    };
}

/**
 * Přepíše adresu bez nového záznamu v historii (každé zaškrtnutí by jinak bylo jedno Zpět).
 * @param {AppState} state
 */
export function writeState(state) {
    const params = new URLSearchParams();
    for (const key of /** @type {const} */ (["clientIds", "categoryIds", "productIds"])) {
        state[key].forEach(id => params.append(key, String(id)));
    }
    if (state.onlyOutOfStock) params.set("onlyOutOfStock", "true");
    if (state.sort.key !== DEFAULT_SORT.key || state.sort.dir !== DEFAULT_SORT.dir) {
        params.set("sort", `${state.sort.key}:${state.sort.dir}`);
    }
    const query = params.toString().replaceAll("%3A", ":"); // čitelnější sort=quantity:desc
    history.replaceState(null, "", query ? `?${query}` : location.pathname);
}

/** @param {string | null} value */
function parseSort(value) {
    const [key, dir] = (value ?? "").split(":");
    const sortKey = SORT_KEYS.find(k => k === key);
    if (!sortKey || (dir !== "asc" && dir !== "desc")) return DEFAULT_SORT;
    return /** @type {Sort} */ ({ key: sortKey, dir });
}
