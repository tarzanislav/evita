# Import z databázy iného programu (požiadavka 12) – plán

Stav: **čaká na súbor databázy** (dodá používateľ o niekoľko dní). Zatiaľ sa nič neimplementuje.

## Zadanie

Preniesť dáta z podobného programu na evidenciu tovaru do EviTA. Databáza môže byť SQL/MySQL alebo iného typu.
Najprv súbor databázy pripraví Claude (analýza a úprava), potom sa naimportuje existujúcim sprievodcom importom.

## Postup po dodaní súboru

1. **Vstup** – ideálne jeden z formátov:
   - `.sql` dump z MySQL/MariaDB (phpMyAdmin → Exportovať, alebo `mysqldump`),
   - `.db` / `.sqlite` / `.sqlite3`,
   - `.mdb` / `.accdb` (Microsoft Access),
   - `.dbf` (dBase/FoxPro),
   - alebo export do Excelu/CSV, prípadne názov pôvodného programu, ak má vlastný formát.
2. **Analýza** – tabuľky, stĺpce a väzby (kategórie, miestnosti, osoby, dodávatelia…), kódovanie diakritiky,
   formáty dátumov a cien, duplicity, prázdne a chybné hodnoty, počty záznamov.
3. **Návrh** – zoznam údajov z pôvodnej databázy, ktoré EviTA dnes nemá (napr. dodávateľ, číslo faktúry,
   umiestnenie v rámci miestnosti, vlastné polia), s odporúčaním: doplniť do EviTA ako nové pole alebo číselník,
   preniesť do poznámky, alebo vynechať. Rozhodne používateľ.
4. **Prevod** – výstup vo formáte pre sprievodcu importom EviTA (Excel/JSON so stĺpcami EviTA a súbor číselníkov),
   s protokolom, čo sa ako previedlo. Pred importom sa v EviTA urobí záloha a import sa dá vrátiť.
5. Ak sa schvália nové polia, doplnia sa do EviTA (databáza, karta položky, filtre, export/import, tlač) ešte pred importom.

## Poznámky

- Osobné údaje (mená, e-maily, telefóny) sa použijú len na prevod; dajú sa pred odoslaním aj anonymizovať.
- Nepridávajú sa žiadne nové knižnice do programu – analýza a prevod prebehnú mimo EviTA.
