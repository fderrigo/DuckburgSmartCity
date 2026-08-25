using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Cms;

/// <summary>
/// Popola il database con i contenuti di default in stile Paperopoli, allineati
/// all'architettura dell'informazione del modello Comuni (misura 1.4.1, pacchetto
/// Cittadino Informato). Tutti i contenuti sono marcati IsDefault=true.
/// Idempotente: agisce solo su DB vuoto.
/// </summary>
public sealed class CmsSeeder
{
    private readonly CmsDbContext _db;
    private readonly IConfiguration _cfg;

    public CmsSeeder(CmsDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    /// <summary>Indirizzo di un altro portale della soluzione, con ripiego locale.</summary>
    private string Sito(string chiave, string locale) =>
        _cfg[$"Siti:{chiave}"] is { Length: > 0 } v ? v : locale;

    public async Task SeedAsync()
    {
        if (await _db.Argomenti.AnyAsync() || await _db.Servizi.AnyAsync()) return;

        await SeedImpostazioni();
        var argomenti = await SeedArgomenti();
        var categorie = await SeedCategorie();
        var uffici = await SeedUffici();
        await SeedServizi(argomenti, categorie, uffici);
        await SeedNovita(argomenti, uffici);
        await SeedPersone();
        await SeedLuoghi();
        await SeedEventi();
        await SeedDocumenti(uffici);
        await SeedPagine();
        await SeedMenu();
        await SeedFaq();
    }

    private static Servizio Def(Servizio s) { s.IsDefault = true; s.IsPublished = true; return s; }

    private async Task SeedImpostazioni()
    {
        var s = new (string k, string v, string gruppo, string etichetta)[]
        {
            ("ente.nome", "Comune di Paperopoli", "Ente", "Nome dell'ente"),
            ("ente.eyebrow", "Portale dei servizi al cittadino", "Ente", "Sopratitolo testata"),
            ("ente.regione", "Regione Palmipedia", "Ente", "Regione di appartenenza"),
            ("ente.patrono", "San Quaquaraqua", "Ente", "Santo patrono"),
            ("ente.indirizzo", "Palazzo Comunale — via dei Dobloni 13", "Ente", "Indirizzo sede"),
            ("ente.quartiere", "Quartiere Dogburg, Paperopoli", "Ente", "Quartiere"),
            ("ente.cf", "00000000313 (fittizio)", "Ente", "Codice fiscale"),
            ("contatti.pec", "protocollo@pec.paperopoli.demo", "Contatti", "PEC"),
            ("contatti.urp", "urp@paperopoli.demo", "Contatti", "Email URP"),
            ("contatti.numeroverde", "800-PAPERINO", "Contatti", "Numero verde"),
            ("contatti.disservizi", "disservizi@paperopoli.demo", "Contatti", "Email segnalazione disservizi"),
            ("orari.riga1", "Lun–Ven 9:00–12:30", "Orari", "Orari sportelli riga 1"),
            ("orari.riga2", "Mar e gio anche 15:00–17:00", "Orari", "Orari sportelli riga 2"),
            ("orari.riga3", "Ufficio Tributi: mar e gio su appuntamento", "Orari", "Orari sportelli riga 3"),
            ("hero.titolo", "Il Comune ti risponde!", "Home", "Titolo hero"),
            ("hero.sottotitolo", "Tributi, anagrafe, rifiuti, scuola: chiedi all'assistente virtuale e ricevi risposte tratte solo dalle fonti ufficiali del Comune.", "Home", "Sottotitolo hero"),
            ("hero.placeholder", "Es. Quando scade la prima rata della TARI?", "Home", "Placeholder domanda hero"),
        };
        _db.Impostazioni.AddRange(s.Select((x, i) => new Impostazione
        {
            Chiave = x.k, Valore = x.v, Gruppo = x.gruppo, Etichetta = x.etichetta,
            Slug = x.k, IsDefault = true, Ordine = i
        }));
        await _db.SaveChangesAsync();
    }

    /// <summary>Argomenti dalla Tassonomia argomenti del modello Comuni (C.SI.1.5).</summary>
    private async Task<Dictionary<string, Argomento>> SeedArgomenti()
    {
        // Nomi esatti dalla Tassonomia argomenti del modello Comuni (verificati dall'App).
        var data = new (string slug, string nome, string desc, string colore)[]
        {
            ("imposte", "Imposte", "TARI, IMU, addizionale comunale e agevolazioni.", "oro"),
            ("gestione-rifiuti", "Gestione rifiuti", "Raccolta differenziata, conferimento e decoro urbano.", "acqua"),
            ("anagrafe", "Anagrafe", "Carta d'identità, certificati, residenza e stato civile.", "rossa"),
            ("istruzione", "Istruzione", "Mensa, trasporto scolastico, nidi e diritto allo studio.", "acqua"),
            ("tassa-sui-servizi", "Tassa sui servizi", "pagoPA, avvisi e canali di pagamento verso il Comune.", "oro"),
            ("assistenza-sociale", "Assistenza sociale", "Contributi, sostegno alle famiglie e servizi alla persona.", "rossa"),
            ("mobilita-sostenibile", "Mobilità sostenibile", "Viabilità, parcheggi, ZTL e trasporti.", "acqua"),
            ("patrimonio-culturale", "Patrimonio culturale", "Biblioteca, eventi e cultura del territorio.", "oro"),
        };
        var map = new Dictionary<string, Argomento>();
        int i = 0;
        foreach (var d in data)
        {
            var a = new Argomento { Slug = d.slug, Nome = d.nome, Descrizione = d.desc, Colore = d.colore, IsDefault = true, Ordine = i++ };
            map[d.slug] = a;
            _db.Argomenti.Add(a);
        }
        await _db.SaveChangesAsync();
        return map;
    }

    /// <summary>Categorie di servizio dal vocabolario del modello Comuni (C.SI.1.7).</summary>
    private async Task<Dictionary<string, CategoriaServizio>> SeedCategorie()
    {
        var nomi = new (string slug, string nome, string desc)[]
        {
            ("educazione-e-formazione", "Educazione e formazione", "Servizi per nidi, scuole, mense e trasporto scolastico."),
            ("salute-benessere-e-assistenza", "Salute, benessere e assistenza", "Sostegni economici e servizi alla persona."),
            ("vita-lavorativa", "Vita lavorativa", "Servizi per chi lavora o cerca lavoro."),
            ("mobilita-e-trasporti", "Mobilità e trasporti", "Viabilità, parcheggi, ZTL e trasporto pubblico."),
            ("catasto-e-urbanistica", "Catasto e urbanistica", "Pratiche edilizie, catasto e pianificazione."),
            ("anagrafe-e-stato-civile", "Anagrafe e stato civile", "Documenti d'identità, certificati e residenza."),
            ("turismo", "Turismo", "Accoglienza e informazioni turistiche."),
            ("giustizia-e-sicurezza-pubblica", "Giustizia e sicurezza pubblica", "Polizia locale e sicurezza del territorio."),
            ("tributi-finanze-e-contravvenzioni", "Tributi, finanze e contravvenzioni", "TARI, IMU, pagamenti e contravvenzioni."),
            ("cultura-e-tempo-libero", "Cultura e tempo libero", "Biblioteca, eventi, sport e associazioni."),
            ("ambiente", "Ambiente", "Rifiuti, verde pubblico e qualità dell'ambiente."),
            ("imprese-e-commercio", "Imprese e commercio", "Attività produttive e commercio."),
            ("autorizzazioni", "Autorizzazioni", "Permessi e autorizzazioni comunali."),
            ("appalti-pubblici", "Appalti pubblici", "Gare, bandi e contratti pubblici."),
            ("agricoltura-e-pesca", "Agricoltura e pesca", "Servizi per il settore agricolo e ittico."),
        };
        var map = new Dictionary<string, CategoriaServizio>();
        int i = 0;
        foreach (var n in nomi)
        {
            var c = new CategoriaServizio { Slug = n.slug, Nome = n.nome, Descrizione = n.desc, IsDefault = true, Ordine = i++ };
            map[n.slug] = c;
            _db.CategorieServizio.Add(c);
        }
        await _db.SaveChangesAsync();
        return map;
    }

    private async Task<Dictionary<string, UnitaOrganizzativa>> SeedUffici()
    {
        var data = new UnitaOrganizzativa[]
        {
            new() { Slug = "tributi", Nome = "Ufficio Tributi", Tipo = TipoUnita.Ufficio,
                Descrizione = "Si occupa di TARI, IMU e addizionale comunale.",
                Competenze = new() { "Gestione TARI e IMU", "Addizionale comunale", "Rateizzazioni e rimborsi", "Agevolazioni tributarie" },
                Sede = "via dei Dobloni 13, Paperopoli", Orari = "Martedì e giovedì 9:00–12:30, su appuntamento",
                Telefono = "Numero verde 800-PAPERINO", Email = "tributi@paperopoli.demo", Pec = "tributi@pec.paperopoli.demo",
                Responsabile = "Bartolo Beccogialli", Prenotabile = true },
            new() { Slug = "anagrafe", Nome = "Anagrafe e stato civile", Tipo = TipoUnita.Ufficio,
                Descrizione = "Carta d'identità elettronica, certificati e cambi di residenza.",
                Competenze = new() { "Rilascio CIE", "Certificati anagrafici", "Cambi di residenza", "Stato civile" },
                Sede = "Palazzo Comunale, via dei Dobloni 13", Orari = "Lun–ven 9:00–12:30, mar e gio anche 15:00–17:00",
                Telefono = "Numero verde 800-PAPERINO", Email = "anagrafe@paperopoli.demo", Pec = "anagrafe@pec.paperopoli.demo",
                Responsabile = "Ufficio Anagrafe", Prenotabile = true },
            new() { Slug = "urp", Nome = "URP — Relazioni con il pubblico", Tipo = TipoUnita.Ufficio,
                Descrizione = "Primo punto di contatto per ogni pratica del cittadino.",
                Competenze = new() { "Informazioni al cittadino", "Raccolta segnalazioni", "Prenotazione appuntamenti" },
                Sede = "via dei Dobloni 13, Paperopoli", Orari = "Lun–ven 9:00–12:30",
                Telefono = "Numero verde 800-PAPERINO", Email = "urp@paperopoli.demo", Pec = "protocollo@pec.paperopoli.demo",
                Responsabile = "Ufficio URP", Prenotabile = true },
            new() { Slug = "ambiente", Nome = "Ufficio Ambiente", Tipo = TipoUnita.Ufficio,
                Descrizione = "Raccolta differenziata, igiene urbana e verde pubblico.",
                Competenze = new() { "Calendario porta a porta", "Kit raccolta differenziata", "Verde pubblico e parchi" },
                Sede = "via dei Dobloni 13, Paperopoli", Orari = "Lun, mer, ven 9:00–12:30",
                Telefono = "Numero verde 800-PAPERINO", Email = "ambiente@paperopoli.demo", Pec = "ambiente@pec.paperopoli.demo",
                Responsabile = "Ines Tuffetti", Prenotabile = true },
            new() { Slug = "scuola", Nome = "Ufficio Scuola", Tipo = TipoUnita.Ufficio,
                Descrizione = "Mensa, trasporto scolastico, nidi e diritto allo studio.",
                Competenze = new() { "Iscrizioni mensa e scuolabus", "Tariffe ISEE", "Bonus nido comunale" },
                Sede = "via dei Dobloni 13, Paperopoli", Orari = "Lun–ven 9:00–12:30",
                Telefono = "Numero verde 800-PAPERINO", Email = "scuola@paperopoli.demo", Pec = "scuola@pec.paperopoli.demo",
                Responsabile = "Clara Pennadoro", Prenotabile = true },
            new() { Slug = "polizia-locale", Nome = "Polizia locale", Tipo = TipoUnita.Ufficio,
                Descrizione = "Viabilità, ZTL, passi carrabili e sicurezza urbana.",
                Competenze = new() { "Permessi ZTL", "Passi carrabili", "Occupazione suolo pubblico", "Contravvenzioni" },
                Sede = "piazza del Doblone 1, Paperopoli", Orari = "Lun–sab 8:00–13:00",
                Telefono = "Numero verde 800-PAPERINO", Email = "polizialocale@paperopoli.demo", Pec = "polizialocale@pec.paperopoli.demo",
                Responsabile = "Comandante Gaia Palmipede", Prenotabile = true },
        };
        var map = new Dictionary<string, UnitaOrganizzativa>();
        int i = 0;
        foreach (var u in data) { u.IsDefault = true; u.Ordine = i++; map[u.Slug] = u; _db.Unita.Add(u); }
        await _db.SaveChangesAsync();
        return map;
    }

    private async Task SeedServizi(
        Dictionary<string, Argomento> arg,
        Dictionary<string, CategoriaServizio> cat,
        Dictionary<string, UnitaOrganizzativa> uff)
    {
        const string condizioniStd = "Il servizio è erogato secondo il regolamento comunale vigente. I dati forniti sono trattati ai soli fini del procedimento.";
        var lista = new List<Servizio>
        {
            Def(new Servizio
            {
                Slug = "tari", Titolo = "TARI — Tassa sui rifiuti",
                Sottotitolo = "La tassa annuale sui rifiuti del Comune di Paperopoli: chi la paga, quando e come.",
                DescrizioneBreve = "Chi la paga, le tre rate, le tariffe e le riduzioni. Scheda completa del servizio.",
                AChiERivolto = "A tutti coloro che possiedono o detengono locali o aree che producono rifiuti, a qualsiasi titolo, sul territorio comunale.",
                Descrizione = "La TARI è la tassa comunale destinata a finanziare i costi del servizio di raccolta e smaltimento dei rifiuti urbani.",
                ComeFare = "Il pagamento avviene tramite avviso pagoPA: online dal portale, dall'app dei pagamenti, oppure presso banche, tabaccai e uffici postali.",
                CosaServe = "Avviso pagoPA ricevuto dal Comune e, per le variazioni, i dati catastali dell'immobile.",
                CosaSiOttiene = "Il regolare pagamento della tassa sui rifiuti e l'eventuale applicazione di riduzioni e agevolazioni.",
                Tempi = "Tre rate nell'anno; pagamento in unica soluzione ammesso entro la scadenza della prima rata.",
                Costi = "Per le utenze domestiche la tariffa si compone di una quota fissa di 0,98 euro al metro quadro all'anno e di una quota variabile che dipende dal numero di occupanti (48 euro all'anno per un nucleo di una persona). Pagamento tramite avviso pagoPA.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/regolamento-tari",
                Scadenze = new() { "30 aprile — Prima rata", "31 luglio — Seconda rata", "2 dicembre — Terza rata (saldo)" },
                Fonti = new() { "tari:p01 · v1.0", "tari:p02 · v1.0", "tari:p03 · v1.0" },
                IconaKey = "tari", ColoreIcona = "oro", InEvidenza = true, Ordine = 0,
                CategoriaId = cat["tributi-finanze-e-contravvenzioni"].Id,
                ArgomentoId = arg["imposte"].Id, UnitaOrganizzativaId = uff["tributi"].Id
            }),
            Def(new Servizio
            {
                Slug = "raccolta-differenziata", Titolo = "Raccolta differenziata",
                Sottotitolo = "Il porta a porta di Paperopoli, quartiere per quartiere.",
                DescrizioneBreve = "Calendario porta a porta per quartiere e regole di conferimento.",
                AChiERivolto = "A tutte le utenze domestiche e non domestiche del territorio comunale.",
                Descrizione = "Il servizio di raccolta differenziata porta a porta ritira i rifiuti direttamente presso le utenze secondo un calendario per quartiere.",
                ComeFare = "Esponi il rifiuto corretto entro le 6:00 del giorno di raccolta, negli appositi contenitori o sacchi.",
                CosaServe = "Il kit di contenitori e sacchi consegnato dall'Ufficio Ambiente e il calendario del proprio quartiere.",
                CosaSiOttiene = "Un corretto conferimento dei rifiuti e il contributo alla percentuale di differenziata del Comune.",
                Tempi = "Ritiro settimanale secondo calendario di quartiere.",
                Costi = "Servizio incluso nella TARI, nessun costo aggiuntivo.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/calendario-differenziata",
                Scadenze = new(), Fonti = new() { "ambiente:p01 · v1.0" },
                IconaKey = "acqua", ColoreIcona = "acqua", InEvidenza = true, Ordine = 1,
                CategoriaId = cat["ambiente"].Id,
                ArgomentoId = arg["gestione-rifiuti"].Id, UnitaOrganizzativaId = uff["ambiente"].Id
            }),
            Def(new Servizio
            {
                Slug = "carta-identita", Titolo = "Carta d'identità elettronica",
                Sottotitolo = "Rilascio e rinnovo della CIE su appuntamento.",
                DescrizioneBreve = "CIE su appuntamento, certificati online e cambi di residenza.",
                AChiERivolto = "A tutti i cittadini residenti nel Comune di Paperopoli.",
                Descrizione = "L'Ufficio Anagrafe rilascia la carta d'identità elettronica (CIE), documento di riconoscimento e strumento di accesso ai servizi digitali.",
                ComeFare = "Prenota un appuntamento presso l'Ufficio Anagrafe; il giorno dell'appuntamento porta la documentazione richiesta.",
                CosaServe = "Documento di riconoscimento scaduto o in scadenza, codice fiscale e una fototessera recente.",
                CosaSiOttiene = "La carta d'identità elettronica, recapitata all'indirizzo indicato.",
                Tempi = "La CIE viene recapitata entro 6 giorni lavorativi dalla richiesta.",
                Costi = "22,00 euro per il rilascio (diritti inclusi), da pagare allo sportello anche con pagoPA.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new(), Fonti = new() { "anagrafe:p01 · v1.0" },
                IconaKey = "rossa", ColoreIcona = "rossa", InEvidenza = true, Ordine = 2,
                CategoriaId = cat["anagrafe-e-stato-civile"].Id,
                ArgomentoId = arg["anagrafe"].Id, UnitaOrganizzativaId = uff["anagrafe"].Id
            }),
            Def(new Servizio
            {
                Slug = "imu", Titolo = "IMU — Imposta municipale propria",
                Sottotitolo = "Aliquote, scadenze ed esenzioni dell'imposta sugli immobili.",
                DescrizioneBreve = "Aliquote, acconto 16 giugno e saldo 16 dicembre, esenzioni.",
                AChiERivolto = "Ai possessori di immobili diversi dall'abitazione principale non di lusso.",
                Descrizione = "L'IMU è l'imposta comunale sul possesso di immobili. L'abitazione principale non di lusso è esente.",
                ComeFare = "Calcola l'imposta in base ad aliquote e rendita catastale e paga con modello F24.",
                CosaServe = "Dati catastali degli immobili e aliquote deliberate dal Comune.",
                CosaSiOttiene = "Il regolare versamento dell'imposta municipale.",
                Tempi = "Due rate: acconto entro il 16 giugno e saldo entro il 16 dicembre.",
                Costi = "L'importo dipende da rendita catastale e aliquota deliberata. Pagamento con modello F24.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/regolamento-tari",
                Scadenze = new() { "16 giugno — Acconto", "16 dicembre — Saldo" },
                Fonti = new() { "imu:p01 · v1.0" },
                IconaKey = "casa", ColoreIcona = "oro", InEvidenza = true, Ordine = 3,
                CategoriaId = cat["tributi-finanze-e-contravvenzioni"].Id,
                ArgomentoId = arg["imposte"].Id, UnitaOrganizzativaId = uff["tributi"].Id
            }),
            Def(new Servizio
            {
                Slug = "mensa-trasporto-scolastico", Titolo = "Mensa e trasporto scolastico",
                Sottotitolo = "Iscrizioni, tariffe per fascia ISEE e linee dello scuolabus.",
                DescrizioneBreve = "Iscrizioni, tariffe ISEE e linee dello scuolabus.",
                AChiERivolto = "Alle famiglie con figli iscritti alle scuole del territorio comunale.",
                Descrizione = "Il Comune gestisce la mensa scolastica e il trasporto con scuolabus, con tariffe agevolate per fascia ISEE.",
                ComeFare = "Presenta la domanda online entro le scadenze annuali allegando l'attestazione ISEE.",
                CosaServe = "SPID o CIE del genitore e attestazione ISEE in corso di validità.",
                CosaSiOttiene = "L'accesso al servizio mensa e/o allo scuolabus alla tariffa spettante.",
                Tempi = "Domande entro il 31 agosto per il nuovo anno scolastico.",
                Costi = "Tariffa per fascia ISEE, da 1,50 a 5,00 euro a pasto; agevolazioni per i nuclei più fragili. Pagamento con avviso pagoPA.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new() { "31 agosto — Chiusura iscrizioni" },
                Fonti = new() { "scuola:p01 · v1.0" },
                IconaKey = "bus", ColoreIcona = "acqua", InEvidenza = true, Ordine = 4,
                CategoriaId = cat["educazione-e-formazione"].Id,
                ArgomentoId = arg["istruzione"].Id, UnitaOrganizzativaId = uff["scuola"].Id
            }),
            Def(new Servizio
            {
                Slug = "pagamenti-pagopa", Titolo = "Pagamenti pagoPA",
                Sottotitolo = "Tutti i pagamenti verso il Comune con avviso pagoPA.",
                DescrizioneBreve = "Tutti i pagamenti verso il Comune con avviso pagoPA.",
                AChiERivolto = "A cittadini e imprese che devono effettuare un pagamento verso il Comune.",
                Descrizione = "pagoPA è il sistema dei pagamenti verso la pubblica amministrazione. Ogni avviso riporta un codice IUV.",
                ComeFare = "Paga online dal portale, con l'app dei pagamenti, oppure presso banche, tabaccai e uffici postali con l'avviso pagoPA.",
                CosaServe = "Avviso pagoPA con codice IUV o QR code.",
                CosaSiOttiene = "La ricevuta del pagamento effettuato.",
                Tempi = "Accredito immediato per i pagamenti online.",
                Costi = "Nessun costo aggiuntivo del Comune; possibili commissioni del canale scelto.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new(), Fonti = new() { "pagamenti:p01 · v1.0" },
                IconaKey = "pagamento", ColoreIcona = "rossa", InEvidenza = true, Ordine = 5,
                CategoriaId = cat["tributi-finanze-e-contravvenzioni"].Id,
                ArgomentoId = arg["tassa-sui-servizi"].Id, UnitaOrganizzativaId = uff["tributi"].Id
            }),
            Def(new Servizio
            {
                Slug = "contributo-affitto", Titolo = "Contributo per l'affitto",
                Sottotitolo = "Sostegno economico alle famiglie per il canone di locazione.",
                DescrizioneBreve = "Contributo comunale a sostegno del canone di locazione per i nuclei in difficoltà.",
                AChiERivolto = "Ai nuclei familiari residenti con contratto di locazione regolare e ISEE entro la soglia.",
                Descrizione = "Il contributo sostiene le famiglie in difficoltà nel pagamento del canone di affitto dell'abitazione principale.",
                ComeFare = "Presenta domanda online durante il bando annuale allegando contratto e ISEE.",
                CosaServe = "SPID o CIE, contratto di locazione registrato e attestazione ISEE.",
                CosaSiOttiene = "Un contributo economico a copertura parziale del canone annuo.",
                Tempi = "Erogazione dopo la chiusura e l'istruttoria del bando, indicativamente entro 90 giorni.",
                Costi = "Servizio gratuito.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new() { "Autunno — Pubblicazione del bando" },
                Fonti = new() { "sociale:p01 · v1.0" },
                IconaKey = "casa", ColoreIcona = "rossa", InEvidenza = false, Ordine = 6,
                CategoriaId = cat["salute-benessere-e-assistenza"].Id,
                ArgomentoId = arg["assistenza-sociale"].Id, UnitaOrganizzativaId = uff["urp"].Id
            }),
            Def(new Servizio
            {
                Slug = "passo-carrabile", Titolo = "Passo carrabile",
                Sottotitolo = "Autorizzazione all'accesso veicolare da area privata a strada pubblica.",
                DescrizioneBreve = "Richiesta, rinnovo e cessazione dell'autorizzazione per il passo carrabile.",
                AChiERivolto = "Ai proprietari o utilizzatori di accessi carrabili su strada pubblica.",
                Descrizione = "Il passo carrabile è l'autorizzazione che vieta la sosta davanti a un accesso veicolare privato, segnalata con l'apposito cartello comunale.",
                ComeFare = "Presenta la richiesta alla Polizia locale allegando planimetria dell'accesso e documento d'identità.",
                CosaServe = "Documento d'identità, planimetria o foto dell'accesso, dati catastali dell'immobile.",
                CosaSiOttiene = "L'autorizzazione e il cartello ufficiale di passo carrabile con numero di concessione.",
                Tempi = "Rilascio entro 30 giorni dalla richiesta completa.",
                Costi = "Canone annuale in base ai metri lineari dell'accesso (da 25 euro l'anno). Pagamento con avviso pagoPA.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new() { "31 gennaio — Rinnovo canone annuale" },
                Fonti = new() { "polizia:p01 · v1.0" },
                IconaKey = "casa", ColoreIcona = "acqua", InEvidenza = false, Ordine = 7,
                CategoriaId = cat["autorizzazioni"].Id,
                ArgomentoId = arg["mobilita-sostenibile"].Id, UnitaOrganizzativaId = uff["polizia-locale"].Id
            }),
            Def(new Servizio
            {
                Slug = "occupazione-suolo-pubblico", Titolo = "Occupazione di suolo pubblico",
                Sottotitolo = "Concessione per dehors, cantieri, traslochi ed eventi.",
                DescrizioneBreve = "Concessione temporanea o permanente di spazi pubblici.",
                AChiERivolto = "A cittadini, imprese e associazioni che devono occupare temporaneamente o stabilmente il suolo pubblico.",
                Descrizione = "La concessione consente l'occupazione di spazi pubblici per dehors, cantieri, traslochi, manifestazioni ed eventi.",
                ComeFare = "Presenta la domanda alla Polizia locale almeno 10 giorni prima dell'occupazione, indicando area, superficie e durata.",
                CosaServe = "Documento d'identità, planimetria dell'area richiesta, marca da bollo (fittizia nella demo).",
                CosaSiOttiene = "La concessione di occupazione con l'indicazione di superficie, durata e canone dovuto.",
                Tempi = "Rilascio entro 10 giorni per occupazioni temporanee, 30 giorni per quelle permanenti.",
                Costi = "Canone unico patrimoniale in base a superficie, durata e zona. Pagamento con avviso pagoPA.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new(), Fonti = new() { "polizia:p02 · v1.0" },
                IconaKey = "documento", ColoreIcona = "oro", InEvidenza = false, Ordine = 8,
                CategoriaId = cat["autorizzazioni"].Id,
                ArgomentoId = arg["mobilita-sostenibile"].Id, UnitaOrganizzativaId = uff["polizia-locale"].Id
            }),
            Def(new Servizio
            {
                Slug = "permesso-ztl", Titolo = "Permesso di accesso alla ZTL",
                Sottotitolo = "Accesso alla zona a traffico limitato del centro storico.",
                DescrizioneBreve = "Permessi per residenti, domiciliati e operatori nella ZTL del Doblone.",
                AChiERivolto = "Ai residenti nella ZTL, ai domiciliati, agli operatori commerciali e ai possessori di veicoli elettrici.",
                Descrizione = "La ZTL del centro storico è attiva tutti i giorni dalle 7:30 alle 19:30. Il permesso consente l'accesso e la sosta negli stalli riservati.",
                ComeFare = "Richiedi il permesso alla Polizia locale indicando targa e categoria di appartenenza; il contrassegno arriva via posta.",
                CosaServe = "Documento d'identità, libretto del veicolo, documentazione della categoria (residenza, contratto di lavoro, ecc.).",
                CosaSiOttiene = "Il permesso ZTL con validità annuale e il contrassegno da esporre.",
                Tempi = "Rilascio entro 15 giorni dalla richiesta completa.",
                Costi = "Gratuito per i residenti; 50 euro l'anno per gli operatori. Pagamento con avviso pagoPA.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new() { "31 dicembre — Scadenza permessi annuali" },
                Fonti = new() { "polizia:p03 · v1.0" },
                IconaKey = "bus", ColoreIcona = "rossa", InEvidenza = false, Ordine = 9,
                CategoriaId = cat["mobilita-e-trasporti"].Id,
                ArgomentoId = arg["mobilita-sostenibile"].Id, UnitaOrganizzativaId = uff["polizia-locale"].Id
            }),
            Def(new Servizio
            {
                Slug = "bonus-nido", Titolo = "Bonus nido comunale",
                Sottotitolo = "Contributo per la retta dei nidi d'infanzia di Paperopoli.",
                DescrizioneBreve = "Contributo comunale sulla retta del nido per fascia ISEE.",
                AChiERivolto = "Alle famiglie residenti con bambini iscritti ai nidi d'infanzia comunali o convenzionati.",
                Descrizione = "Il bonus integra le misure nazionali riducendo la retta mensile del nido in base alla fascia ISEE del nucleo.",
                ComeFare = "Presenta domanda online entro la scadenza del bando allegando l'attestazione ISEE minorenni.",
                CosaServe = "SPID o CIE del genitore, attestazione ISEE minorenni, iscrizione al nido.",
                CosaSiOttiene = "Uno sconto sulla retta mensile, applicato direttamente in bolletta.",
                Tempi = "Esito entro 30 giorni dalla chiusura del bando.",
                Costi = "Servizio gratuito.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new() { "30 settembre — Chiusura domande" },
                Fonti = new() { "scuola:p02 · v1.0" },
                IconaKey = "orologio", ColoreIcona = "acqua", InEvidenza = false, Ordine = 10,
                CategoriaId = cat["educazione-e-formazione"].Id,
                ArgomentoId = arg["istruzione"].Id, UnitaOrganizzativaId = uff["scuola"].Id
            }),
            Def(new Servizio
            {
                Slug = "cambio-residenza", Titolo = "Cambio di residenza",
                Sottotitolo = "Dichiarazione di residenza e cambio di abitazione.",
                DescrizioneBreve = "Trasferimento della residenza a Paperopoli o cambio di indirizzo.",
                AChiERivolto = "A chi trasferisce la propria dimora abituale a Paperopoli o cambia abitazione all'interno del Comune.",
                Descrizione = "La dichiarazione di residenza registra il trasferimento della dimora abituale. La registrazione avviene entro 2 giorni lavorativi.",
                ComeFare = "Presenta la dichiarazione all'Ufficio Anagrafe di persona, via PEC o tramite il servizio online nazionale.",
                CosaServe = "Documento d'identità, codice fiscale e titolo di occupazione dell'immobile (proprietà, contratto, comodato).",
                CosaSiOttiene = "La registrazione della nuova residenza e l'aggiornamento dei documenti collegati.",
                Tempi = "Registrazione entro 2 giorni lavorativi; accertamento entro 45 giorni.",
                Costi = "Servizio gratuito.",
                CondizioniServizio = condizioniStd, CondizioniServizioUrl = "/documenti/carta-servizi",
                Scadenze = new(), Fonti = new() { "anagrafe:p02 · v1.0" },
                IconaKey = "rossa", ColoreIcona = "oro", InEvidenza = false, Ordine = 11,
                CategoriaId = cat["anagrafe-e-stato-civile"].Id,
                ArgomentoId = arg["anagrafe"].Id, UnitaOrganizzativaId = uff["anagrafe"].Id
            }),
        };
        _db.Servizi.AddRange(lista);
        await _db.SaveChangesAsync();
    }

    private async Task SeedNovita(Dictionary<string, Argomento> arg, Dictionary<string, UnitaOrganizzativa> uff)
    {
        var data = new Novita[]
        {
            new() { Slug = "porta-a-porta-dogburg", Titolo = "Porta a porta: nuovo calendario nel quartiere Dogburg",
                Tipo = TipoNovita.Avviso, Data = new DateTime(2026, 7, 1),
                Sommario = "Dal 15 luglio cambia il giorno di raccolta di carta e cartone.",
                Corpo = "Dal 15 luglio la raccolta di carta e cartone nel quartiere Dogburg passa dal mercoledì al venerdì. Il calendario aggiornato è disponibile chiedendo all'assistente virtuale.",
                InEvidenza = true, ArgomentoId = arg["gestione-rifiuti"].Id, ACuraDiId = uff["ambiente"].Id },
            new() { Slug = "festa-san-quaquaraqua", Titolo = "Festa di San Quaquaraqua: variazioni degli orari",
                Tipo = TipoNovita.Notizia, Data = new DateTime(2026, 6, 24),
                Sommario = "Gli sportelli comunali resteranno chiusi nella giornata del patrono.",
                Corpo = "In occasione della festa del patrono gli sportelli comunali resteranno chiusi. Riapertura regolare il giorno successivo con i consueti orari.",
                InEvidenza = true, ACuraDiId = uff["urp"].Id },
            new() { Slug = "mensa-iscrizioni", Titolo = "Mensa scolastica: aperte le iscrizioni per il nuovo anno",
                Tipo = TipoNovita.Comunicato, Data = new DateTime(2026, 6, 18),
                Sommario = "Domande online entro il 31 agosto, tariffe per fascia ISEE.",
                Corpo = "Fino al 31 agosto è possibile iscriversi al servizio mensa per il nuovo anno scolastico. Tariffe agevolate per fascia ISEE.",
                InEvidenza = true, ArgomentoId = arg["istruzione"].Id, ACuraDiId = uff["scuola"].Id },
            new() { Slug = "tari-seconda-rata", Titolo = "TARI: promemoria seconda rata",
                Tipo = TipoNovita.Avviso, Data = new DateTime(2026, 6, 10),
                Sommario = "La seconda rata della TARI scade il 31 luglio.",
                Corpo = "La seconda rata della TARI scade il 31 luglio. L'avviso pagoPA è in arrivo nelle caselle dei contribuenti.",
                InEvidenza = false, ArgomentoId = arg["imposte"].Id, ACuraDiId = uff["tributi"].Id },
            new() { Slug = "ztl-orari-estivi", Titolo = "ZTL del Doblone: orari estivi in vigore",
                Tipo = TipoNovita.Notizia, Data = new DateTime(2026, 6, 1),
                Sommario = "Fino a settembre la ZTL resta attiva anche il sabato sera.",
                Corpo = "Per la stagione estiva la zona a traffico limitato del centro storico resta attiva anche il sabato dalle 20:00 alle 24:00. I permessi ordinari restano validi.",
                InEvidenza = false, ArgomentoId = arg["mobilita-sostenibile"].Id, ACuraDiId = uff["polizia-locale"].Id },
            new() { Slug = "biblioteca-orario-continuato", Titolo = "La biblioteca di Dogburg apre a orario continuato",
                Tipo = TipoNovita.Comunicato, Data = new DateTime(2026, 5, 20),
                Sommario = "Sala studio aperta dalle 9 alle 22 per la sessione d'esami.",
                Corpo = "Per tutta la sessione estiva la biblioteca civica di Dogburg estende l'orario della sala studio: dalle 9:00 alle 22:00 dal lunedì al sabato.",
                InEvidenza = false, ArgomentoId = arg["patrimonio-culturale"].Id, ACuraDiId = uff["urp"].Id },
        };
        int i = 0;
        foreach (var n in data) { n.IsDefault = true; n.IsPublished = true; n.Ordine = i++; }
        _db.Novita.AddRange(data);
        await _db.SaveChangesAsync();
    }

    private async Task SeedPersone()
    {
        var sindaca = new Persona
        {
            Slug = "sindaco", Nome = "Adele Anatrini", Ruolo = RuoloPersona.Sindaco, Carica = "Sindaca",
            Biografia = "Nata e cresciuta nel quartiere Dogburg, ragioniera, per vent'anni alla guida della cooperativa del Mercato dei Dobloni. Personaggio di fantasia: ogni somiglianza con persone o paperi reali è puramente casuale.",
            Deleghe = new() { "Personale e organizzazione degli uffici", "Comunicazione istituzionale e portale dei servizi", "Rapporti con la Regione Palmipedia" },
            Ricevimento = "Riceve i cittadini il giovedì dalle 10:00 alle 12:00 al Palazzo Comunale, su appuntamento tramite URP.",
            Email = "sindaca@paperopoli.demo (fittizio)", Telefono = "Numero verde 800-PAPERINO",
            RitrattoSvg = "<svg viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"Ritratto a fumetto della Sindaca Adele Anatrini\"><circle cx=\"60\" cy=\"60\" r=\"56\" fill=\"#FFD84D\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M28 118 q4 -30 32 -30 q28 0 32 30 Z\" fill=\"#1D9BB8\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M36 118 L84 92\" stroke=\"#F2B705\" stroke-width=\"9\"/><path d=\"M36 118 L84 92\" stroke=\"#E23A2E\" stroke-width=\"3\"/><circle cx=\"60\" cy=\"54\" r=\"28\" fill=\"#FFF6E0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M84 50 q16 -2 18 8 q-4 8 -18 5 q3 -6 0 -13 Z\" fill=\"#F2B705\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><circle cx=\"70\" cy=\"46\" r=\"4\" fill=\"#17120E\"/><circle cx=\"71.5\" cy=\"44.5\" r=\"1.4\" fill=\"#fff\"/><path d=\"M40 32 q8 -10 20 -6\" fill=\"none\" stroke=\"#17120E\" stroke-width=\"4\" stroke-linecap=\"round\"/><circle cx=\"48\" cy=\"88\" r=\"3.5\" fill=\"#fff\" stroke=\"#17120E\" stroke-width=\"2\"/><circle cx=\"58\" cy=\"91\" r=\"3.5\" fill=\"#fff\" stroke=\"#17120E\" stroke-width=\"2\"/><circle cx=\"68\" cy=\"90\" r=\"3.5\" fill=\"#fff\" stroke=\"#17120E\" stroke-width=\"2\"/></svg>",
            IsDefault = true, Ordine = 0
        };
        var giunta = new (string slug, string nome, string carica, RuoloPersona ruolo, string deleghe, string ric, string svg)[]
        {
            ("romeo-starnazzi", "Romeo Starnazzi", "Vicesindaco", RuoloPersona.Vicesindaco,
                "lavori pubblici, viabilità, manutenzione della Piazza del Doblone", "Riceve il lunedì 15:00–17:00 su appuntamento.",
                "<svg viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"Ritratto del vicesindaco Romeo Starnazzi\"><circle cx=\"60\" cy=\"60\" r=\"56\" fill=\"#BDE7F0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M28 118 q4 -30 32 -30 q28 0 32 30 Z\" fill=\"#14748A\" stroke=\"#17120E\" stroke-width=\"4\"/><circle cx=\"60\" cy=\"56\" r=\"28\" fill=\"#FFF6E0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M84 52 q16 -2 18 8 q-4 8 -18 5 q3 -6 0 -13 Z\" fill=\"#F2B705\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><circle cx=\"70\" cy=\"48\" r=\"4\" fill=\"#17120E\"/><circle cx=\"71.5\" cy=\"46.5\" r=\"1.4\" fill=\"#fff\"/><path d=\"M34 42 q2 -22 26 -22 q24 0 26 22 l-6 2 q-4 -14 -20 -14 q-16 0 -20 14 Z\" fill=\"#F2B705\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><path d=\"M30 44 h60\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linecap=\"round\"/></svg>"),
            ("clara-pennadoro", "Clara Pennadoro", "Assessora", RuoloPersona.Assessore,
                "scuola, mensa e trasporto scolastico, cultura e biblioteca civica", "Riceve il mercoledì 9:00–11:00 su appuntamento.",
                "<svg viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"Ritratto dell'assessora Clara Pennadoro\"><circle cx=\"60\" cy=\"60\" r=\"56\" fill=\"#FFD1CC\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M28 118 q4 -30 32 -30 q28 0 32 30 Z\" fill=\"#E23A2E\" stroke=\"#17120E\" stroke-width=\"4\"/><circle cx=\"60\" cy=\"56\" r=\"28\" fill=\"#FFF6E0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M84 52 q16 -2 18 8 q-4 8 -18 5 q3 -6 0 -13 Z\" fill=\"#F2B705\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><circle cx=\"70\" cy=\"48\" r=\"9\" fill=\"none\" stroke=\"#17120E\" stroke-width=\"3\"/><circle cx=\"48\" cy=\"48\" r=\"9\" fill=\"none\" stroke=\"#17120E\" stroke-width=\"3\"/><path d=\"M57 48 h4\" stroke=\"#17120E\" stroke-width=\"3\"/><circle cx=\"70\" cy=\"48\" r=\"3.5\" fill=\"#17120E\"/><path d=\"M42 30 q10 -8 24 -4\" fill=\"none\" stroke=\"#17120E\" stroke-width=\"4\" stroke-linecap=\"round\"/></svg>"),
            ("bartolo-beccogialli", "Bartolo Beccogialli", "Assessore", RuoloPersona.Assessore,
                "bilancio, TARI, IMU e tributi, pagamenti pagoPA", "Riceve il martedì 9:00–12:30 con l'Ufficio Tributi.",
                "<svg viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"Ritratto dell'assessore Bartolo Beccogialli\"><circle cx=\"60\" cy=\"60\" r=\"56\" fill=\"#FFD84D\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M28 118 q4 -30 32 -30 q28 0 32 30 Z\" fill=\"#17120E\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M52 92 l8 6 8 -6 -8 -6 Z\" fill=\"#E23A2E\" stroke=\"#17120E\" stroke-width=\"3\" stroke-linejoin=\"round\"/><circle cx=\"60\" cy=\"56\" r=\"28\" fill=\"#FFF6E0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M84 52 q16 -2 18 8 q-4 8 -18 5 q3 -6 0 -13 Z\" fill=\"#F2B705\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><circle cx=\"70\" cy=\"48\" r=\"4\" fill=\"#17120E\"/><circle cx=\"71.5\" cy=\"46.5\" r=\"1.4\" fill=\"#fff\"/><path d=\"M34 40 q26 -14 52 0 l-4 8 q-22 -10 -44 0 Z\" fill=\"#2E7D1E\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/></svg>"),
            ("ines-tuffetti", "Ines Tuffetti", "Assessora", RuoloPersona.Assessore,
                "ambiente, raccolta differenziata, Parco della Fontana Dorata", "Riceve il venerdì 10:00–12:00 su appuntamento.",
                "<svg viewBox=\"0 0 120 120\" role=\"img\" aria-label=\"Ritratto dell'assessora Ines Tuffetti\"><circle cx=\"60\" cy=\"60\" r=\"56\" fill=\"#BDE7F0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M28 118 q4 -30 32 -30 q28 0 32 30 Z\" fill=\"#2E7D1E\" stroke=\"#17120E\" stroke-width=\"4\"/><circle cx=\"60\" cy=\"56\" r=\"28\" fill=\"#FFF6E0\" stroke=\"#17120E\" stroke-width=\"4\"/><path d=\"M84 52 q16 -2 18 8 q-4 8 -18 5 q3 -6 0 -13 Z\" fill=\"#F2B705\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><circle cx=\"70\" cy=\"48\" r=\"4\" fill=\"#17120E\"/><circle cx=\"71.5\" cy=\"46.5\" r=\"1.4\" fill=\"#fff\"/><path d=\"M32 38 q6 -16 28 -16 q20 0 26 12 l-6 6 q-8 -8 -20 -8 q-16 0 -22 10 Z\" fill=\"#2E7D1E\" stroke=\"#17120E\" stroke-width=\"3.5\" stroke-linejoin=\"round\"/><path d=\"M76 22 q10 -8 16 -2 q-2 10 -14 8 Z\" fill=\"#7FBF4D\" stroke=\"#17120E\" stroke-width=\"3\" stroke-linejoin=\"round\"/></svg>"),
        };
        _db.Persone.Add(sindaca);
        int i = 1;
        foreach (var g in giunta)
        {
            _db.Persone.Add(new Persona
            {
                Slug = g.slug, Nome = g.nome, Carica = g.carica, Ruolo = g.ruolo,
                Deleghe = new() { g.deleghe }, Ricevimento = g.ric, RitrattoSvg = g.svg,
                Telefono = "Numero verde 800-PAPERINO", IsDefault = true, Ordine = i++
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedLuoghi()
    {
        var data = new Luogo[]
        {
            new() { Slug = "piazza-del-doblone", Nome = "Piazza del Doblone", Categoria = "Piazza",
                Descrizione = "Il cuore della città, con la fontana dorata e il mercato del sabato.",
                Indirizzo = "Piazza del Doblone, centro storico, Paperopoli",
                ModalitaAccesso = "Area pedonale, sempre accessibile. Accesso senza barriere architettoniche. Ingresso gratuito.",
                Contatti = "URP — urp@paperopoli.demo — 800-PAPERINO", Orari = "Sempre aperta" },
            new() { Slug = "parco-fontana-dorata", Nome = "Parco della Fontana Dorata", Categoria = "Parco",
                Descrizione = "Aree gioco, laghetto delle papere e percorso ciclabile lungo l'ansa del fiume.",
                Indirizzo = "viale del Fiume 1, Paperopoli",
                ModalitaAccesso = "Ingressi da viale del Fiume e dal ponte di Dogburg. Percorsi accessibili a passeggini e carrozzine. Ingresso gratuito.",
                Contatti = "Ufficio Ambiente — ambiente@paperopoli.demo", Orari = "Aperto dall'alba al tramonto" },
            new() { Slug = "biblioteca-dogburg", Nome = "Biblioteca civica di Dogburg", Categoria = "Cultura",
                Descrizione = "Sala studio, emeroteca e la più grande collezione di fumetti della regione.",
                Indirizzo = "via delle Nuvolette 7, quartiere Dogburg, Paperopoli",
                ModalitaAccesso = "Ingresso libero con tessera gratuita. Ascensore e postazioni accessibili. Sala studio su prenotazione nei weekend.",
                Contatti = "biblioteca@paperopoli.demo — 800-PAPERINO", Orari = "Lun–sab 9:00–19:00" },
        };
        int i = 0;
        foreach (var l in data) { l.IsDefault = true; l.Ordine = i++; }
        _db.Luoghi.AddRange(data);
        await _db.SaveChangesAsync();
    }

    private async Task SeedEventi()
    {
        var data = new Evento[]
        {
            new() { Slug = "festa-san-quaquaraqua", Titolo = "Festa di San Quaquaraqua", Ricorrenza = "Festa patronale · settembre",
                Sommario = "Sfilata dei carri, mercatino dei Dobloni e spettacolo pirotecnico sul fiume.",
                Descrizione = "La festa del patrono San Quaquaraqua anima Paperopoli per tre giorni con sfilate, mercatini e lo spettacolo pirotecnico sul fiume.",
                DataInizio = new DateTime(2026, 9, 18), DataFine = new DateTime(2026, 9, 20), Orario = "Dalle 10:00 alle 24:00",
                LuogoTesto = "Piazza del Doblone", Costo = "Gratuito", Contatti = "URP — urp@paperopoli.demo — 800-PAPERINO" },
            new() { Slug = "mercato-dei-dobloni", Titolo = "Mercato dei Dobloni", Ricorrenza = "Mercato · ogni sabato",
                Sommario = "Prodotti locali e artigianato in Piazza del Doblone, dalle 8 alle 13.",
                Descrizione = "Ogni sabato mattina la Piazza del Doblone ospita il mercato con prodotti locali e artigianato.",
                Orario = "Ogni sabato, dalle 8:00 alle 13:00",
                LuogoTesto = "Piazza del Doblone", Costo = "Ingresso gratuito", Contatti = "URP — urp@paperopoli.demo" },
            new() { Slug = "notte-dei-fumetti", Titolo = "La Notte dei Fumetti", Ricorrenza = "Cultura · luglio",
                Sommario = "Letture, autori e proiezioni fino a mezzanotte alla biblioteca di Dogburg.",
                Descrizione = "La biblioteca civica apre fino a mezzanotte con letture ad alta voce, incontri con autori e proiezioni per famiglie.",
                DataInizio = new DateTime(2026, 7, 25), Orario = "Dalle 18:00 alle 24:00",
                LuogoTesto = "Biblioteca civica di Dogburg", Costo = "Gratuito con tessera della biblioteca", Contatti = "biblioteca@paperopoli.demo" },
        };
        int i = 0;
        foreach (var e in data) { e.IsDefault = true; e.Ordine = i++; }
        _db.Eventi.AddRange(data);
        await _db.SaveChangesAsync();
    }

    private async Task SeedDocumenti(Dictionary<string, UnitaOrganizzativa> uff)
    {
        var data = new Documento[]
        {
            new() { Slug = "statuto-comunale", Titolo = "Statuto del Comune di Paperopoli", Tipo = TipoDocumento.Normativa,
                Data = new DateTime(2024, 1, 15), Descrizione = "Lo statuto dell'ente immaginario di Paperopoli (documento dimostrativo).",
                UrlFile = "/media/statuto-comunale.pdf", UfficioResponsabileId = uff["urp"].Id },
            new() { Slug = "regolamento-tari", Titolo = "Regolamento TARI", Tipo = TipoDocumento.Normativa,
                Data = new DateTime(2026, 1, 20), Descrizione = "Regolamento per l'applicazione della tassa sui rifiuti.",
                UrlFile = "/media/regolamento-tari.pdf", UfficioResponsabileId = uff["tributi"].Id },
            new() { Slug = "carta-servizi", Titolo = "Carta dei servizi", Tipo = TipoDocumento.Documento,
                Data = new DateTime(2026, 3, 1), Descrizione = "Impegni e standard di qualità dei servizi comunali.",
                UrlFile = "/media/carta-servizi.pdf", UfficioResponsabileId = uff["urp"].Id },
            new() { Slug = "calendario-differenziata", Titolo = "Calendario raccolta differenziata", Tipo = TipoDocumento.Dataset,
                Data = new DateTime(2026, 6, 30), Descrizione = "Calendario del porta a porta per quartiere in formato aperto.",
                UrlFile = "/media/calendario-differenziata.pdf", UfficioResponsabileId = uff["ambiente"].Id },
        };
        int i = 0;
        foreach (var d in data) { d.IsDefault = true; d.Ordine = i++; }
        _db.Documenti.AddRange(data);
        await _db.SaveChangesAsync();
    }

    private async Task SeedPagine()
    {
        // Dicitura richiesta dal criterio C.SI.3.4 - Licenza e attribuzione.
        const string licenza =
            "<h2 data-element=\"legal-notes-section\">Licenza dei contenuti</h2>" +
            "<div data-element=\"legal-notes-body\"><p>In applicazione del principio open by default ai sensi dell'articolo 52 del decreto legislativo 7 marzo 2005, n. 82 (CAD) e salvo dove diversamente specificato (compresi i contenuti incorporati di terzi), i dati, i documenti e le informazioni pubblicati sul sito sono rilasciati con licenza CC-BY 4.0. Gli utenti sono quindi liberi di condividere (riprodurre, distribuire, comunicare al pubblico, esporre in pubblico), rappresentare, eseguire e recitare questo materiale con qualsiasi mezzo e formato e modificare (trasformare il materiale e utilizzarlo per opere derivate) per qualsiasi fine, anche commerciale con il solo onere di attribuzione, senza apporre restrizioni aggiuntive.</p></div>";

        var data = new Pagina[]
        {
            new() { Slug = "privacy", Titolo = "Informativa sulla privacy", Sottotitolo = "Privacy", MostraInFooter = true,
                Corpo = "<h2>Trattamento dei dati</h2><p>Questo è un sito dimostrativo di un ente immaginario: non raccoglie dati personali, non usa cookie di profilazione e non conserva le conversazioni con l'assistente oltre la sessione tecnica necessaria a produrre la risposta.</p><h2>Domande all'assistente</h2><p>Le domande inviate all'assistente virtuale vengono elaborate dai fornitori dei modelli selezionati (Gemini o Claude) al solo scopo di generare la risposta. Non inserire dati personali reali nelle domande.</p><h2>Titolare (fittizio)</h2><p>Comune di Paperopoli, via dei Dobloni 13 — PEC protocollo@pec.paperopoli.demo.</p>" },
            new() { Slug = "note-legali", Titolo = "Note legali", Sottotitolo = "Note legali", MostraInFooter = true,
                Corpo = "<h2>Natura del sito</h2><p>Il Comune di Paperopoli è un ente immaginario. Questo portale è una simulazione dimostrativa realizzata a scopo di studio: nomi, persone, indirizzi, importi e scadenze sono di fantasia e non hanno alcun valore reale.</p><h2>Contenuti e stile</h2><p>Lo stile grafico evoca l'estetica del fumetto classico senza riprodurre personaggi, loghi o marchi di alcun editore. Ogni riferimento a opere esistenti è evitato; mascotte e stemma sono originali.</p><h2>Assistente virtuale</h2><p>L'assistente risponde esclusivamente sulla base di un corpus dimostrativo di contenuti fittizi. Le risposte non costituiscono informazione ufficiale né consulenza.</p>" + licenza },
            new() { Slug = "dichiarazione-accessibilita", Titolo = "Dichiarazione di accessibilità", Sottotitolo = "Accessibilità", MostraInFooter = true,
                Corpo = "<div class=\"nota-fonte\" style=\"margin-bottom:1.4rem;\"><strong>Nota:</strong> il Comune di Paperopoli è un ente immaginario. Questa pagina imita la dichiarazione di accessibilità prevista per i siti della pubblica amministrazione, ma non ha valore legale e non è registrata presso AgID.</div><h2>Stato di conformità</h2><p>Questo sito è progettato per essere <strong>parzialmente conforme</strong> ai requisiti della WCAG 2.1 livello AA: navigazione da tastiera, collegamento di salto al contenuto, landmark e intestazioni strutturate, contrasti verificati, etichette esplicite dei campi, contenuti dinamici annunciati con regioni live e rispetto della preferenza di riduzione del movimento.</p><h2>Contenuti non accessibili</h2><ul><li>Il carattere display in stile fumetto è usato solo per i titoli; i testi informativi usano un carattere ad alta leggibilità.</li><li>Le onomatopee decorative sono nascoste alle tecnologie assistive.</li></ul><h2>Meccanismo di feedback</h2><p>Hai riscontrato barriere? Scrivi a urp@paperopoli.demo o chiama il numero verde 800-PAPERINO. Nella realtà qui troveresti anche il collegamento al meccanismo di segnalazione AgID.</p>" },
        };
        int i = 0;
        foreach (var p in data) { p.IsDefault = true; p.Ordine = i++; }
        _db.Pagine.AddRange(data);
        await _db.SaveChangesAsync();
    }

    private async Task SeedMenu()
    {
        // Menu principale: voci e ordine obbligatori del modello (C.SI.1.6) + una voce
        // aggiuntiva ammessa in tolleranza (Assistente virtuale, data-element custom-submenu).
        var principale = new (string et, string url, bool ev, string icona, string de)[]
        {
            ("Amministrazione", "/amministrazione", false, "", "management"),
            ("Novità", "/novita", false, "", "news"),
            ("Servizi", "/servizi", false, "", "all-services"),
            ("Vivere il Comune", "/vivere-il-comune", false, "", "live"),
            ("Assistente virtuale", "/assistente", true, "💬", "custom-submenu"),
        };
        int i = 0;
        foreach (var m in principale)
            _db.VociMenu.Add(new VoceMenu { Etichetta = m.et, Url = m.url, Evidenzia = m.ev, Icona = m.icona,
                DataElement = m.de, Posizione = PosizioneMenu.Principale, IsDefault = true, Ordine = i++ });

        var footer = new (string et, string url, string de)[]
        {
            ("Dichiarazione di accessibilità", "/dichiarazione-accessibilita", "accessibility-link"),
            ("Privacy", "/privacy", "privacy-policy-link"),
            ("Note legali", "/note-legali", "legal-notes"),
            ("Domande frequenti (FAQ)", "/faq", "faq"),
            ("Segnalazione disservizio", "/segnalazione-disservizio", "report-inefficiency"),
            ("Valutazione adesione al modello", Sito("Valutazione", "http://localhost:5400/"), ""),
        };
        i = 0;
        foreach (var m in footer)
            _db.VociMenu.Add(new VoceMenu { Etichetta = m.et, Url = m.url, DataElement = m.de,
                Posizione = PosizioneMenu.Footer, IsDefault = true, Ordine = i++ });

        await _db.SaveChangesAsync();
    }

    private async Task SeedFaq()
    {
        var data = new (string d, string r, string cat)[]
        {
            ("Quando scade la prima rata della TARI?",
             "La prima rata della TARI scade il 30 aprile. È ammesso il pagamento in unica soluzione entro la stessa data.", "Tributi"),
            ("Come pago un avviso pagoPA?",
             "Puoi pagare online dal portale, con l'app dei pagamenti, oppure presso banche, tabaccai e uffici postali presentando l'avviso con il codice IUV.", "Pagamenti"),
            ("Come prenoto un appuntamento con un ufficio?",
             "Usa la funzione \"Prenota appuntamento\" nella sezione Servizi: scegli l'ufficio, la data e l'orario disponibili. Riceverai subito il codice di prenotazione.", "Uffici"),
            ("Quanto costa la carta d'identità elettronica?",
             "Il rilascio della CIE costa 22,00 euro, diritti inclusi. Serve l'appuntamento presso l'Ufficio Anagrafe.", "Anagrafe"),
            ("Quando passa la raccolta di carta e cartone a Dogburg?",
             "Dal 15 luglio la raccolta di carta e cartone nel quartiere Dogburg avviene il venerdì. Esponi i contenitori entro le 6:00.", "Rifiuti"),
            ("Come segnalo un disservizio?",
             "Usa il collegamento \"Segnalazione disservizio\" nel footer del sito: indica la categoria, il luogo e una descrizione. Puoi allegare foto e documenti.", "Assistenza"),
            ("Gli sportelli sono aperti il giorno del patrono?",
             "No, nella giornata di San Quaquaraqua gli sportelli comunali restano chiusi. Riaprono regolarmente il giorno successivo.", "Uffici"),
            ("Chi può chiedere il contributo per l'affitto?",
             "I nuclei familiari residenti con contratto di locazione registrato e ISEE entro la soglia indicata dal bando annuale, pubblicato in autunno.", "Assistenza"),
        };
        int i = 0;
        foreach (var f in data)
            _db.Faq.Add(new FaqItem { Slug = $"faq-{i + 1}", Domanda = f.d, Risposta = f.r, Categoria = f.cat, IsDefault = true, Ordine = i++ });
        await _db.SaveChangesAsync();
    }
}
