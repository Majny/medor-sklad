// @ts-check
// Formátování čísel, české řazení a drobné pomocné funkce sdílené ostatními moduly.

const numberFormat = new Intl.NumberFormat("cs-CZ");

/** Porovnávání textů podle české abecedy (Č za C, Ch za H). */
export const collator = new Intl.Collator("cs");

/**
 * Číslo s českým oddělovačem tisíců, například 14 230.
 * @param {number} value
 */
export function formatNumber(value) {
    return numberFormat.format(value);
}

/**
 * Počet se správným českým tvarem: 1 řádek, 2 řádky, 5 řádků.
 * @param {number} count
 * @param {[string, string, string]} forms tvar pro 1, pro 2 až 4 a pro ostatní
 */
export function plural(count, [one, few, many]) {
    const form = count === 1 ? one : count >= 2 && count <= 4 ? few : many;
    return `${formatNumber(count)} ${form}`;
}

/**
 * Text pro hledání bez ohledu na velikost písmen a diakritiku ("Čaj" najde i "caj").
 * @param {string} text
 */
export function normalizeText(text) {
    return text.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

/**
 * Odloží volání, dokud se po dobu `ms` nic nezmění.
 * @template {unknown[]} A
 * @param {(...args: A) => void} fn
 * @param {number} ms
 * @returns {(...args: A) => void}
 */
export function debounce(fn, ms) {
    /** @type {number | undefined} */
    let timer;
    return (...args) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), ms);
    };
}
