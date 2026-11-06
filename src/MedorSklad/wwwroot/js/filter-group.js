// @ts-check
// Jedna skupina filtru (Klienti, Kategorie nebo Produkty): hledání, počítadlo, "Vybrat vše" a položky mimo rozsah kaskády.

import { element } from "./dom.js";
import { normalizeText } from "./format.js";

/**
 * @typedef {import("./api.js").FilterOptions} FilterOptions
 * @typedef {import("./api.js").LookupItem} LookupItem
 * @typedef {import("./url-state.js").AppState} AppState
 * @typedef {"clientIds" | "categoryIds" | "productIds"} FilterKey
 * @typedef {{ key: FilterKey, title: string, source: keyof FilterOptions, searchLabel: string }} GroupDef
 */

/**
 * Jedna skupina zaškrtávátek ze šablony #filter-group-template.
 * @param {GroupDef} def
 * @param {LookupItem[]} items
 * @param {AppState} state
 * @param {() => void} changed
 */
export function createGroup(def, items, state, changed) {
    const template = /** @type {HTMLTemplateElement} */ (document.getElementById("filter-group-template"));
    const root = /** @type {HTMLElement} */ (template.content.firstElementChild?.cloneNode(true));
    const find = (/** @type {string} */ selector) => /** @type {HTMLElement} */ (root.querySelector(selector));
    const search = /** @type {HTMLInputElement} */ (find(".group-search"));
    const toggleAll = /** @type {HTMLButtonElement} */ (find(".toggle-all"));
    const others = /** @type {HTMLButtonElement} */ (find(".show-others"));
    const list = find(".options");
    const noMatch = find(".no-match");

    find(".group-title").textContent = def.title;
    search.setAttribute("aria-label", def.searchLabel);
    search.placeholder = `${def.searchLabel}…`;
    list.id = `options-${def.key}`;
    others.setAttribute("aria-controls", list.id);

    const rows = items.map(item => {
        const label = element("label", "option");
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.addEventListener("change", () => {
            state[def.key] = rows.filter(r => r.checkbox.checked).map(r => r.item.id);
            changed();
        });
        label.append(checkbox, element("span", "", item.name), element("span", "tag", "mimo výběr"));
        list.append(label);
        return { item, label, checkbox, text: normalizeText(item.name) };
    });

    /** @type {Set<number> | null} */
    let scope = null;
    let showOthers = false;
    const visibleRows = () => rows.filter(r => !r.label.hidden);

    search.addEventListener("input", () => update(scope));
    others.addEventListener("click", () => { showOthers = !showOthers; update(scope); });
    toggleAll.addEventListener("click", () => {
        const visible = visibleRows();
        const select = !visible.every(r => r.checkbox.checked);
        visible.forEach(r => (r.checkbox.checked = select));
        state[def.key] = rows.filter(r => r.checkbox.checked).map(r => r.item.id);
        changed();
    });

    /** @param {Set<number> | null} newScope */
    function update(newScope) {
        scope = newScope;
        const needle = normalizeText(search.value.trim());
        const selected = new Set(state[def.key]);
        let outside = 0;
        for (const row of rows) {
            const isSelected = selected.has(row.item.id);
            const inScope = scope === null || scope.has(row.item.id);
            const matches = row.text.includes(needle);
            if (!inScope && !isSelected && matches) outside++;
            row.checkbox.checked = isSelected;
            // Vybraná položka mimo rozsah se nikdy tiše neodebere: zůstane vidět s popiskem "mimo výběr".
            row.label.classList.toggle("out-of-scope", !inScope);
            row.label.hidden = !isSelected && !(matches && (inScope || showOthers));
        }
        const visible = visibleRows();
        const count = find(".count");
        count.hidden = selected.size === 0;
        count.replaceChildren(String(selected.size), element("span", "sr-only", " vybráno"));
        toggleAll.textContent = visible.length > 0 && visible.every(r => r.checkbox.checked) ? "Zrušit výběr" : "Vybrat vše";
        toggleAll.disabled = visible.length === 0;
        noMatch.hidden = visible.length > 0;
        others.hidden = outside === 0;
        others.textContent = showOthers ? `Skrýt ostatní (${outside})` : `Zobrazit ostatní (${outside})`;
        others.setAttribute("aria-expanded", String(showOthers));
    }

    return { key: def.key, element: root, update, clearSearch: () => { search.value = ""; showOthers = false; } };
}
