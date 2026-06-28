// Máscara de miles para inputs de dinero (.js-money): muestra 150.000 al escribir
// y al enviar el formulario deja solo dígitos para que el servidor lo parsee bien.
(function () {
    function soloDigitos(s) { return (s || '').replace(/\D+/g, ''); }
    function formatear(s) {
        var d = soloDigitos(s);
        if (!d) return '';
        return d.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    }

    function init() {
        var inputs = document.querySelectorAll('.js-money');
        inputs.forEach(function (inp) {
            // Formatear valor inicial.
            if (inp.value) inp.value = formatear(inp.value);

            inp.addEventListener('input', function () {
                var pos = inp.value.length - inp.selectionStart;
                inp.value = formatear(inp.value);
                // Reubicar el cursor aproximadamente al final relativo.
                var newPos = Math.max(0, inp.value.length - pos);
                try { inp.setSelectionRange(newPos, newPos); } catch (e) { }
            });

            // Al enviar, limpiar a dígitos puros.
            if (inp.form) {
                inp.form.addEventListener('submit', function () {
                    inp.value = soloDigitos(inp.value);
                });
            }
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
