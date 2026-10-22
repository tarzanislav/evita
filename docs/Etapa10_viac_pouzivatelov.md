# Etapa 10 – Viac používateľov, prístupové práva a sieťová databáza

Špecifikácia na neskoršie spracovanie (nezačatá). Cieľ: EviTA zostane plnohodnotným programom na jednom počítači
(ako doteraz), ale bude vedieť pracovať aj s **viacerými používateľmi (cca 10 – 20)** s **prístupovými právami**
a voliteľne s **databázou na serveri**, ku ktorej sa pripája viac počítačov naraz.

## 1. Režimy prevádzky

| Režim | Databáza | Používatelia | Pre koho |
|---|---|---|---|
| **A – Lokálny** (predvolený, ako doteraz) | SQLite v dátovom priečinku | bez prihlasovania (jeden správca) | jeden počítač, jeden človek |
| **B – Lokálny s používateľmi** | SQLite v dátovom priečinku | prihlasovanie, role a práva | jeden zdieľaný počítač, viac ľudí (striedavo) |
| **C – Server** | MariaDB/MySQL, Microsoft SQL Server (aj Express) alebo PostgreSQL | prihlasovanie, role a práva | viac počítačov v sieti naraz (10 – 20 používateľov) |

- Režim sa volí pri prvom spustení a dá sa zmeniť v *Nastavenia → Databáza a používatelia* (len správca).
- Prechod A → B: zapne sa prihlasovanie, vytvorí sa účet správcu (meno + heslo), dáta zostávajú.
- Prechod A/B → C: sprievodca **Preniesť evidenciu na server** (kontrola spojenia, vytvorenie tabuliek, prenos všetkých
  dát vrátane príloh, histórie, kalendára, úloh, poznámok a nastavení; počty záznamov sa porovnajú; lokálna databáza
  zostane ako záloha, program sa prepne na server).
- Návrat C → A: export zo servera do novej lokálnej databázy (napr. na notebook do terénu).
- SQLite súbor v zdieľanom sieťovom priečinku **nie je podporovaný** (riziko poškodenia pri súčasnom zápise) –
  program to zistí (UNC cesta / sieťová jednotka) a upozorní.

## 2. Databázové systémy (režim C)

- Prístup ostáva cez **Entity Framework Core**; poskytovatelia:
  - MariaDB/MySQL – `Pomelo.EntityFrameworkCore.MySql` (MIT),
  - SQL Server – `Microsoft.EntityFrameworkCore.SqlServer` (MIT),
  - PostgreSQL – `Npgsql.EntityFrameworkCore.PostgreSQL` (PostgreSQL licencia).
  Pred pridaním každú knižnicu overiť (licencia, pôvod, verzia, závislosti) a uviesť v README; nič iné nepridávať.
- **Odporúčanie pre používateľa:** škola/úrad s Windows serverom → SQL Server Express; NAS (Synology, QNAP) alebo
  Linux → MariaDB; inak PostgreSQL.
- Pripojenie: server, port, názov databázy, používateľ databázy, heslo, šifrovanie (TLS povinné mimo localhost),
  tlačidlo *Overiť spojenie*. Heslo k databáze uložené cez **DPAPI** (ako heslo SMTP), do exportu konfigurácie sa
  neprenáša.
- Program používa jeden databázový účet s právami len na svoju databázu (SELECT/INSERT/UPDATE/DELETE + úprava schémy
  pri aktualizácii). Návod na vytvorenie databázy a účtu (skript pre každý systém) bude v README / v sprievodcovi.

## 3. Úpravy dátovej vrstvy

1. **Migrácie schémy** (`Databaza.Migracie`, `VerziaSchemy`): oddeliť od SQLite – pre každý poskytovateľ vlastná
   sada krokov alebo prechod na EF Core migrácie s poskytovateľom-špecifickými súbormi. Aktualizácia programu na
   jednom počítači upraví schému servera; staršie verzie programu sa k novšej schéme odmietnu pripojiť (hláška
   „aktualizujte EviTA“), novšie k staršej schéme ju upravia len so súhlasom správcu a po zálohe.
2. **Prírastkové čísla** prideľuje databáza (sekvencia / tabuľka čísel s atomickým zvýšením v transakcii) – nikdy
   dva rovnaké ani pri súčasnom ukladaní.
3. **Súbežné úpravy:** stĺpec `Verzia` (už existuje) ako optimistický zámok (concurrency token). Pri strete: okno
   „Položku medzitým zmenil(a) X o hh:mm“ s porovnaním polí a voľbou *Ponechať moje / Prevziať ich / Zlúčiť*.
   Rovnako pre kalendár, úlohy, poznámky, číselníky.
