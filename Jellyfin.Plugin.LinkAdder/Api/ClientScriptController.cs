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

            document.getElementById('localIspFtpLoaderDiscoverWidget')?.remove();
            document.getElementById('localIspFtpLoaderDiscoverWidgetStyles')?.remove();

            if (window.__localIspFtpLoaderHeaderButton) return;
            window.__localIspFtpLoaderHeaderButton = true;

            const buttonId = 'localIspFtpLoaderHeaderButton';
            const containerId = 'localIspFtpLoaderHeaderButtonContainer';
            const navigationStyleId = 'localIspFtpLoaderNavigationStyles';
            const addMediaRoute = '#/configurationpage?name=localispmediaadd';
            const discoverRoute = '#/configurationpage?name=localispdiscovery';

            function ensureNavigationStyles() {
                if (document.getElementById(navigationStyleId)) return;
                const style = document.createElement('style');
                style.id = navigationStyleId;
                style.textContent = `
                    .localIspPluginGroup { margin: .35rem 0; }
                    .localIspPluginParent { display:flex;align-items:center;gap:.85rem;width:100%;padding:.8rem 1.05rem;border:0;background:transparent;color:inherit;font:inherit;font-weight:600;text-align:left;cursor:pointer; }
                    .localIspPluginParent:hover { background:rgba(255,255,255,.06); }
                    .localIspPluginParent .material-icons:last-child { margin-left:auto;transition:transform .2s ease; }
                    .localIspPluginGroup.localIspCollapsed .localIspPluginParent .material-icons:last-child { transform:rotate(-90deg); }
                    .localIspPluginChildren > a { padding-left:3.1rem!important; }
                    .localIspPluginGroup.localIspCollapsed .localIspPluginChildren { display:none; }
                `;
                document.head.appendChild(style);
            }

            function routeLink(routeName) {
                return Array.from(document.querySelectorAll('a[href]')).find((link) =>
                    link.getAttribute('href')?.toLowerCase().includes(`name=${routeName}`));
            }

            function ensureNavigationGroup() {
                ensureNavigationStyles();
                const discover = routeLink('localispdiscovery');
                const addMedia = routeLink('localispmediaadd');
                if (!discover || !addMedia) return;
                if (discover.closest('.localIspPluginGroup') && addMedia.closest('.localIspPluginGroup')) return;
                if (discover.parentElement !== addMedia.parentElement) return;

                const navigation = discover.parentElement;
                const group = document.createElement('div');
                group.className = 'localIspPluginGroup';
                const parent = document.createElement('button');
                parent.type = 'button';
                parent.className = 'localIspPluginParent';
                parent.setAttribute('aria-expanded', 'true');
                parent.innerHTML = '<span class="material-icons storage" aria-hidden="true"></span><span>Local ISP FTP Loader</span><span class="material-icons expand_more" aria-hidden="true"></span>';
                const children = document.createElement('div');
                children.className = 'localIspPluginChildren';
                parent.addEventListener('click', () => {
                    const collapsed = group.classList.toggle('localIspCollapsed');
                    parent.setAttribute('aria-expanded', String(!collapsed));
                });
                navigation.insertBefore(group, discover);
                group.append(parent, children);
                children.append(discover, addMedia);
            }

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
                document.getElementById('localIspFtpLoaderDiscoverWidget')?.remove();
                document.getElementById('localIspFtpLoaderDiscoverWidgetStyles')?.remove();
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

            let scheduled = false;
            const ensureEnhancements = () => {
                if (scheduled) return;
                scheduled = true;
                requestAnimationFrame(() => {
                    scheduled = false;
                    ensureButton();
                    ensureNavigationGroup();
                });
            };
            const observer = new MutationObserver(ensureEnhancements);

            function start() {
                ensureEnhancements();
                observer.observe(document.body, { childList: true, subtree: true });
            }

            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', start, { once: true });
            } else {
                start();
            }
        })();
        """;
}
