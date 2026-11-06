// @ts-check
// Propojení modulů: načte možnosti filtru, obnoví stav z adresy a načte grid.

import { getFilters, getStock } from "./api.js";
import { button, element } from "./dom.js";
import { hasUnsavedWork } from "./editing.js";
import { createFilters, isFilterActive } from "./filters.js";
import { debounce, plural } from "./format.js";
import { createGrid } from "./grid.js";
import { onPendingChange, pendingCount, savesStarted, whenSaved } from "./row-saver.js";
import { readState, writeState } from "./url-state.js";

const byId = (/** @type {string} */ id) => /** @type {HTMLElement} */ (document.getElementById(id));

const state = readState();
const saveStatus = /** @type {HTMLButtonElement} */ (byId("save-status"));
const gridTable = /** @type {HTMLTableElement} */ (byId("grid"));

/** @type {{ update: () => void, reset: () => void } | null} */
let filters = null;
/** @type {AbortController | null} */
let loading = null;
/** První načtení gridu se neoznamuje, další (po změně filtru) ano. */
let loadedOnce = false;
/** Stav "Vše uloženo" ukážeme až po prvním uložení, do té doby by nic neříkal. */
let savedOnce = false;

const grid = createGrid(gridTable, byId("summary"), {
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

const problemEditors = () => /** @type {HTMLElement[]} */ ([...document.querySelectorAll(".qty-editor:is(.invalid, .error, .conflict)")]);

/**
 * Stav ukládání v hlavičce: ukládá se, neuložené problémy (klik přejde na první z nich),
 * "Vše uloženo" po aspoň jednom uložení, jinak skryto.
 */
function refreshSaveStatus() {
    const saving = pendingCount();
    const problems = problemEditors().length;
    if (saving > 0) savedOnce = true;
    saveStatus.classList.toggle("busy", saving > 0);
    saveStatus.classList.toggle("problem", saving === 0 && problems > 0);
    saveStatus.disabled = !(saving === 0 && problems > 0);
    saveStatus.hidden = saving === 0 && problems === 0 && !savedOnce;
    saveStatus.textContent = saving > 0 ? `Ukládám ${plural(saving, ["změnu", "změny", "změn"])}`
        : problems > 0 ? `Neuloženo: ${plural(problems, ["položka", "položky", "položek"])}`
        : "Vše uloženo";
}

saveStatus.addEventListener("click", () => {
    const first = problemEditors()[0];
    if (!first) return;
    first.closest("tr")?.scrollIntoView({ block: "center" });
    /** @type {HTMLInputElement | null} */ (first.querySelector("input.qty"))?.focus();
});
onPendingChange(refreshSaveStatus);
gridTable.addEventListener("editorstate", refreshSaveStatus);

// Na úzkém okně je filtr sbalený, na širokém vždy otevřený.
const narrow = matchMedia("(max-width: 860px)");
const filterPanel = /** @type {HTMLDetailsElement} */ (byId("filter-panel"));
filterPanel.open = !narrow.matches;
narrow.addEventListener("change", event => (filterPanel.open = !event.matches));

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
            if (savesStarted() === savesBefore) {
                grid.setRows(rows);
                refreshSaveStatus();
                if (loadedOnce) announce(`Zobrazeno ${plural(rows.length, ["položka", "položky", "položek"])}`, false);
                loadedOnce = true;
                return;
            }
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
