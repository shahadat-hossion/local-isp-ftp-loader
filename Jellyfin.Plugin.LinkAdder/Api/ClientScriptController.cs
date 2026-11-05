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
            const addMediaRoute = '#/configurationpage?name=localispmediaadd';

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
                button.innerHTML = '<span class="material-icons add" aria-hidden="true">add</span>';
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

            const observer = new MutationObserver(ensureButton);

            function start() {
                ensureButton();
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
