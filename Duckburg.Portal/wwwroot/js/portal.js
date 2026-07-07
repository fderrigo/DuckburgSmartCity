/**
 * Script del portale Duckburg: menu principale a scomparsa (mobile).
 * La chat dell'assistente vive in ChattyDuck.Quack (quack-chat.js).
 */
(function () {
    'use strict';

    const navToggle = document.querySelector('[data-nav-toggle]');
    if (navToggle) {
        const lista = document.getElementById('menu-principale');
        navToggle.addEventListener('click', function () {
            const aperto = lista.classList.toggle('aperto');
            navToggle.setAttribute('aria-expanded', aperto ? 'true' : 'false');
        });
    }
})();
