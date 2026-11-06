// @ts-check
// Propojení modulů: načte možnosti filtru, obnoví stav z adresy a načte grid.

import { getFilters, getStock } from "./api.js";
import { button, element } from "./dom.js";
import { hasUnsavedWork } from "./editing.js";
import { createFilters, isFilterActive } from "./filters.js";
import { debounce, plural } from "./format.js";
import { createGrid } from "./grid.js";
import { onPendingChange, savesStarted, whenSaved } from "./row-saver.js";
import { readState, writeState } from "./url-state.js";

const byId = (/** @type {string} */ id) => /** @type {HTMLElement} */ (document.getElementById(id));

const state = readState();
const saveStatus = byId("save-status");

/** @type {{ update: () => void, reset: () => void } | null} */
let filters = null;
/** @type {AbortController | null} */
let loading = null;

const grid = createGrid(/** @type {HTMLTableElement} */ (byId("grid")), byId("summary"), {
    onSort: key => {
        const dir = state.sort.key === key && state.sort.dir === "asc" ? "desc" : "asc";
        state.sort = { key, dir };
        writeState(state);
        grid.setSort(state.sort);
    },
    onRetry: () => loadGrid(),
    onReset: () => resetFilter(),
    canReset: () => isFilterActive(state),
    announce,
});

onPendingChange(count => {
    saveStatus.textContent = count === 0 ? "Vše uloženo" : `Ukládám ${plural(count, ["změnu", "změny", "změn"])}`;
    saveStatus.classList.toggle("busy", count > 0);
});

/** Každá změna filtru: hned adresa a filtr, grid až po 250 ms klidu. */
function onFilterChange() {
    writeState(state);
    debouncedLoad();
}
const debouncedLoad = debounce(() => loadGrid(), 250);

function resetFilter() {
    if (filters) {
        filters.reset();
    } else {
        Object.assign(state, { clientIds: [], categoryIds: [], productIds: [], onlyOutOfStock: false });
        onFilterChange();
    }
}

/**
 * Starší rozběhnutý dotaz zrušíme a před načtením počkáme na rozpracovaná uložení. Pokud během
 * dotazu začalo nové uložení (řádky jsou editovatelné i při načítání), mohou být data starší
 * než to uložení, proto je načteme znovu.
 */
async function loadGrid() {
    loading?.abort();
    const controller = (loading = new AbortController());
    grid.showLoading();
    try {
        for (;;) {
            await whenSaved();
            if (controller.signal.aborted) return;
            const savesBefore = savesStarted();
            const rows = await getStock(state, controller.signal);
            if (savesStarted() === savesBefore) { grid.setRows(rows); return; }
        }
    } catch (error) {
        if (controller.signal.aborted) return;
        grid.showError(error instanceof Error ? error.message : String(error));
    }
}

async function loadFilters() {
    const container = byId("filter-groups");
    try {
        filters = createFilters(byId("filters"), await getFilters(), state, onFilterChange);
        writeState(state); // createFilters zahodí neznámá id z adresy
    } catch (error) {
        const text = element("p", "", `Filtr se nepodařilo načíst. ${error instanceof Error ? error.message : ""}`);
        text.setAttribute("role", "alert");
        const retry = button("Zkusit znovu", () => { container.textContent = "Načítám filtr…"; loadFilters(); }, "secondary");
        container.replaceChildren(text, retry);
    }
}

/** @param {string} message @param {boolean} isError */
function announce(message, isError) {
    byId(isError ? "alert" : "status").textContent = message;
}

window.addEventListener("beforeunload", event => {
    if (!hasUnsavedWork()) return;
    event.preventDefault(); // prohlížeč zobrazí vlastní potvrzovací dialog
    event.returnValue = ""; // totéž pro starší prohlížeče
});

// Filtr načteme první, protože z adresy zahodí neznámá id, která by jinak vyprázdnila grid.
grid.setSort(state.sort);
grid.showLoading();
await loadFilters();
await loadGrid();
