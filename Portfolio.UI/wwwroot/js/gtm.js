window.portfolioGtm = {
    loaded: false,
    load: function (containerId) {
        if (window.portfolioGtm.loaded) {
            return;
        }
        if (!/^GTM-[A-Z0-9]+$/.test(containerId)) {
            console.warn('Google Tag Manager not loaded: invalid container id "' + containerId + '"');
            return;
        }
        window.portfolioGtm.loaded = true;
        window.dataLayer = window.dataLayer || [];
        window.dataLayer.push({ 'gtm.start': new Date().getTime(), event: 'gtm.js' });
        var script = document.createElement('script');
        script.async = true;
        script.src = 'https://www.googletagmanager.com/gtm.js?id=' + encodeURIComponent(containerId);
        document.head.appendChild(script);
    }
};
