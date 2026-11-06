// @ts-check
// Volání REST API. Všechny chyby převádí na ApiError s českou hláškou z ProblemDetails.

/**
 * @typedef {{ id: number, name: string }} LookupItem
 * @typedef {{ id: number, name: string, categoryIds: number[] }} CategorizedItem
 * @typedef {{ clients: CategorizedItem[], categories: LookupItem[], products: CategorizedItem[] }} FilterOptions
 * @typedef {{ clientId: number, clientName: string, productId: number, productName: string,
 *             categories: string[], quantity: number, version: number }} StockRow
 * @typedef {{ clientIds: number[], categoryIds: number[], productIds: number[], onlyOutOfStock: boolean }} StockFilter
 */

/** Chyba z API: HTTP status (0 = server nedostupný), česká hláška a rozšíření ProblemDetails. */
export class ApiError extends Error {
    /**
     * @param {number} status
     * @param {string} message
     * @param {Record<string, unknown>} [extensions]
     */
    constructor(status, message, extensions = {}) {
        super(message);
        this.name = "ApiError";
        this.status = status;
        this.extensions = extensions;
    }
}

/** Standardní pole ProblemDetails; všechno ostatní považujeme za rozšíření (např. currentQuantity). */
const PROBLEM_FIELDS = new Set(["type", "title", "status", "detail", "instance", "errors", "traceId"]);

/** @returns {Promise<FilterOptions>} */
export function getFilters() {
    return request("/api/filters");
}

/**
 * @param {StockFilter} filter
 * @param {AbortSignal} [signal] umožní zrušit starší dotaz, když uživatel mezitím změní filtr
 * @returns {Promise<StockRow[]>}
 */
export function getStock(filter, signal) {
    const params = new URLSearchParams();
    for (const key of /** @type {const} */ (["clientIds", "categoryIds", "productIds"])) {
        filter[key].forEach(id => params.append(key, String(id)));
    }
    if (filter.onlyOutOfStock) params.set("onlyOutOfStock", "true");
    return request(`/api/stock?${params}`, { signal });
}

/**
 * @param {number} clientId
 * @param {number} productId
 * @param {{ quantity: number, version: number }} body verze řádku, kterou uživatel naposledy viděl
 * @returns {Promise<StockRow>}
 */
export function putQuantity(clientId, productId, body) {
    return request(`/api/stock/${clientId}/${productId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
}

/**
 * @param {string} url
 * @param {RequestInit} [options]
 */
async function request(url, options = {}) {
    let response;
    try {
        response = await fetch(url, { ...options, headers: { Accept: "application/json", ...options.headers } });
    } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") throw error;
        throw new ApiError(0, "Server není dostupný, zkuste to prosím znovu.");
    }
    if (!response.ok) throw await toApiError(response);
    return response.json();
}

/**
 * Česká hláška z ProblemDetails: detail, případně první validační chyba, jinak obecný text podle statusu.
 * @param {Response} response
 */
async function toApiError(response) {
    /** @type {Record<string, any>} */
    let problem = {};
    try {
        problem = await response.json();
    } catch {
        // tělo není JSON, použijeme obecnou hlášku níže
    }
    const firstError = Object.values(problem.errors ?? {}).flat()[0];
    const message = problem.detail ?? firstError ?? fallbackMessage(response.status);
    const extensions = Object.fromEntries(Object.entries(problem).filter(([key]) => !PROBLEM_FIELDS.has(key)));
    return new ApiError(response.status, String(message), extensions);
}

/** @param {number} status */
function fallbackMessage(status) {
    if (status === 404) return "Záznam neexistuje.";
    if (status >= 500) return "Chyba serveru, zkuste to prosím znovu.";
    return `Požadavek se nepodařil (${status}).`;
}