4. **Obnova zoznamov:** v režime C sa zmeny od iných zisťujú periodicky (napr. každých 30 s podľa `DatumZmeny`)
   a pri návrate do okna; zoznam sa obnoví bez straty výberu a filtrov. Upozornenie „X zmenil(a) 3 položky“.
5. **Nastavenia:** rozdeliť na
   - *spoločné* (v databáze): organizácia a logo, kategórie, miestnosti, osoby, typy udalostí, šablóny štítkov,
     uložené zostavy (voliteľne zdieľané), hranica obnovy, pravidlá záloh servera;
   - *osobné/počítačové* (lokálny súbor v profile používateľa Windows): jazyk, tlačiareň a voľby tlače, PDF,
     e-mail (SMTP), panely dashboardu, posledné filtre, port mobilného skenera, kamera.
6. **Prílohy a obrázky** (ak sú v databáze ako BLOB) – overiť veľkosti pre každý systém; prípadne limit veľkosti.
7. Dátumy ukladať jednotne (UTC alebo „bez časovej zóny“ pre dátumy bez času) – overiť pri všetkých poskytovateľoch.
8. Vyhľadávanie bez diakritiky a radenie: ponechať v programe (nie závislé od kolácie databázy).

## 4. Používatelia, prihlásenie a práva (režimy B a C)

### Používateľ
- Meno (prihlasovacie), celé meno, e-mail, rola, aktívny/zablokovaný, posledné prihlásenie, voliteľne prepojenie na
  osobu z číselníka Osoby (aby „Moja technika“ ukázala pridelené položky).
- **Heslo:** uložené len ako hash (PBKDF2/Argon2 z .NET, so soľou, veľa iterácií), min. 8 znakov, zmena pri prvom
  prihlásení, zablokovanie na 5 min. po 5 zlých pokusoch, správca môže heslo resetovať (dočasné heslo).
- Voliteľne **prihlásenie účtom Windows** (meno používateľa domény) bez ďalšieho hesla – len v doméne.
- Odhlásenie / zmena používateľa bez zatvorenia programu; automatické odhlásenie po nečinnosti (nastaviteľné).

### Roly (predvolené, správca ich môže upraviť alebo pridať vlastné)
| Rola | Čo môže |
|---|---|
| **Správca** | všetko vrátane používateľov, rolí, nastavení databázy, záloh a obnovy, importu, vymazania |
| **Správca evidencie** | evidencia (pridať, upraviť, vyradiť, vymazať), číselníky, import/export, štítky, inventúra, zostavy |
| **Technik** | pridať a upraviť položky, vyradiť, štítky, inventúra, kalendár/revízie; nie vymazať, nie číselníky |
| **Inventarizátor** | len inventúra (skenovanie, zápis nálezov) a prezeranie |
| **Čitateľ** | len prezeranie, tlač a export (bez zmien) |

### Práva (jemné, priraďujú sa roliam)
`evidencia.citat`, `evidencia.pridat`, `evidencia.upravit`, `evidencia.vyradit`, `evidencia.vymazat`,
`evidencia.ceny` (vidieť/upraviť ceny a sumy – aj v Štatistike a exporte), `ciselniky.upravit`, `import`,
`export`, `tlac`, `stitky`, `inventura`, `kalendar.upravit`, `ulohy`, `poznamky`, `statistika`, `zalohy`,
`nastavenia`, `pouzivatelia`.
- Voliteľné **obmedzenie na miestnosti/budovy** (napr. technik budovy B vidí a mení len svoje miestnosti).
- UI: tlačidlá a sekcie bez práva sú skryté alebo neaktívne s tooltipom „Na túto akciu nemáte oprávnenie“;
  kontrola práv aj v službách (nie len v UI), aby sa nedala obísť.
- Poznámky a úlohy: súkromné (len autor) alebo zdieľané.

### Audit
- História zmien (už existuje) doplniť o **používateľa** a počítač: kto, kedy, čo zmenil (pôvodná → nová hodnota).
- Záznam prihlásení, zlyhaných pokusov, zmien používateľov/práv, importov, obnovy zálohy, vymazaní.
- Zostava *História zmien* filtrovateľná podľa používateľa.

### Správa používateľov
- Sekcia *Nastavenia → Používatelia a práva* (len správca): zoznam, pridať, upraviť, zablokovať, reset hesla,
  priradenie roly, editor rolí (zaškrtávanie práv), export zoznamu.
