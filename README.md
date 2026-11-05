# Správa skladu

Jednoduchá webová aplikace v ASP.NET Core (.NET 10) pro správu skladu produktů. Jedna stránka s filtrovatelným gridem, data přes REST API, uložení v SQLite přes Entity Framework Core.

## Spuštění

Potřeba je .NET 10 SDK.

```bash
dotnet run --project src/MedorSklad
```

Aplikace běží na http://localhost:5033. Při prvním spuštění se vytvoří soubor `src/MedorSklad/sklad.db` a naplní se testovacími daty. Pro čistý start stačí soubor smazat.

Testy:

```bash
dotnet test
```

## Doménový model

- **Produkt** je společný pro všechny klienty a může patřit do více kategorií.
- **Klient** má přidělené kategorie, kterými se zabývá. Jeho sortiment tvoří všechny produkty z těchto kategorií.
- **Skladová položka** (`StockItem`) drží počet kusů konkrétního produktu na skladu konkrétního klienta. Každý klient má tedy vlastní sklad.

## Grid a filtr

- Jeden řádek gridu = jeden produkt na skladu jednoho klienta. Sloupec Kategorie ukazuje ty kategorie produktu, kterými se klient zabývá. Produkt ve dvou kategoriích klienta tak není v gridu dvakrát se stejným počtem kusů.
- Filtr umožňuje vybrat klienty, kategorie a produkty. Prázdná skupina znamená bez omezení, skupiny se kombinují logickým AND.
- Filtr kategorií ponechá řádek, pokud produkt u daného klienta spadá aspoň do jedné vybrané kategorie.
- Počet kusů se mění přímo v gridu. Uloží se po potvrzení (Enter nebo opuštění pole), Escape vrátí původní hodnotu. Uložení jedné buňky běží za sebou a grid se po změně filtru načte až po dokončení rozpracovaných uložení, takže nikdy neukáže starý stav.

## REST API

| Metoda | Cesta | Popis |
|---|---|---|
| GET | `/api/filters` | Klienti, kategorie a produkty pro menu filtru |
| GET | `/api/stock?clientIds=1&categoryIds=2&productIds=3` | Řádky gridu, parametry lze opakovat |
| PUT | `/api/stock/{clientId}/{productId}` | Tělo `{ "quantity": 10 }`, nastaví počet kusů |

PUT vrací `204` při úspěchu, `400` pro chybějící nebo záporný počet a pro produkt mimo sortiment klienta, `404` pro neexistujícího klienta či produkt. Chyby mají tvar ProblemDetails s českou hláškou. Příklady volání jsou v `src/MedorSklad/MedorSklad.http`.

## Struktura

```
src/MedorSklad/
  Data/         entity, DbContext, seed testovacích dat
  Services/     StockService, doménová logika gridu a úprav
  Controllers/  REST API
  Contracts/    DTO pro API
  wwwroot/      stránka (HTML, CSS, JavaScript bez build kroku)
tests/MedorSklad.Tests/  xUnit: testy služby nad SQLite v paměti a integrační testy REST API (WebApplicationFactory)
```

## Zjednodušení

- Schéma se vytváří přes `EnsureCreated`, protože jde o databázi v souboru pro snadné testování. V produkci bych použil EF migrace.
- Bez autentizace a bez řešení souběžných úprav (poslední zápis vyhrává).
- Frontend je čisté HTML a JavaScript, aby šla aplikace spustit jediným příkazem bez Node.js.
