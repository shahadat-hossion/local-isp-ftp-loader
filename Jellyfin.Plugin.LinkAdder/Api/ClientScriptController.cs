using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LinkAdder.Api;

/// <summary>
/// Serves the small web-client enhancement used for the Add Media header button.
/// </summary>
[ApiController]
[Route("LocalIspFtpLoader")]
public sealed class ClientScriptController : ControllerBase
{
    /// <summary>
    /// Gets the browser script that adds the header button.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("client.js")]
    [Produces("application/javascript")]
    public ContentResult GetClientScript()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Content(ClientScript, "application/javascript", System.Text.Encoding.UTF8);
    }

    private const string ClientScript = """
        (() => {
            'use strict';

            if (window.__localIspFtpLoaderHeaderButton) return;
            window.__localIspFtpLoaderHeaderButton = true;

            const buttonId = 'localIspFtpLoaderHeaderButton';
            const containerId = 'localIspFtpLoaderHeaderButtonContainer';
            const widgetId = 'localIspFtpLoaderDiscoverWidget';
            const addMediaRoute = '#/configurationpage?name=localispmediaadd';
            const discoverRoute = '#/configurationpage?name=localispdiscovery';

            function isVisible(element) {
                return Boolean(element && element.isConnected && element.getClientRects().length);
            }

            function openAddMedia() {
                if (window.location.hash === addMediaRoute) {
                    window.dispatchEvent(new HashChangeEvent('hashchange'));
                } else {
                    window.location.hash = addMediaRoute;
                }
            }

            function openDiscover() {
                if (window.location.hash === discoverRoute) {
                    window.dispatchEvent(new HashChangeEvent('hashchange'));
                } else {
                    window.location.hash = discoverRoute;
                }
            }

            function createButton() {
                const button = document.createElement('button');
                button.id = buttonId;
                button.type = 'button';
                button.className = 'headerButton headerButtonRight paper-icon-button-light headerAddMediaButton';
                button.setAttribute('is', 'paper-icon-button-light');
                button.title = 'Add Media';
                button.setAttribute('aria-label', 'Add Media');
                button.innerHTML = '<span class="material-icons add" aria-hidden="true"></span>';
                button.addEventListener('click', openAddMedia);
                return button;
            }

            function findVisibleSearchButton() {
                const candidates = Array.from(document.querySelectorAll(
                    '.headerSearchButton, .MuiToolbar-root button[aria-label], .MuiToolbar-root button[title]'));
                return candidates.find((candidate) => {
                    if (!isVisible(candidate)) return false;
                    if (candidate.classList.contains('headerSearchButton')) return true;
                    const label = `${candidate.getAttribute('aria-label') || ''} ${candidate.title || ''}`;
                    return /search/i.test(label) || Boolean(candidate.querySelector('.material-icons.search'));
                }) || null;
            }

            function findHeaderTray() {
                const enhancedTray = window.JellyfinEnhanced?.helpers?.getHeaderButtonTray?.();
                if (enhancedTray && isVisible(enhancedTray)) return enhancedTray;

                const visibleLegacy = Array.from(document.querySelectorAll('.headerRight')).find(isVisible);
                if (visibleLegacy) return visibleLegacy;

                const searchButton = findVisibleSearchButton();
                if (searchButton?.parentElement) return searchButton.parentElement;

                const userMenuButton = document.querySelector('[aria-controls="app-user-menu"]');
                const toolbar = userMenuButton?.closest('.MuiToolbar-root')
                    || document.querySelector('.MuiAppBar-root .MuiToolbar-root');
                if (!toolbar) return null;

                let userMenuBox = userMenuButton;
                while (userMenuBox && userMenuBox.parentElement !== toolbar) {
                    userMenuBox = userMenuBox.parentElement;
                }

                const preceding = userMenuBox?.previousElementSibling;
                if (preceding && !preceding.matches('button, a, [role="button"]')) return preceding;
                return null;
            }

            function ensureButton() {
                const headerTray = findHeaderTray();
                if (!headerTray) return;

                const existingContainer = document.getElementById(containerId);
                if (existingContainer?.parentElement === headerTray && isVisible(existingContainer)) return;
                existingContainer?.remove();

                const container = document.createElement('div');
                container.id = containerId;
                container.appendChild(createButton());

                const searchButton = findVisibleSearchButton();
                if (searchButton?.parentElement === headerTray) {
                    headerTray.insertBefore(container, searchButton);
                } else {
                    headerTray.prepend(container);
                }
            }

            function isHomeRoute() {
                const hash = (window.location.hash || '').toLowerCase();
                return hash === '' || hash === '#/' || hash === '#/home' || hash.startsWith('#/home.html');
            }

            function value(item, name) {
                return item?.[name[0].toLowerCase() + name.slice(1)] ?? item?.[name];
            }

            function escapeHtml(text) {
                const element = document.createElement('span');
                element.textContent = String(text ?? '');
                return element.innerHTML;
            }

            function installWidgetStyles() {
                if (document.getElementById(`${widgetId}Styles`)) return;
                const style = document.createElement('style');
                style.id = `${widgetId}Styles`;
                style.textContent = `
                    #${widgetId}{margin:1.5em 0 2em;padding:0 3.3%;box-sizing:border-box}
                    #${widgetId} .liflWidgetHeader{display:flex;align-items:center;justify-content:space-between;gap:1em;margin-bottom:.7em}
                    #${widgetId} .liflWidgetTitle{font-size:1.45em;font-weight:600;margin:0}
                    #${widgetId} .liflViewAll{border:0;background:transparent;color:#00a4dc;font-weight:600;cursor:pointer;padding:.5em}
                    #${widgetId} .liflWidgetRow{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(135px,11vw);gap:1em;overflow-x:auto;overscroll-behavior-inline:contain;padding:.15em 0 .8em;scrollbar-width:thin}
                    #${widgetId} .liflWidgetCard{border:0;border-radius:.55em;background:#18263d;color:inherit;padding:0;overflow:hidden;text-align:left;cursor:pointer;min-width:0;box-shadow:0 2px 8px rgba(0,0,0,.2)}
                    #${widgetId} .liflWidgetPoster{display:block;width:100%;aspect-ratio:2/3;object-fit:cover;background:#101a2a}
                    #${widgetId} .liflWidgetBody{display:block;padding:.65em}
                    #${widgetId} .liflWidgetName{display:block;font-weight:700;line-height:1.25;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
                    #${widgetId} .liflWidgetMeta{display:block;margin-top:.3em;font-size:.82em;opacity:.72;text-transform:capitalize}
                    #${widgetId} .liflWidgetLoading{opacity:.72;padding:.75em 0}
                    @media(max-width:600px){#${widgetId}{padding:0 1em}#${widgetId} .liflWidgetRow{grid-auto-columns:minmax(125px,42vw);gap:.7em}}
                `;
                document.head.appendChild(style);
            }

            function balancedCatalog(items) {
                const result = [];
                for (const mediaType of ['movie', 'series', 'anime']) {
                    result.push(...items.filter(item => value(item, 'MediaType') === mediaType).slice(0, 4));
                }
                return result;
            }

            async function loadDiscoverWidget(widget) {
                const row = widget.querySelector('.liflWidgetRow');
                try {
                    const catalog = await ApiClient.ajax({
                        url: ApiClient.getUrl('Plugins/LinkAdder/Automation/Catalog'),
                        type: 'GET',
                        dataType: 'json'
                    });
                    const items = balancedCatalog(Array.isArray(catalog) ? catalog : []);
                    row.innerHTML = items.map(item => {
                        const title = value(item, 'Title');
                        const year = value(item, 'Year');
                        const poster = value(item, 'PosterUrl');
                        const type = value(item, 'MediaType') === 'series' ? 'show' : value(item, 'MediaType');
                        return `<button class="liflWidgetCard" type="button" title="${escapeHtml(title)}">`
                            + (poster ? `<img class="liflWidgetPoster" src="${escapeHtml(poster)}" alt="" loading="lazy">` : '<span class="liflWidgetPoster"></span>')
                            + `<span class="liflWidgetBody"><span class="liflWidgetName">${escapeHtml(title)}${year ? ` (${escapeHtml(year)})` : ''}</span>`
                            + `<span class="liflWidgetMeta">${escapeHtml(type)} · Rating ${Number(value(item, 'Rating') || 0).toFixed(1)}</span></span></button>`;
                    }).join('') || '<span class="liflWidgetLoading">No discovery titles are currently available.</span>';
                    row.querySelectorAll('.liflWidgetCard').forEach(card => card.addEventListener('click', openDiscover));
                } catch {
                    row.innerHTML = '<span class="liflWidgetLoading">Discover is temporarily unavailable.</span>';
                }
            }

            function findHomeSections() {
                const selectors = ['.homeSectionsContainer', '.homePage .sections', '.homePage .content-primary', '#indexPage .sections'];
                for (const selector of selectors) {
                    const element = Array.from(document.querySelectorAll(selector)).find(isVisible);
                    if (element) return element;
                }
                return null;
            }

            function ensureDiscoverWidget() {
                const existing = document.getElementById(widgetId);
                if (!isHomeRoute()) {
                    existing?.remove();
                    return;
                }
                if (existing?.isConnected) return;
                const homeSections = findHomeSections();
                if (!homeSections || !window.ApiClient) return;

                installWidgetStyles();
                const widget = document.createElement('section');
                widget.id = widgetId;
                widget.innerHTML = '<div class="liflWidgetHeader"><h2 class="liflWidgetTitle">Discover</h2><button class="liflViewAll" type="button">View all</button></div>'
                    + '<div class="liflWidgetRow"><span class="liflWidgetLoading">Loading recommendations…</span></div>';
                widget.querySelector('.liflViewAll').addEventListener('click', openDiscover);
                homeSections.prepend(widget);
                loadDiscoverWidget(widget);
            }

            function ensureEnhancements() {
                ensureButton();
                ensureDiscoverWidget();
            }

            const observer = new MutationObserver(ensureEnhancements);

            function start() {
                ensureEnhancements();
                observer.observe(document.body, { childList: true, subtree: true });
                window.addEventListener('hashchange', ensureEnhancements);
            }

            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', start, { once: true });
            } else {
                start();
            }
        })();
        """;
}
