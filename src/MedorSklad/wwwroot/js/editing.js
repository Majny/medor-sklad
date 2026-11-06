// @ts-check
// Editor počtu kusů v buňce gridu: ovládání z klávesnice jako v tabulkovém procesoru,
// jediné hlídané uložení commit(), stavy řádku a panel pro konflikt souběžné úpravy (409).

import { button, element } from "./dom.js";
import { formatNumber } from "./format.js";
import { createRowSaver, pendingCount } from "./row-saver.js";

/** @typedef {import("./api.js").StockRow} StockRow */
/** @typedef {"dirty" | "invalid" | "saving" | "saved" | "error" | "conflict"} EditorState */

const MAX_QUANTITY = 1_000_000;
const INVALID_HINT = "Zadejte celé číslo 0 až 1 000 000";
const SAVED_MARK_MS = 1500;
const STATES = ["dirty", "invalid", "saving", "saved", "error", "conflict"];

/**
 * @typedef {{
 *   onRowChanged: (row: StockRow) => void,
 *   announce: (message: string, isError: boolean) => void,
 * }} EditorHandlers
 */

/** Pro beforeunload: běžící uložení, rozepsaná nebo neplatná hodnota, nevyřešený konflikt. */
export function hasUnsavedWork() {
    return pendingCount() > 0 || document.querySelector(".qty-editor:is(.dirty, .invalid, .conflict)") !== null;
}

/** @param {string} text */
function parseQuantity(text) {
    return /^\d+$/.test(text) && Number(text) <= MAX_QUANTITY ? Number(text) : null;
}

/**
 * @param {StockRow} row
 * @param {EditorHandlers} handlers
 */
