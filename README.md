# EviTA – evidencia IT techniky

Desktopový program pre Windows 10/11 na evidenciu informačných technológií, zariadení a príslušenstva.

## Stiahnutie a inštalácia

1. Otvorte [najnovšiu verziu](https://github.com/tarzanislav/evita/releases/latest).
2. Stiahnite súbor **EviTA_Setup_X.Y.Z.exe** (X.Y.Z je číslo verzie) a spustite ho.
3. Windows môže zobraziť modré upozornenie *Neznámy vydavateľ* (inštalátor zatiaľ nemá platený podpisový certifikát).
   Kliknite na **Ďalšie informácie → Spustiť aj tak**.

Pri prvom spustení si vyberiete, či chcete začať s **ukážkovými dátami** (200 položiek, 30 osôb, 25 miestností),
s **prázdnou databázou**, alebo **naimportovať existujúcu evidenciu** zo súboru. Ukážkové dáta neskôr vymažete
v časti *Servis → Databáza → Vymazať dáta a začať nanovo*.

## Import a export

- **Export** (menu *Import a export*, alebo tlačidlo *Export* v Evidencii pre vyfiltrované či označené položky):
  Excel, CSV (UTF-8, bodkočiarka), JSON, XML s popisom formátu `evita.xsd`, HTML a PDF, s voľbou stĺpcov.
- **Import** cez sprievodcu v 5 krokoch: výber súboru (Excel, CSV, JSON, XML), priradenie stĺpcov, kontrola
  s opravou priamo v tabuľke, náhľad a import s protokolom. Pred zápisom sa urobí záloha a import sa dá jedným
  kliknutím vrátiť. Duplicity sa hľadajú podľa identifikátora, sériového čísla a zhody názvu, kategórie a miestnosti.
- **Prenos celej databázy** na iný počítač: úplný export JSON alebo XML naimportovaný do prázdnej evidencie
  zachová prírastkové čísla, číselníky aj históriu zmien.
- **Vzorové súbory** `vzor_import.xlsx` (s návodom a výberovými zoznamami), `.csv`, `.json`, `.xml`
  sú v priečinku `Dokumenty\EviTA\Vzory`.

## Aktualizácie

Od verzie 0.2.7 program pri spustení sám zistí, či je na GitHube nová verzia, ukáže novinky a ponúkne aktualizáciu.
Po kliknutí na *Aktualizovať teraz* sa nová verzia stiahne, databáza sa zálohuje, nainštaluje sa a program sa znova spustí.
Kontrolu možno vypnúť alebo spustiť ručne v časti *Servis → Aktualizácie*.

Každá verzia má na GitHube vlastné vydanie (napr. `v0.2.7`) s inštalátorom `EviTA_Setup_0.2.7.exe`
a zdrojovým kódom `evita-0.2.7.zip` a `evita-0.2.7.tar.gz`.

## Kde sú dáta

| Čo | Kde |
| --- | --- |
| Program | `C:\Program Files\EviTA` |
| Databáza | `C:\ProgramData\EviTA\Data\evita.db` |
| Zálohy | `Dokumenty\EviTA\Zálohy` (dá sa zmeniť v Servis → Zálohy) |
| Vzorové súbory pre import | `Dokumenty\EviTA\Vzory` |
| Exporty (predvolene) | `Dokumenty\EviTA\Exporty` |
| Meniny a sviatky (Kalendárium) | `C:\ProgramData\Kalendarium\kalendarium.db` |

Odinštalovanie programu databázu ani zálohy nemaže.

## Stav vývoja

- [x] **Etapa 1 – Jadro:** evidencia, filtre, karta položky, história zmien, hromadné pridanie a zmena, číselníky,
      ukážková databáza, jednorazová záloha, inštalátor
- [x] **Etapa 2 – Bezpečnosť dát:** pravidelné zálohy (aj cez Plánovač úloh Windows), zálohy s heslom,
      obnova zo zálohy, kontrola a oprava databázy, automatická aktualizácia štruktúry databázy
- [x] **Etapa 3 – Dashboard:** počty, graf kategórií, meniny, sviatky a dni pracovného pokoja, upozornenia
- [x] **Etapa 4 – Import a export:** všetky formáty, sprievodca importom, vzorové súbory, prenos celej databázy
- [ ] Etapa 5 – Tlač a zostavy
- [ ] Etapa 6 – Kalendár
- [ ] Etapa 7 – Kalendárium (citáty, výročia)
- [ ] Etapa 8 – Skenovanie a údaje z internetu
- [ ] Etapa 9 – Dokončenie

## Technické informácie

- C# / .NET 10 (LTS), WPF s Fluent vzhľadom Windows 11
- SQLite cez Entity Framework Core
- Inštalátor: Inno Setup, zostavuje sa automaticky cez GitHub Actions pri každej zmene vetvy `main`

Štruktúra:

```
src/Evita.App/
  Models/       dátové triedy (položka, kategória, miestnosť, osoba, história)
  Data/         databáza (EF Core, SQLite)
  Services/     evidencia, číselníky, servis, zálohy, import a export, ukážkové dáta
  ViewModels/   logika obrazoviek
  Views/        okná a obrazovky (XAML)
installer/      skript inštalátora
```
