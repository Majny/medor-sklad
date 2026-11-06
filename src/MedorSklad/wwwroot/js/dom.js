// @ts-check
// Malé pomocníky pro tvorbu DOM prvků, sdílené gridem, filtrem a editorem.

/**
 * @param {string} tag
 * @param {string} [className]
 * @param {string} [text]
 */
export function element(tag, className, text) {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text !== undefined) el.textContent = text;
    return el;
}

/**
 * @param {string} text
 * @param {() => void} onClick
 * @param {string} [className]
 */
export function button(text, onClick, className) {
    const el = /** @type {HTMLButtonElement} */ (element("button", className, text));
    el.type = "button";
    el.addEventListener("click", onClick);
    return el;
}
