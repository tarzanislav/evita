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

## Jazyky

Program je v **angličtine** (predvolený jazyk), **slovenčine, češtine, poľštine, maďarčine, ruštine a čínštine**.
Jazyk zmeníte v časti *Nastavenia → Jazyk programu*; tlačidlo **Language reset** vráti angličtinu. Zmena sa prejaví
po reštarte programu. Prekladá sa celé rozhranie, správy, tlačové zostavy, PDF a hlavičky exportov; import rozpozná
stĺpce v ktoromkoľvek jazyku. Dáta evidencie (názvy položiek, miestností, osôb) sa neprekladajú – počiatočné
kategórie a typy udalostí, ktoré ste nepremenovali, sa po zmene jazyka premenujú do nového jazyka.

Preklady sú v `src/Evita.App/Preklady/<jazyk>.json` (kľúčom je slovenský text). Skript `nastroje/preklady.py kontrola`
overí, že každý text v programe má preklad vo všetkých jazykoch.

## Dashboard, úlohy a poznámky

- **Dashboard:** dnešný deň s meninami a sviatkami, úlohy (To Do), **počasie** pre zvolené mesto alebo obec
  (Open-Meteo, predpoveď na 5 dní), **kurzový lístok** ECB s vývojom za 30 dní a prepočtom mien, pripnuté poznámky,
  kalendár na 7 dní, počty, upozornenia. Panely sa dajú skryť (*Prispôsobiť dashboard*).
- **Úlohy:** samostatný zoznam úloh s prioritou, termínom, popisom a voliteľným naviazaním na položku evidencie.
- **Poznámky:** poznámkový blok s farbami, pripnutím a vyhľadávaním; poznámku možno prepojiť s dňom v kalendári,
  s udalosťou alebo s položkou evidencie (zobrazí sa v kalendári aj v karte položky).

## Nastavenia a konfigurácia

V časti *Nastavenia* je jazyk, miesto pre počasie, meny kurzového lístka a **export a import konfigurácie programu**
(súbor JSON bez dát evidencie – jazyk, panely dashboardu, nastavenia kalendára a typy udalostí, pravidelné zálohy,
názov a logo organizácie, tlač a export). Heslo automatických záloh sa z bezpečnostných dôvodov neprenáša.

## Import a export

- **Export** (menu *Import a export*, alebo tlačidlo *Export* v Evidencii pre vyfiltrované či označené položky):
  Excel, CSV (UTF-8, bodkočiarka), JSON, XML s popisom formátu `evita.xsd`, HTML a PDF, s voľbou stĺpcov.
- **Import** cez sprievodcu v 5 krokoch: výber súboru (Excel, CSV, JSON, XML), priradenie stĺpcov, kontrola
  s opravou priamo v tabuľke, náhľad a import s protokolom. Pred zápisom sa urobí záloha a import sa dá jedným
  kliknutím vrátiť. Duplicity sa hľadajú podľa identifikátora, sériového čísla a zhody názvu, kategórie a miestnosti.
- **Import číselníkov** v tom istom sprievodcovi (*Doplniť kategórie, miestnosti alebo osoby*): z Excelu, CSV alebo zo
  súboru EviTA (JSON, XML). Pridajú sa len záznamy, ktoré ešte neexistujú; meno a priezvisko môžu byť aj v jednom
  stĺpci. V Exceli sa použije hárok s názvom číselníka (Kategórie, Miestnosti, Osoby).
- **Prenos celej databázy** na iný počítač: úplný export JSON alebo XML naimportovaný do prázdnej evidencie
  zachová prírastkové čísla, číselníky aj históriu zmien.
- **Vzorové súbory** sú priamo v programe (*Import a export → Vzorové súbory pre import*, aj v sprievodcovi).
  Kliknutím sa uloží do priečinka Stiahnuté: `EviTA_vzor_Excel.xlsx` (s návodom a výberovými zoznamami),
  `EviTA_vzor_CSV.csv`, `EviTA_vzor_JSON.json`, `EviTA_vzor_XML.xml` (spolu s popisom formátu `evita.xsd`)
  a `EviTA_vzor_ciselniky.xlsx` (kategórie, miestnosti a osoby, každý číselník na vlastnom hárku).

## Aktualizácie