- Vždy musí zostať aspoň jeden aktívny správca (kontrola pri zmene/zablokovaní).
- Núdzový prístup: ak sa správca zabudne heslo v režime B, obnova zo zálohy alebo „reset správcu“ s potvrdením
  heslom lokálneho účtu Windows s právami administrátora.

## 5. Zálohy a obnova
- Režim A/B: ako doteraz (kópia súboru SQLite, voliteľne s heslom).
- Režim C: **logická záloha** do súboru EviTA (všetky tabuľky do JSON/ZIP, šifrovanie heslom ako doteraz) – nezávislá
  od databázového systému, dá sa obnoviť aj do SQLite (a naopak). Odporúčanie zálohovať aj na strane servera
  (nástroje databázy). Obnova len správca, s potvrdením a predchádzajúcou zálohou aktuálneho stavu.
- Pravidelnú zálohu servera robí len jeden počítač (určený v nastaveniach), aby sa neduplikovala.

## 6. Ostatné časti programu
- **Mobilný skener / kamera:** fungujú na každom počítači samostatne; zápis do spoločnej databázy.
- **Inventúra:** viac ľudí naraz v rôznych miestnostiach tej istej inventúry; nálezy sa zlučujú, prehľad v reálnom čase.
- **Štítky, tlač, PDF, e-mail:** lokálne nastavenia; uložené zostavy a nastavenia štítkov voliteľne zdieľané.
- **Aktualizácie:** upozornenie, ak iné počítače majú staršiu verziu, ktorá nepodporuje aktuálnu schému.
- **Dashboard:** „Moja technika“ a „Moje úlohy“ podľa prihláseného používateľa.
- **Výkon:** 10 – 20 používateľov, desiatky tisíc položiek – načítanie zoznamu s filtrami na strane databázy pri
  veľkom počte položiek; overiť, že Evidencia sa otvorí do 2 s pri 20 000 položkách.

## 7. Bezpečnosť
- Šifrované spojenie s databázou (TLS); bez TLS len localhost.
- Heslá používateľov len ako hash; heslá k databáze/SMTP cez DPAPI; nič citlivé v exporte konfigurácie ani v logoch.
- Princíp najmenších práv pre databázový účet; parametrizované dotazy (EF Core), žiadne skladanie SQL z textu.
- Bezpečnostné pravidlá čítačky kódov ostávajú bez zmeny (zxing-cpp v3.1.1 zo zdroja, len kernel32.dll).

## 8. Testovanie (CI)
- Samotest režimu B na SQLite: vytvorenie používateľov, prihlásenie, zlé heslo/zablokovanie, kontrola práv v UI aj
  v službách, audit s menom používateľa, posledný správca sa nedá zablokovať.
- Samotest režimu C: v GitHub Actions spustiť **MariaDB, SQL Server a PostgreSQL** (služby/kontajnery alebo inštalácia
  na Windows runneri) a pre každý: vytvorenie schémy, prenos vzorovej evidencie zo SQLite (porovnanie počtov
  a súčtov cien), dva súbežní klienti (stret úprav → okno stretu, prírastkové čísla bez duplicít), logická záloha
  a obnova do SQLite, migrácia schémy z predošlej verzie.
- Snímky nových okien (prihlásenie, používatelia a práva, nastavenie databázy, sprievodca prenosom, stret úprav).
- Preklady všetkých nových textov (EN, SK, CS, PL, HU, RU, ZH).

## 9. Postup prác (návrh poradia)
1. Rozdelenie nastavení na spoločné a lokálne; abstrakcia poskytovateľa databázy; migrácie pre viac systémov.
2. Používatelia, roly, práva, prihlásenie, audit (režim B na SQLite).
3. Poskytovatelia MariaDB/MySQL, SQL Server, PostgreSQL; okno pripojenia; sprievodca prenosom (režim C).
4. Súbežnosť: prírastkové čísla, optimistické zámky, okno stretu, obnova zoznamov.
5. Logické zálohy a obnova medzi systémami; inventúra viacerými ľuďmi.
6. Testy v CI pre všetky tri databázy, preklady, README, vydanie.

## 10. Otázky na používateľa pred začatím
- Koľko počítačov a ľudí bude pracovať naraz; je k dispozícii server, NAS alebo len bežné počítače?
- Ktorý databázový systém uprednostniť (prípadne už existujúci server v organizácii)?
- Prihlasovanie vlastným heslom EviTA, účtom Windows (doména), alebo oboje?
- Majú byť ceny skryté pre niektoré roly? Treba obmedzenie práv na miestnosti/budovy?
