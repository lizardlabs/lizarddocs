/* Optional site integrations. An empty analytics ID makes no Google requests. */
(() => {
  function initializeIntegrations(doc, host = globalThis) {
    const node = doc.getElementById('lizarddocs-integrations');
    if (!node) return;
    const settings = JSON.parse(node.textContent);
    const id = settings.analyticsId;
    let analyticsStarted = false;
    function analytics(allowed) {
      if (!id) return;
      host['ga-disable-' + id] = !allowed;
      if (!allowed && !analyticsStarted) return;
      host.dataLayer = host.dataLayer || [];
      host.gtag = host.gtag || function () { host.dataLayer.push(arguments); };
      const consent = { analytics_storage: allowed ? 'granted' : 'denied', ad_storage: 'denied', ad_user_data: 'denied', ad_personalization: 'denied' };
      if (!analyticsStarted) {
        analyticsStarted = true;
        host.gtag('consent', 'default', consent);
        host.gtag('js', new Date());
        host.gtag('config', id, { allow_google_signals: false, allow_ad_personalization_signals: false });
        const script = doc.createElement('script');
        script.async = true;
        script.src = 'https://www.googletagmanager.com/gtag/js?id=' + encodeURIComponent(id);
        doc.head.append(script);
      } else host.gtag('consent', 'update', consent);
    }
    // If the consent provider is blocked, leave analytics unloaded.
    if (!host.cookieconsent?.initialise) return;
    host.cookieconsent.initialise({
      palette: { popup: { background: settings.popupColor }, button: { background: settings.buttonColor } },
      position: 'bottom-left', theme: 'classic', type: id ? 'opt-in' : 'info', revokable: true,
      content: { href: settings.privacyUrl, ...(id ? { message: 'Optional analytics helps us understand how this documentation is used. You can accept or decline.', allow: 'Allow analytics', deny: 'Decline' } : {}) },
      onInitialise(status) { analytics(status === 'allow'); },
      onStatusChange(status) { analytics(status === 'allow'); },
      onRevokeChoice() { analytics(false); },
      onPopupOpen() {
        if (!settings.externalLinksNewTab) return;
        for (const link of doc.querySelectorAll('.cc-window a[href]')) {
          const href = link.getAttribute('href');
          if (!/^https?:\/\//i.test(href) && !href.startsWith('//')) continue;
          link.setAttribute('target', '_blank'); link.setAttribute('rel', 'noopener noreferrer');
        }
      }
    });
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { initializeIntegrations };
  if (typeof document !== 'undefined') initializeIntegrations(document);
})();