export function createQuantityEditor(row, handlers) {
    const label = `${row.clientName}, ${row.productName}`;
    const input = /** @type {HTMLInputElement} */ (element("input", "qty"));
    Object.assign(input, { type: "text", inputMode: "numeric", autocomplete: "off", value: String(row.quantity) });
    input.setAttribute("aria-label", `Počet kusů, ${label}`);
    const mark = element("span", "qty-mark");
    mark.setAttribute("aria-hidden", "true");
    const message = element("div", "qty-message");
    message.id = `qty-message-${row.clientId}-${row.productId}`;
    input.setAttribute("aria-describedby", message.id);
    const controls = element("div", "qty-controls");
    controls.append(stepButton("−", "Snížit o 1", -1), input, stepButton("+", "Zvýšit o 1", 1), mark);
    const root = element("div", "qty-editor");
    root.append(controls, message);

    let target = row.quantity; // hodnota, kterou má uživatel uloženou nebo ve frontě k uložení
    let savedTimer = 0;
    /** Otevřený panel konfliktu: hodnota, kterou server odmítl, a aktuální stav na serveru. */
    /** @type {{ mine: number, current: number, currentVersion: number } | null} */
    let conflict = null;
    const save = createRowSaver(row, { onSaved, onConflict, onError });

    /** @param {EditorState | null} state */
    function setState(state) {
        root.classList.remove(...STATES);
        if (state) root.classList.add(state);
        input.setAttribute("aria-invalid", String(state === "invalid" || state === "error"));
        mark.textContent = state === "saved" ? "✓" : "";
        clearTimeout(savedTimer);
        if (state === "saved") savedTimer = window.setTimeout(() => setState(null), SAVED_MARK_MS);
    }
    const is = (/** @type {EditorState} */ state) => root.classList.contains(state);
    const showMessage = (/** @type {(string | Node)[]} */ ...content) => message.replaceChildren(...content);

    /** Zruší rozepsaný nebo chybový stav; běžící uložení a konflikt nechá být. */
    function clearEditState() {
        if (is("dirty") || is("invalid") || is("error")) { setState(null); showMessage(); }
    }

    /** Jediné místo, odkud se ukládá (Enter, blur, tlačítka). Stejná hodnota se nepošle dvakrát. */
    function commit() {
        const quantity = parseQuantity(input.value.trim());
        if (quantity === null) { setState("invalid"); showMessage(INVALID_HINT); return false; }
        input.value = String(quantity); // "007" zobrazíme jako 7
        if (conflict) {
            // Nová hodnota během konfliktu je vědomé přepsání; odmítnutá hodnota (např. blur) panel nechá.
            if (quantity !== conflict.mine) resolveConflict(quantity);
            return true;
        }
        if (quantity === target) { clearEditState(); return true; }
        enqueue(quantity);
        return true;
    }

    function enqueue(/** @type {number} */ quantity) {
        target = quantity;
        showMessage();
        setState("saving");
        save(quantity);
    }

    /** 200: server vrátil nový stav, grid jen přepočítá součty a odznaky (bez znovunačtení). */
    function onSaved(/** @type {number} */ quantity) {
        handlers.onRowChanged(row);
        if (quantity !== target) return; // ve frontě je novější hodnota
        if (parseQuantity(input.value.trim()) !== quantity) return; // uživatel mezitím píše dál, stav zůstane rozepsaný
        setState("saved");
        handlers.announce(`Uloženo: ${label}, ${formatNumber(quantity)} ks`, false);
    }

    /** 400, 404, 422 nebo nedostupný server: vrátíme poslední potvrzenou hodnotu a ukážeme důvod. */
    function onError(/** @type {number} */ quantity, /** @type {string} */ reason) {
        if (quantity === target) {
            target = row.quantity;
            if (parseQuantity(input.value.trim()) === quantity) input.value = String(row.quantity);
        }
        setState("error");
        showMessage(reason);
        handlers.announce(`Neuloženo (${label}): ${reason}`, true);
    }

    /**
     * 409: někdo jiný mezitím řádek změnil. Uživatel rozhodne tlačítkem, Esc (= použít aktuální)
     * nebo uložením jiné hodnoty (= přepsat). Uložení ve frontě se starou verzí dostanou 409 také
     * a panel jen aktualizují, takže nic nepřepíše cizí změnu bez rozhodnutí uživatele.
     */
    function onConflict(/** @type {number} */ mine, /** @type {number} */ current, /** @type {number} */ currentVersion) {
        conflict = { mine, current, currentVersion };
        const choose = (/** @type {number} */ quantity) => () => { resolveConflict(quantity); input.focus(); };
        const panel = element("div", "conflict-panel");
        const actions = element("div", "conflict-actions");
        actions.append(
            button(`Použít ${formatNumber(current)}`, choose(current)),
            button(`Přepsat na ${formatNumber(mine)}`, choose(mine)));
        panel.append(element("p", "", `Mezitím změněno na ${formatNumber(current)} ks. Vaše hodnota ${formatNumber(mine)} nebyla uložena.`), actions);
        setState("conflict");
        showMessage(panel);
        handlers.announce(`Konflikt u položky ${label}: mezitím změněno na ${formatNumber(current)} ks.`, true);
    }

    /** Převezme aktuální stav ze serveru; jiná hodnota než aktuální se pošle s aktuální verzí. */
    function resolveConflict(/** @type {number} */ quantity) {
        if (!conflict) return;
        row.quantity = target = conflict.current;
        row.version = conflict.currentVersion;
        conflict = null;
        handlers.onRowChanged(row);
        input.value = String(quantity);
        setState(null);
        showMessage();
        if (quantity !== row.quantity) enqueue(quantity);
    }

    input.addEventListener("focus", () => input.select());
    input.addEventListener("blur", () => commit());
    input.addEventListener("input", () => {
        if (conflict) return; // panel zůstává až do rozhodnutí (viz onConflict)
        const quantity = parseQuantity(input.value.trim());
        showMessage();
        setState(quantity === null ? "invalid" : quantity !== target ? "dirty" : null);
    });
    input.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            if (commit()) moveFocus(input, event.shiftKey ? -1 : 1); // následný blur už nic neuloží
        } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            moveFocus(input, event.key === "ArrowDown" ? 1 : -1); // uloží se při blur
        } else if (event.key === "Escape") {
            if (conflict) resolveConflict(conflict.current); // poslední potvrzená hodnota je ta ze serveru
            input.value = String(target);
            clearEditState();
            input.select();
        }
    });

    /** Tlačítka −/+ nejsou v pořadí Tabu, nekradou fokus a ukládají hned. */
    function stepButton(/** @type {string} */ text, /** @type {string} */ action, /** @type {number} */ delta) {
        const step = button(text, () => {
            const base = parseQuantity(input.value.trim()) ?? target;
            input.value = String(Math.min(MAX_QUANTITY, Math.max(0, base + delta)));
            commit();
        }, "qty-step");
        step.tabIndex = -1;
        step.setAttribute("aria-label", `${action}, ${label}`);
        step.addEventListener("mousedown", event => event.preventDefault());
        return step;
    }

    return root;
}

/** Přesune fokus na vstup o `delta` řádků níž (záporné číslo = výš). */
function moveFocus(/** @type {HTMLInputElement} */ input, /** @type {number} */ delta) {
    const inputs = [...(input.closest("tbody")?.querySelectorAll("input.qty") ?? [])];
    const next = /** @type {HTMLInputElement | undefined} */ (inputs[inputs.indexOf(input) + delta]);
    if (next) next.focus(); else input.select();
}
