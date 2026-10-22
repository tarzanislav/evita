/*
 * EviTA – prepis diktovania do textu bez internetu (whisper.cpp, licencia MIT).
 * Tenká vrstva s jednoduchým rozhraním pre C#: otvoriť model, prepísať zvuk, zavrieť.
 * Nič neposiela do siete a nečíta nič okrem zadaného súboru modelu.
 */
#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include "whisper.h"

#define EVITA_API __declspec(dllexport)

static size_t citaj(void * ctx, void * vystup, size_t pocet) { return fread(vystup, 1, pocet, (FILE *)ctx); }
static bool koniec(void * ctx) { return feof((FILE *)ctx) != 0; }
static void zavri_subor(void * ctx) { fclose((FILE *)ctx); }
static void ticho(enum ggml_log_level uroven, const char * text, void * data) { (void)uroven; (void)text; (void)data; }

/* Otvorí model (cesta v UTF-16, aby fungovala aj s diakritikou). Vráti NULL pri chybe. */
EVITA_API void * evita_hlas_otvor(const wchar_t * cesta)
{
    whisper_log_set(ticho, NULL);
    FILE * subor = _wfopen(cesta, L"rb");
    if (subor == NULL) {
        return NULL;
    }

    struct whisper_model_loader nacitavac;
    nacitavac.context = subor;
    nacitavac.read = citaj;
    nacitavac.eof = koniec;
    nacitavac.close = zavri_subor;

    struct whisper_context_params parametre = whisper_context_default_params();
    parametre.use_gpu = false;
    return whisper_init_with_params(&nacitavac, parametre);
}

/*
 * Prepíše zvuk (16 kHz, mono, float -1..1) v zadanom jazyku ("sk"). Výsledok v UTF-8 do vystup.
 * Vráti dĺžku textu alebo -1 pri chybe.
 */
EVITA_API int evita_hlas_prepis(void * kontext, const float * zvuk, int vzoriek, const char * jazyk, int vlakna,
                                const char * vyzva, char * vystup, int velkost)
{
    if (kontext == NULL || zvuk == NULL || vystup == NULL || velkost < 1) {
        return -1;
    }

    struct whisper_context * ctx = (struct whisper_context *)kontext;
    struct whisper_full_params p = whisper_full_default_params(WHISPER_SAMPLING_BEAM_SEARCH);
    p.n_threads = vlakna > 0 ? vlakna : 4;
    p.language = jazyk;
    p.detect_language = false;
    p.translate = false;
    p.no_timestamps = true;
    p.print_special = false;
    p.print_progress = false;
    p.print_realtime = false;
    p.print_timestamps = false;
    p.suppress_nst = true;
    p.initial_prompt = (vyzva != NULL && vyzva[0] != 0) ? vyzva : NULL;

    vystup[0] = 0;
    if (whisper_full(ctx, p, zvuk, vzoriek) != 0) {
        return -1;
    }

    size_t pozicia = 0;
    const int segmentov = whisper_full_n_segments(ctx);
    for (int i = 0; i < segmentov; i++) {
        const char * text = whisper_full_get_segment_text(ctx, i);
        const size_t dlzka = strlen(text);
        if (pozicia + dlzka + 1 >= (size_t)velkost) {
            break;
        }

        memcpy(vystup + pozicia, text, dlzka);
        pozicia += dlzka;
        vystup[pozicia] = 0;
    }

    return (int)pozicia;
}

EVITA_API void evita_hlas_zavri(void * kontext)
{
    if (kontext != NULL) {
        whisper_free((struct whisper_context *)kontext);
    }
}