Od verzie 0.2.7 program pri spustení sám zistí, či je na GitHube nová verzia, ukáže novinky a ponúkne aktualizáciu.
Po kliknutí na *Aktualizovať teraz* sa nová verzia stiahne, databáza sa zálohuje, nainštaluje sa a program sa znova spustí.
Kontrolu možno vypnúť alebo spustiť ručne v časti *Servis → Aktualizácie*.

Každá verzia má na GitHube vlastné vydanie (napr. `v0.2.7`) s inštalátorom `EviTA_Setup_0.2.7.exe`
a zdrojovým kódom `evita-0.2.7.zip` a `evita-0.2.7.tar.gz`.

## Pracovný kalendár

Menu *Kalendár* ukazuje mesiac, týždeň alebo zoznam udalostí. Víkendy a dni pracovného pokoja sú farebne odlíšené,
pri každom dni sú meniny a sviatky. Dvojklikom na deň pridáte udalosť alebo úlohu (typ s farbou, čas alebo celý deň
či obdobie cez viac dní, opakovanie, pripomienka, naviazanie na položku, miestnosť alebo osobu, stav). Koniec záruky,
ďalšia revízia, pravidelná záloha a ročná inventúra sa do kalendára dostanú automaticky (dajú sa vypnúť v nastaveniach
kalendára). Pri spustení programu sa zobrazí prehľad dňa s úlohami po termíne a pripomienkami. Kalendár sa dá
vytlačiť (mesiac na strane alebo zoznam) a uložiť ako `.ics` pre Outlook alebo Google Kalendár.

## Tlač a zostavy

V menu *Tlač a zostavy* vyberiete zostavu, nastavíte obdobie, zoskupenie, stĺpce a ich poradie a orientáciu A4.
Každá zostava sa dá pozrieť v náhľade, vytlačiť alebo jedným kliknutím uložiť do PDF. Hlavička obsahuje názov
organizácie a logo (nastavíte na tej istej obrazovke), päta dátum tlače a číslo strany. Zobrazené položky
z Evidencie vytlačíte tlačidlom *Tlačiť…* alebo klávesmi Ctrl+P.

Nastavenú zostavu uložíte tlačidlom **Uložiť zostavu…** pod vlastným názvom – so stĺpcami a ich poradím, filtrami
(miestnosť, osoba, kategória), orientáciou a obdobím. Obdobie môže byť pevné (od – do) alebo napr. *minulý mesiac*
či *tento rok* – vtedy sa pri každom použití prepočíta. Uložené zostavy sú vľavo pod zoznamom zostáv; prenášajú sa
aj s exportom konfigurácie programu.

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
- [x] **Etapa 5 – Tlač a zostavy:** súhrnný zoznam, vyfiltrované položky, po miestnostiach (inventúrny zoznam s podpismi),
      po kategóriách, podľa pridelenia (zverená technika s podpisom), prírastkový zoznam, podľa dátumu pridania,
      vyradené položky, história zmien; náhľad, tlač a PDF; hlavička s názvom organizácie a logom
- [x] **Etapa 6 – Pracovný kalendár:** mesiac, týždeň a zoznam so sviatkami, menami a číslom týždňa; udalosti a úlohy
      s typmi a farbami, opakovaním, pripomienkou a naviazaním na položku, miestnosť alebo osobu; automatické termíny
      (záruky, revízie, zálohy, inventúra); prehľad dňa pri spustení; upozornenie pred udalosťou; panel na dashboarde;
      záložka Udalosti v karte položky; tlač kalendára a export `.ics` pre Outlook a Google Kalendár
- [x] **Verzia 0.7 – Dashboard a jazyky:** kurzový lístok, počasie, úlohy (To Do), poznámkový blok prepojený
      s kalendárom a položkami, export a import konfigurácie, jazyky EN, SK, CS, PL, HU, RU, ZH s tlačidlom Language reset
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
  Services/     evidencia, číselníky, servis, zálohy, import a export, kalendár, úlohy, poznámky,
                kurzy, počasie, konfigurácia, preklad, ukážkové dáta
  Preklady/     slovníky prekladov (en, cs, pl, hu, ru, zh)
  ViewModels/   logika obrazoviek
  Views/        okná a obrazovky (XAML)
installer/      skript inštalátora
nastroje/       pomocné skripty (kontrola prekladov)
```
