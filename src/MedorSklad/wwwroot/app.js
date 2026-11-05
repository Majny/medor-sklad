"use strict";

const MAX_QUANTITY = 2147483647; // int.MaxValue na serveru

// ---------- REST API ----------

const api = {
    getFilters() {
        return request("/api/filters");
    },

    getStock(filter) {
        const params = new URLSearchParams();
        for (const [key, ids] of Object.entries(filter)) {
            ids.forEach(id => params.append(key, id));
        }
        return request(`/api/stock?${params}`);
    },

    updateQuantity(clientId, productId, quantity) {
        return request(`/api/stock/${clientId}/${productId}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ quantity }),
        });
    },
};

async function request(url, options) {
    let response;
    try {
        response = await fetch(url, options);
    } catch {
        throw new Error("Server není dostupný.");
    }
    if (!response.ok) {
        throw new Error(await errorMessage(response));
    }
    return response.status === 204 ? null : response.json();
}

/** Česká chybová hláška z ProblemDetails (detail, případně první validační chyba). */
async function errorMessage(response) {
    try {
        const problem = await response.json();
        if (problem.detail) return problem.detail;
        const firstError = Object.values(problem.errors ?? {}).flat()[0];
        if (firstError) return firstError;
    } catch {
        // tělo není JSON, použijeme obecnou hlášku níže
    }
    if (response.status === 404) return "Záznam neexistuje.";
    if (response.status >= 500) return "Chyba serveru, zkuste to prosím znovu.";
    return `Požadavek se nepodařil (${response.status}).`;
}

// ---------- Stavová lišta ----------

function showStatus(message, kind) {
    const status = document.getElementById(kind === "error" ? "error" : "status");
    status.textContent = message;
    if (kind === "error") {
        document.getElementById("status").textContent = "";
    } else {
        document.getElementById("error").textContent = "";
    }
}

// ---------- Filtr ----------

const filter = { clientIds: [], categoryIds: [], productIds: [] };

function renderOptions(containerId, items, key) {
    const container = document.getElementById(containerId);
    container.replaceChildren(...items.map(item => {
        const label = document.createElement("label");
        label.className = "option";
        label.dataset.name = normalize(item.name);

        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.value = item.id;
        checkbox.addEventListener("change", () => {
            filter[key] = [...container.querySelectorAll("input:checked")].map(i => Number(i.value));
            updateSelectedCount(key);
            filterProductList();
            loadGrid();
        });

        label.append(checkbox, document.createTextNode(item.name));
        return label;
    }));
}

function updateSelectedCount(key) {
    const count = filter[key].length;
    const badge = document.querySelector(`.filter-group[data-key="${key}"] .count`);
    badge.textContent = count > 0 ? `vybráno ${count}` : "";
}

function resetFilter() {
    document.querySelectorAll(".filter input[type=checkbox]").forEach(c => (c.checked = false));
    document.getElementById("product-search").value = "";
    Object.keys(filter).forEach(key => {
        filter[key] = [];
        updateSelectedCount(key);
    });
    filterProductList();
    loadGrid();
}

/** Vyhledávání bez ohledu na diakritiku a velikost písmen. */
function normalize(text) {
    return text.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase();
}

/** Skryje produkty neodpovídající hledání. Vybrané produkty zůstávají vidět, aby bylo jasné, čím je grid filtrován. */
function filterProductList() {
    const needle = normalize(document.getElementById("product-search").value.trim());
    document.querySelectorAll("#products .option").forEach(option => {
        const checked = option.querySelector("input").checked;
        option.hidden = needle !== "" && !checked && !option.dataset.name.includes(needle);
    });
}

// ---------- Grid ----------

/** Rozpracovaná uložení. Grid se znovu načte až po jejich dokončení, aby neukázal starý počet. */
const pendingSaves = new Set();
let gridRequestId = 0;

async function loadGrid() {
    const requestId = ++gridRequestId;
    document.getElementById("row-count").textContent = "Načítám…";
    try {
        await Promise.allSettled([...pendingSaves]);
        const rows = await api.getStock(filter);
        if (requestId !== gridRequestId) return; // mezitím přišel novější požadavek
        renderGrid(rows);
        document.getElementById("error").textContent = "";
    } catch (error) {
        if (requestId !== gridRequestId) return;
        document.getElementById("row-count").textContent = "";
        document.getElementById("grid-body").replaceChildren(messageRow("Data se nepodařilo načíst."));
        showStatus(`Nepodařilo se načíst data: ${error.message}`, "error");
    }
}

function renderGrid(rows) {
    const body = document.getElementById("grid-body");
    document.getElementById("row-count").textContent = `Řádků: ${rows.length}`;

    if (rows.length === 0) {
        body.replaceChildren(messageRow("Filtru neodpovídá žádná položka skladu."));
        return;
    }

    body.replaceChildren(...rows.map(row => {
        const tr = document.createElement("tr");
        tr.append(cell(row.clientName), cell(row.categories.join(", ")), cell(row.productName));

        const td = document.createElement("td");
        td.className = "num";
        td.append(...quantityEditor(row));
        tr.append(td);
        return tr;
    }));
}

function cell(text) {
    const td = document.createElement("td");
    td.textContent = text;
    return td;
}

function messageRow(text) {
    const tr = document.createElement("tr");
    const td = document.createElement("td");
    td.colSpan = 4;
    td.className = "empty";
    td.textContent = text;
    tr.append(td);
    return tr;
}

/**
 * Editor počtu kusů. Uložení jedné buňky běží za sebou (nikdy dvě najednou),
 * takže v databázi skončí vždy poslední zadaná hodnota.
 */
function quantityEditor(row) {
    const input = document.createElement("input");
    input.type = "number";
    input.min = "0";
    input.max = String(MAX_QUANTITY);
    input.step = "1";
    input.inputMode = "numeric";
    input.value = row.quantity;
    input.className = "qty";
    input.setAttribute("aria-label", `Počet kusů: ${row.productName}, ${row.clientName}`);

    const mark = document.createElement("span");
    mark.className = "mark";
    mark.setAttribute("aria-hidden", "true");

    let saved = row.quantity; // poslední hodnota potvrzená serverem
    let queue = Promise.resolve();

    const setState = (state, symbol, title) => {
        input.classList.remove("saving", "saved", "failed");
        if (state) input.classList.add(state);
        input.toggleAttribute("aria-invalid", state === "failed");
        mark.textContent = symbol;
        mark.title = title;
    };

    input.addEventListener("keydown", e => {
        if (e.key === "Enter") input.blur();
        if (e.key === "Escape") {
            input.value = saved;
            setState(null, "", "");
            input.blur();
        }
    });

    input.addEventListener("change", () => {
        const text = input.value.trim();
        const quantity = Number(text);
        if (!/^\d+$/.test(text) || quantity > MAX_QUANTITY) {
            input.value = saved;
            setState("failed", "!", "Neplatná hodnota");
            showStatus("Počet kusů musí být celé nezáporné číslo.", "error");
            return;
        }

        setState("saving", "…", "Ukládám");
        const save = queue.then(async () => {
            try {
                await api.updateQuantity(row.clientId, row.productId, quantity);
                saved = quantity;
                if (Number(input.value) === quantity) {
                    setState("saved", "✓", "Uloženo");
                }
                showStatus(`Uloženo: ${row.productName} (${row.clientName}) = ${quantity} ks`, "ok");
            } catch (error) {
                if (Number(input.value) === quantity) {
                    input.value = saved;
                }
                setState("failed", "!", error.message);
                showStatus(`Uložení selhalo: ${error.message}`, "error");
            }
        });
        queue = save;
        pendingSaves.add(save);
        save.finally(() => pendingSaves.delete(save));
    });

    return [input, mark];
}

// ---------- Start ----------

async function init() {
    document.getElementById("reset-filter").addEventListener("click", resetFilter);
    document.getElementById("product-search").addEventListener("input", filterProductList);

    try {
        const options = await api.getFilters();
        renderOptions("clients", options.clients, "clientIds");
        renderOptions("categories", options.categories, "categoryIds");
        renderOptions("products", options.products, "productIds");
    } catch (error) {
        showStatus(`Nepodařilo se načíst filtr: ${error.message}`, "error");
    }
    await loadGrid();
}

init();
