/**
 * Chat dell'assistente virtuale ChattyDuck (Comune di Paperopoli).
 * Modulo condiviso: usato dalla pagina /assistente e dal widget montato dal portale.
 */
(function () {
    'use strict';

    function fmt(n) {
        return Number(n).toLocaleString('it-IT');
    }

    /**
     * Stato d'uso dei modelli (finestra 60s + giornaliero) dal tracker del server.
     * Aggiorna tutti gli span [data-usage-model] presenti nella pagina (pagina e widget).
     */
    async function refreshUsage() {
        const spans = document.querySelectorAll('[data-usage-model]');
        if (spans.length === 0) return;
        try {
            const res = await fetch('/chat/usage');
            if (!res.ok) return;
            const snapshots = await res.json();
            for (const s of snapshots) {
                const parts = [];
                const l = s.limits || {};

                let rich = fmt(s.requestsLastMinute) + ' rich.';
                if (l.requestsPerMinute != null)
                    rich += ' (' + fmt(Math.max(0, l.requestsPerMinute - s.requestsLastMinute)) + ' rimaste)';
                parts.push('Ultimo minuto: ' + rich);

                let tin = fmt(s.inputTokensLastMinute) + ' token in';
                if (l.inputTokensPerMinute != null)
                    tin += ' (' + fmt(Math.max(0, l.inputTokensPerMinute - s.inputTokensLastMinute)) + ' rimasti)';
                parts.push(tin);

                let tout = fmt(s.outputTokensLastMinute) + ' token out';
                if (l.outputTokensPerMinute != null)
                    tout += ' (' + fmt(Math.max(0, l.outputTokensPerMinute - s.outputTokensLastMinute)) + ' rimasti)';
                parts.push(tout);

                if (l.requestsPerDay != null)
                    parts.push('oggi ' + fmt(s.requestsToday) + '/' + fmt(l.requestsPerDay) + ' rich.');

                // Se il provider ha riportato lo stato reale (header rate-limit), ha la precedenza.
                let text;
                const p = s.provider;
                if (p && p.requestsRemaining != null) {
                    const pezzi = [fmt(p.requestsRemaining) + '/' + fmt(p.requestsLimit) + ' rich. rimaste'];
                    if (p.inputTokensRemaining != null)
                        pezzi.push(fmt(p.inputTokensRemaining) + '/' + fmt(p.inputTokensLimit) + ' token in rimasti');
                    if (p.outputTokensRemaining != null)
                        pezzi.push(fmt(p.outputTokensRemaining) + '/' + fmt(p.outputTokensLimit) + ' token out rimasti');
                    const at = p.retrievedAt ? new Date(p.retrievedAt).toLocaleTimeString('it-IT') : '';
                    text = 'Dato reale provider: ' + pezzi.join(' · ') + (at ? ' (agg. ' + at + ')' : '');
                } else {
                    text = 'Stima locale: ' + parts.join(' · ');
                }

                document.querySelectorAll('[data-usage-model="' + s.model + '"]')
                    .forEach(function (span) { span.textContent = text; });
            }
        } catch {
            // pannello informativo: un errore di rete qui non deve disturbare la chat
        }
    }

    // Aggiorna quando l'utente apre il pannello dei limiti.
    document.querySelectorAll('.chat-limiti').forEach(function (d) {
        d.addEventListener('toggle', function () { if (d.open) refreshUsage(); });
    });


    /**
     * Rende il markdown della risposta come nodi DOM.
     *
     * Il modello risponde in markdown, perche' e' il modo in cui scrive: elenchi per le
     * scadenze, grassetto per gli importi, id di sezione fra apici per le citazioni.
     * Inserito come testo, quel markdown resta letterale e il cittadino legge asterischi.
     *
     * Il renderer e' minimo di proposito e costruisce nodi, senza mai passare da
     * innerHTML. Il testo che arriva qui e' scritto da un modello a partire da contenuti
     * del CMS: nessuno dei due e' un posto da cui accettare HTML.
     */
    function renderMarkdown(md) {
        const frag = document.createDocumentFragment();
        const righe = String(md ?? '').replace(/\r\n/g, '\n').split('\n');
        let i = 0;

        // Solo http, https e percorsi interni: esclude javascript: e data:.
        function urlSicura(u) {
            return /^https?:\/\//i.test(u) || /^\//.test(u);
        }

        function inline(testo, dove) {
            const re = /(`[^`\n]+`)|(\*\*[^*\n]+\*\*)|(\*[^*\n]+\*)|(\[[^\]\n]+\]\([^)\s]+\))/g;
            let ultimo = 0, m;
            while ((m = re.exec(testo)) !== null) {
                if (m.index > ultimo) {
                    dove.appendChild(document.createTextNode(testo.slice(ultimo, m.index)));
                }
                const t = m[0];
                if (t.startsWith('`')) {
                    const el = document.createElement('code');
                    el.textContent = t.slice(1, -1);
                    dove.appendChild(el);
                } else if (t.startsWith('**')) {
                    const el = document.createElement('strong');
                    el.textContent = t.slice(2, -2);
                    dove.appendChild(el);
                } else if (t.startsWith('*')) {
                    const el = document.createElement('em');
                    el.textContent = t.slice(1, -1);
                    dove.appendChild(el);
                } else {
                    const taglio = t.indexOf('](');
                    const etichetta = t.slice(1, taglio);
                    const href = t.slice(taglio + 2, -1);
                    if (urlSicura(href)) {
                        const a = document.createElement('a');
                        a.href = href;
                        a.target = '_blank';
                        a.rel = 'noopener';
                        a.textContent = etichetta;
                        dove.appendChild(a);
                    } else {
                        dove.appendChild(document.createTextNode(etichetta));
                    }
                }
                ultimo = m.index + t.length;
            }
            if (ultimo < testo.length) {
                dove.appendChild(document.createTextNode(testo.slice(ultimo)));
            }
        }

        const VOCE = /^\s*([-*+]|\d+[.)])\s+(.*)$/;
        const TITOLO = /^(#{1,6})\s+(.*)$/;

        while (i < righe.length) {
            const riga = righe[i];

            if (riga.trim() === '') { i++; continue; }

            const titolo = TITOLO.exec(riga);
            if (titolo) {
                // Dentro un fumetto un h1 sarebbe fuori scala: si parte da h4.
                const liv = Math.min(6, 3 + titolo[1].length);
                const el = document.createElement('h' + liv);
                inline(titolo[2], el);
                frag.appendChild(el);
                i++;
                continue;
            }

            const voce = VOCE.exec(riga);
            if (voce) {
                const numerato = /\d/.test(voce[1]);
                const lista = document.createElement(numerato ? 'ol' : 'ul');
                while (i < righe.length) {
                    const v = VOCE.exec(righe[i]);
                    if (!v) break;
                    const li = document.createElement('li');
                    inline(v[2], li);
                    lista.appendChild(li);
                    i++;
                }
                frag.appendChild(lista);
                continue;
            }

            if (/^\s*([-*_])\1{2,}\s*$/.test(riga)) {
                frag.appendChild(document.createElement('hr'));
                i++;
                continue;
            }

            // Paragrafo: righe consecutive fino alla prossima vuota o al blocco seguente.
            const p = document.createElement('p');
            const pezzi = [];
            while (i < righe.length && righe[i].trim() !== '' &&
                   !VOCE.test(righe[i]) && !TITOLO.test(righe[i])) {
                pezzi.push(righe[i].trim());
                i++;
            }
            inline(pezzi.join(' '), p);
            frag.appendChild(p);
        }

        return frag;
    }

    function initChat(root) {
        const messages = root.querySelector('[data-chat-messages]');
        const form = root.querySelector('[data-chat-form]');
        const input = root.querySelector('[data-chat-input]');
        const send = root.querySelector('[data-chat-send]');
        const modelSelect = root.querySelector('[data-chat-model]');

        function addBalloon(role, text) {
            const div = document.createElement('div');
            div.className = 'balloon ' + role;
            const chi = document.createElement('span');
            chi.className = 'chi';
            chi.textContent = role.includes('utente') ? 'Cittadino' : 'Assistente';
            div.appendChild(chi);
            div.appendChild(document.createTextNode(text));
            messages.appendChild(div);
            messages.scrollTop = messages.scrollHeight;
            return div;
        }

        /**
         * Sostituisce il testo del fumetto con la risposta resa.
         * L'intestazione con "Assistente" resta: e' il primo figlio.
         */
        function mostraRisposta(balloon, testo) {
            while (balloon.childNodes.length > 1) balloon.removeChild(balloon.lastChild);
            balloon.classList.add('md');
            balloon.appendChild(renderMarkdown(testo));
        }

        function addFonti(fonti) {
            if (!fonti || fonti.length === 0) return;
            const box = document.createElement('details');
            box.className = 'fonti-box';
            const summary = document.createElement('summary');
            summary.textContent = 'Fonti recuperate (' + fonti.length + ')';
            box.appendChild(summary);
            for (const f of fonti) {
                const item = document.createElement('div');
                item.className = 'passaggio';

                // Titolo della scheda piu' sezione: una sezione da sola non dice a quale
                // contenuto appartenga, ed e' quello che il cittadino deve poter ritrovare.
                const pid = document.createElement('span');
                pid.className = 'pid';
                pid.textContent = f.scheda + (f.etichetta ? ' · ' + f.etichetta : '');

                const meta = document.createElement('span');
                meta.className = 'pmeta';
                const parti = [f.id];
                if (f.versione) parti.push('v' + f.versione);
                if (f.hash) parti.push('hash ' + f.hash.replace('sha256:', '').substring(0, 12) + '…');
                meta.textContent = parti.join(' · ');

                const txt = document.createElement('p');
                txt.textContent = f.testo;

                item.append(pid, meta, txt);

                // Un contenuto fuori dal periodo di validita' va segnalato: citarlo come
                // attuale e' il modo peggiore di sbagliare per una fonte certificata.
                if (f.valido === false) {
                    const scaduto = document.createElement('span');
                    scaduto.className = 'pmeta';
                    scaduto.textContent = '⚠ contenuto non più valido';
                    item.appendChild(scaduto);
                }

                if (f.url) {
                    const link = document.createElement('a');
                    link.href = f.url;
                    link.target = '_blank';
                    link.rel = 'noopener';
                    link.className = 'pmeta';
                    link.textContent = 'apri la pagina';
                    item.appendChild(link);
                }

                box.appendChild(item);
            }
            messages.appendChild(box);
            messages.scrollTop = messages.scrollHeight;
        }

        async function ask(text) {
            const model = modelSelect ? modelSelect.value : 'gemini';
            addBalloon('utente', text);
            if (send) send.disabled = true;
            const pending = addBalloon('assistente pending', '… sto consultando le fonti (' + model + ')');
            try {
                const res = await fetch('/chat', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ message: text, model })
                });
                const data = await res.json();
                if (!res.ok) {
                    pending.lastChild.textContent = 'Errore: ' + (data.error ?? data.detail ?? res.status);
                    pending.classList.add('errore');
                    pending.classList.remove('pending');
                    return;
                }
                pending.classList.remove('pending');
                mostraRisposta(pending, data.reply);
                addFonti(data.fonti);
            } catch (err) {
                pending.lastChild.textContent = 'Errore di rete: ' + err;
                pending.classList.add('errore');
                pending.classList.remove('pending');
            } finally {
                if (send) send.disabled = false;
                input.focus();
                refreshUsage();
            }
        }

        // Voce "configura il tuo chatbot": non e' un modello, apre la modale e ripristina la scelta.
        const mcpDialog = root.querySelector('[data-mcp-modale]');
        if (modelSelect && modelSelect.tagName === 'SELECT' && mcpDialog) {
            let ultimoModello = modelSelect.value;
            modelSelect.addEventListener('change', function () {
                if (modelSelect.value === 'mcp-config') {
                    modelSelect.value = ultimoModello;
                    mcpDialog.showModal();
                } else {
                    ultimoModello = modelSelect.value;
                }
            });
            mcpDialog.querySelector('[data-mcp-chiudi]').addEventListener('click', function () {
                mcpDialog.close();
            });
        }

        // Modale informativa: mostra la sezione del modello selezionato.
        const infoBtn = root.querySelector('[data-info-apri]');
        const infoDialog = root.querySelector('[data-info-modale]');
        if (infoBtn && infoDialog) {
            infoBtn.addEventListener('click', function () {
                const model = modelSelect ? modelSelect.value : 'gemini';
                infoDialog.querySelectorAll('[data-info-modello]').forEach(function (sec) {
                    sec.hidden = sec.getAttribute('data-info-modello') !== model;
                });
                infoDialog.showModal();
            });
            infoDialog.querySelector('[data-info-chiudi]').addEventListener('click', function () {
                infoDialog.close();
            });
        }

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            const text = input.value.trim();
            if (!text) return;
            input.value = '';
            ask(text);
        });

        return { ask: ask, input: input };
    }

    // Chat a pagina intera (/assistente)
    const pagina = document.querySelector('[data-chat-pagina]');
    if (pagina) {
        const chat = initChat(pagina);
        // Domanda arrivata dalla ricerca del sito o dall'hero: ?q=...
        const q = new URLSearchParams(window.location.search).get('q');
        if (q && q.trim()) {
            chat.ask(q.trim());
        } else {
            // Focus di cortesia: marcato come interazione mouse per non far
            // comparire l'anello di focus di Bootstrap Italia all'apertura.
            chat.input.setAttribute('data-focus-mouse', 'true');
            chat.input.focus({ preventScroll: true });
            chat.input.addEventListener('blur', function () {
                chat.input.removeAttribute('data-focus-mouse');
            }, { once: true });
        }
    }

    // Widget flottante (tutte le altre pagine del portale)
    const widgetBtn = document.querySelector('[data-widget-apri]');
    const widgetPanel = document.querySelector('[data-widget-pannello]');
    if (widgetBtn && widgetPanel) {
        const chat = initChat(widgetPanel);
        const chiudi = widgetPanel.querySelector('[data-widget-chiudi]');

        function apri() {
            widgetPanel.classList.add('aperto');
            widgetBtn.setAttribute('aria-expanded', 'true');
            widgetBtn.hidden = true;
            chat.input.focus();
        }
        function chiudiPannello() {
            widgetPanel.classList.remove('aperto');
            widgetBtn.setAttribute('aria-expanded', 'false');
            widgetBtn.hidden = false;
            widgetBtn.focus();
        }

        widgetBtn.addEventListener('click', apri);
        chiudi.addEventListener('click', chiudiPannello);
        widgetPanel.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') chiudiPannello();
        });
    }
})();
