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

        function addPassages(passages) {
            if (!passages || passages.length === 0) return;
            const box = document.createElement('details');
            box.className = 'fonti-box';
            const summary = document.createElement('summary');
            summary.textContent = 'Fonti recuperate (' + passages.length + ')';
            box.appendChild(summary);
            for (const p of passages) {
                const item = document.createElement('div');
                item.className = 'passaggio';
                const pid = document.createElement('span');
                pid.className = 'pid';
                pid.textContent = p.id;
                const meta = document.createElement('span');
                meta.className = 'pmeta';
                meta.textContent = 'v' + p.version + ' · hash ' + p.hash.substring(0, 12) + '…';
                const txt = document.createElement('p');
                txt.textContent = p.text;
                item.append(pid, meta, txt);
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
                pending.lastChild.textContent = data.reply;
                addPassages(data.passages);
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
            chat.input.focus();
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
