#!/usr/bin/env python3
"""
Texty na preklad programu EviTA.

  python nastroje/preklady.py kluce        – vypíše všetky texty na preklad (slovenské kľúče)
  python nastroje/preklady.py kontrola     – skontroluje, či slovníky Preklady/*.json obsahujú všetky texty
                                             (a či preklady zachovali zástupné znaky {0}, {1:0.0} …)
  python nastroje/preklady.py chybajuce xx – vypíše texty, ktoré chýbajú v slovníku jazyka xx (JSON na doplnenie)

Zdrojom textov sú volania Preklad.T("…"), Preklad.F($"…"), Preklad.N("…"), Preklad.Pocet(n, "…", "…", "…")
v C# a pevné texty v XAML (Text, Content, Header, ToolTip, Title, StringFormat …).
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

KOREN = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Evita.App'))
PREKLADY = os.path.join(KOREN, 'Preklady')
JAZYKY = ['en', 'cs', 'pl', 'hu', 'ru', 'zh']
VYNECHANE_SUBORY = {'Samotest.cs'}


# ---------------------------------------------------------------- lexikálny rozbor C#

class Retazec:
    """Reťazcový literál v zdrojovom texte."""

    def __init__(self, zaciatok, koniec, druh, hodnota, format=None, diery=None):
        self.zaciatok = zaciatok      # index prvého znaku literálu (vrátane $ @)
        self.koniec = koniec          # index za posledným znakom
        self.druh = druh              # 'obyc', 'verb', 'surovy', 'interp'
        self.hodnota = hodnota        # hodnota (pri interpolovanom formát s {0} …)
        self.format = format          # pri interpolovanom: formát ako FormattableString.Format
        self.diery = diery or []      # pri interpolovanom: zoznam (zaciatok, koniec) výrazov v dierach


def _escape(s, i):
    """Spracuje escape sekvenciu v obyčajnom reťazci od s[i] == '\\'. Vráti (znak, nový index)."""
    c = s[i + 1]
    mapa = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '\\': '\\', '"': '"', "'": "'", 'a': '\a', 'b': '\b', 'f': '\f', 'v': '\v', 'e': '\x1b'}
    if c in mapa:
        return mapa[c], i + 2
    if c == 'u':
        return chr(int(s[i + 2:i + 6], 16)), i + 6
    if c == 'U':
        return chr(int(s[i + 2:i + 10], 16)), i + 10
    if c == 'x':
        j = i + 2
        while j < len(s) and j < i + 6 and s[j] in '0123456789abcdefABCDEF':
            j += 1
        return chr(int(s[i + 2:j], 16)), j
    return c, i + 2


def _preskoc_vyraz(s, i, koncove):
    """Preskočí výraz v diere interpolovaného reťazca až po znak z `koncove` na najvyššej úrovni."""
    hlbka = 0
    while i < len(s):
        c = s[i]
        if hlbka == 0 and c in koncove:
            return i
        if c in '([{':
            hlbka += 1
        elif c in ')]}':
            hlbka -= 1
        elif c == '"' or (c in '$@' and i + 1 < len(s) and s[i + 1] in '"$@'):
            r = _citaj_retazec(s, i)
            if r is not None:
                i = r.koniec
                continue
        elif c == "'":
            i = _preskoc_znak(s, i)
            continue
        i += 1
    raise ValueError('neukončená diera v interpolovanom reťazci')


def _preskoc_znak(s, i):
    j = i + 1
    if s[j] == '\\':
        j += 2
        while s[j] != "'":
            j += 1
    else:
        j += 1
    return j + 1


def _citaj_retazec(s, i):
    """Prečíta reťazcový literál od indexu i (", @", $", $@", @$", \"\"\"). Vráti Retazec alebo None."""
    j = i
    dolar = 0
    zavinac = False
    while j < len(s) and s[j] in '$@':
        if s[j] == '$':
            dolar += 1
        else:
            zavinac = True
        j += 1
    if j >= len(s) or s[j] != '"':
        return None
    # surový reťazec """
    if s.startswith('"""', j):
        k = j
        while k < len(s) and s[k] == '"':
            k += 1
        pocet = k - j
        uzaver = '"' * pocet
        koniec = s.index(uzaver, k)
        obsah = s[k:koniec]
        # odsadenie viacriadkového surového reťazca
        if '\n' in obsah:
            riadky = obsah.split('\n')
            posledny = riadky[-1]
            odsadenie = len(posledny) - len(posledny.lstrip())
            riadky = riadky[1:-1]
            obsah = '\n'.join(r[odsadenie:] if r.strip() else '' for r in riadky)
        r = Retazec(i, koniec + pocet, 'surovy', obsah)
        if dolar:
            r.druh = 'interp'
            r.format = None  # surové interpolované reťazce sa neprekladajú
        return r
    if dolar:
        return _citaj_interpolovany(s, i, j, zavinac)
    k = j + 1
    hodnota = []
    if zavinac:
        while True:
            if s[k] == '"':
                if k + 1 < len(s) and s[k + 1] == '"':
                    hodnota.append('"')
                    k += 2
                    continue
                break
            hodnota.append(s[k])
            k += 1
        return Retazec(i, k + 1, 'verb', ''.join(hodnota))
    while s[k] != '"':
        if s[k] == '\\':
            znak, k = _escape(s, k)
            hodnota.append(znak)
        else:
            hodnota.append(s[k])
            k += 1
    return Retazec(i, k + 1, 'obyc', ''.join(hodnota))


def _citaj_interpolovany(s, i, j, zavinac):
    k = j + 1
    format_ = []
    diery = []
    index = 0
    while True:
        c = s[k]
        if c == '"':
            if zavinac and k + 1 < len(s) and s[k + 1] == '"':
                format_.append('"')
                k += 2
                continue
            break
        if c == '{':
            if s[k + 1] == '{':
                format_.append('{{')
                k += 2
                continue
            zac = k + 1
            kon = _preskoc_vyraz(s, zac, ',:}')
            diery.append((zac, kon))
            zarovnanie = ''
            specifikator = ''
            m = kon
            if s[m] == ',':
                n = _preskoc_vyraz(s, m + 1, ':}')
                zarovnanie = ',' + s[m + 1:n].strip()
                m = n
            if s[m] == ':':
                n = s.index('}', m)
                specifikator = s[m:n]
                m = n
            format_.append('{' + str(index) + zarovnanie + specifikator + '}')
            index += 1
            k = m + 1
            continue
        if c == '}':
            format_.append('}}')
            k += 2 if s[k + 1] == '}' else 1
            continue
        if c == '\\' and not zavinac:
            znak, k = _escape(s, k)
            format_.append(znak)
            continue
        format_.append(c)
        k += 1
    f = ''.join(format_)
    return Retazec(i, k + 1, 'interp', f, format=f, diery=diery)


def retazce(s):
    """Všetky reťazcové literály v zdrojovom texte C# (vrátane tých vnútri dier interpolovaných reťazcov)."""
    vysledok = []
    i = 0
    n = len(s)
    while i < n:
        c = s[i]
        if s.startswith('//', i):
            i = s.find('\n', i)
            if i < 0:
                break
            continue
        if s.startswith('/*', i):
            i = s.index('*/', i) + 2
            continue
        if c == "'":
            i = _preskoc_znak(s, i)
            continue
        if c == '"' or (c in '$@' and i + 1 < n and s[i + 1] in '"$@'):
            r = _citaj_retazec(s, i)
            if r is not None:
                vysledok.append(r)
                for zac, kon in r.diery:
                    for vnoreny in retazce(s[zac:kon]):
                        vnoreny.zaciatok += zac
                        vnoreny.koniec += zac
                        vysledok.append(vnoreny)
                i = r.koniec
                continue
        i += 1
    return vysledok


# ---------------------------------------------------------------- texty na preklad

VOLANIA = re.compile(r'(?:Preklad\.)?\b(T|F|N|TAkJe|Pocet)\(\s*$')


def kluce_cs(cesta):
    """Kľúče z volaní Preklad.T/F/N/Pocet v súbore C#. Vráti zoznam (kľúč, riadok)."""
    s = open(cesta, encoding='utf-8-sig').read()
    vysledok = []
    zoznam = retazce(s)
    for r in zoznam:
        pred = s[max(0, r.zaciatok - 40):r.zaciatok]
        m = VOLANIA.search(pred)
        if not m or m.group(1) in ('TAkJe', 'Pocet'):
            continue
        text = r.format if r.druh == 'interp' else r.hodnota
        if text is not None:
            vysledok.append((text, s.count('\n', 0, r.zaciatok) + 1))
    # Pocet(n, "{0} položka", "{0} položky", "{0} položiek")
    for m in re.finditer(r'\bPocet\(', s):
        hlbka = 0
        i = m.end()
        vnutri = []
        while i < len(s):
            lit = next((r for r in zoznam if r.zaciatok == i), None)
            if lit is not None:
                if hlbka == 0:
                    vnutri.append(lit)
                i = lit.koniec
                continue
            if s[i] == '(':
                hlbka += 1
            elif s[i] == ')':
                if hlbka == 0:
                    break
                hlbka -= 1
            i += 1
        if len(vnutri) == 3 and all(t.druh in ('obyc', 'verb') for t in vnutri):
            vysledok.append(('|'.join(t.hodnota for t in vnutri), s.count('\n', 0, m.start()) + 1))
    return vysledok


XAML_ATRIBUTY = {'Text', 'Content', 'Header', 'ToolTip', 'Title', 'Description'}
VAZBA_FORMAT = re.compile(r"(StringFormat|TargetNullValue|FallbackValue)\s*=\s*('(?:[^']*)'|[^,}]+(?:\{[^}]*\}[^,}]*)*)")


def _ma_pismeno(text):
    return any(z.isalpha() for z in text)


def kluce_xaml(cesta):
    vysledok = []
    obsah = open(cesta, encoding='utf-8-sig').read()
    for m in re.finditer(r'<([\w:.]+)((?:\s+[\w:.]+\s*=\s*"[^"]*")*)\s*/?>', obsah):
        prvok = m.group(1)
        riadok = obsah.count('\n', 0, m.start()) + 1
        for a in re.finditer(r'([\w:.]+)\s*=\s*"([^"]*)"', m.group(2)):
            nazov, hodnota = a.group(1), a.group(2)
            hodnota = (hodnota.replace('&#xA;', '\n').replace('&#x0a;', '\n').replace('&quot;', '"').replace('&lt;', '<')
                       .replace('&gt;', '>').replace('&apos;', "'").replace('&amp;', '&'))
            if hodnota.startswith('{}'):
                hodnota = hodnota[2:]
            elif hodnota.startswith('{'):
                # väzba – formát a náhradné texty
                for f in VAZBA_FORMAT.finditer(hodnota):
                    text = f.group(2).strip()
                    if text.startswith("'") and text.endswith("'"):
                        text = text[1:-1]
                    if text.startswith('{}'):
                        text = text[2:]
                    if _ma_pismeno(text):
                        vysledok.append((text, riadok))
                continue
            kratky = nazov.split('.')[-1]
            if kratky in XAML_ATRIBUTY or (prvok in ('Setter',) and nazov == 'Value' and False):
                if _ma_pismeno(hodnota) and not re.fullmatch(r'&#x[0-9A-Fa-f]+;', a.group(2)):
                    vysledok.append((hodnota, riadok))
    return vysledok


def vsetky_kluce():
    """Slovník kľúč → zoznam miest výskytu."""
    kluce = {}
    for koren, _, subory in os.walk(KOREN):
        if os.sep + 'obj' in koren or os.sep + 'bin' in koren:
            continue
        for nazov in sorted(subory):
            cesta = os.path.join(koren, nazov)
            rel = os.path.relpath(cesta, KOREN)
            if nazov in VYNECHANE_SUBORY:
                continue
            if nazov.endswith('.cs'):
                zoznam = kluce_cs(cesta)
            elif nazov.endswith('.xaml'):
                zoznam = kluce_xaml(cesta)
            else:
                continue
            for text, riadok in zoznam:
                if not _ma_pismeno(text.replace('|', '')):
                    continue
                kluce.setdefault(text, []).append(f'{rel}:{riadok}')
    return kluce


# ---------------------------------------------------------------- kontrola slovníkov

ZASTUPNY = re.compile(r'\{\d+(?:,[^}:]*)?(?::[^}]*)?\}')


def zastupne(text):
    return sorted(ZASTUPNY.findall(text))


def nacitaj_slovnik(kod):
    cesta = os.path.join(PREKLADY, kod + '.json')
    if not os.path.exists(cesta):
        return {}
    return json.load(open(cesta, encoding='utf-8'))


def kontrola():
    kluce = vsetky_kluce()
    chyby = 0
    for kod in JAZYKY:
        slovnik = nacitaj_slovnik(kod)
        chybajuce = [k for k in kluce if not slovnik.get(k)]
        nadbytocne = [k for k in slovnik if k not in kluce]
        zle = []
        for k, v in slovnik.items():
            if not v or k not in kluce:
                continue
            if '|' in k and k.count('|') == 2:
                tvary = v.split('|')
                if any(zastupne(t) != zastupne(k.split('|')[0]) for t in tvary):
                    zle.append(k)
            elif zastupne(k) != zastupne(v):
                zle.append(k)
        print(f'{kod}: {len(slovnik)} prekladov, chýba {len(chybajuce)}, nadbytočné {len(nadbytocne)}, chybné zástupné znaky {len(zle)}')
        for k in chybajuce[:30]:
            print(f'   chýba: {k!r}  ({kluce[k][0]})')
        for k in zle[:30]:
            print(f'   zástupné znaky: {k!r} → {slovnik[k]!r}')
        for k in nadbytocne[:10]:
            print(f'   nadbytočné: {k!r}')
        chyby += len(chybajuce) + len(zle)
    return chyby


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    prikaz = sys.argv[1]
    if prikaz == 'kluce':
        kluce = vsetky_kluce()
        for k, miesta in kluce.items():
            print(json.dumps(k, ensure_ascii=False), '  #', miesta[0], f'(+{len(miesta) - 1})' if len(miesta) > 1 else '')
        print(len(kluce), 'textov', file=sys.stderr)
        return 0
    if prikaz == 'kontrola':
        return 1 if kontrola() else 0
    if prikaz == 'chybajuce':
        kod = sys.argv[2]
        slovnik = nacitaj_slovnik(kod)
        kluce = vsetky_kluce()
        chybajuce = {k: '' for k in kluce if not slovnik.get(k)}
        print(json.dumps(chybajuce, ensure_ascii=False, indent=2))
        return 0
    print(__doc__)
    return 1


if __name__ == '__main__':
    sys.exit(main())
