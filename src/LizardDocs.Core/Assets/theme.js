/* Read the preference before paint; system remains the default. */
(() => {
  function initializeTheme(doc, host = globalThis) {
    const root = doc.documentElement;
    const media = host.matchMedia?.('(prefers-color-scheme: dark)');
    const key = 'lizarddocs-theme';
    let preference = 'system';
    try { preference = host.localStorage?.getItem(key) || 'system'; } catch (_) { /* Storage may be unavailable on local files. */ }
    if (!['system', 'light', 'dark'].includes(preference)) preference = 'system';
    let button;
    function apply() {
      root.dataset.theme = preference === 'system' ? (media?.matches ? 'dark' : 'light') : preference;
      if (button) {
        button.setAttribute('aria-label', 'Colour theme: ' + preference + '. Change colour theme');
        button.title = 'Colour theme: ' + preference + ' (system → light → dark)';
        button.firstElementChild.textContent = preference === 'system' ? '◐' : preference === 'light' ? '☀' : '☾';
      }
    }
    apply();
    media?.addEventListener?.('change', apply);
    function bind() {
      button = doc.querySelector('.theme-switcher');
      if (!button) return;
      button.hidden = false;
      button.addEventListener('click', () => {
        preference = { system: 'light', light: 'dark', dark: 'system' }[preference];
        try { host.localStorage?.setItem(key, preference); } catch (_) { /* The current page still changes. */ }
        apply();
      });
      apply();
    }
    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', bind, { once: true });
    else bind();
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { initializeTheme };
  if (typeof document !== 'undefined') initializeTheme(document);
})();
