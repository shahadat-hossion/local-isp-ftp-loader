using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LinkAdder.Api;

/// <summary>
/// Serves the web-client enhancement used for the unified Search &amp; Add button.
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
            const userMenuItemId = 'localIspFtpLoaderUserMenuItem';
            const navigationStyleId = 'localIspFtpLoaderNavigationStyles';
            const addMediaRoute = '#/search';
            const importLinkRoute = '#/configurationpage?name=localispimportlink';
            const discoverRoute = '#/configurationpage?name=localispdiscovery';
            let replaceDefaultSearch = null;

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

            function createSubmenuLink(template, id, routeName, label, iconName) {
                const link = template.cloneNode(true);
                link.id = id;
                link.setAttribute('href', `#/configurationpage?name=${routeName}`);
                const labelNode = Array.from(link.querySelectorAll('*')).find((element) =>
                    element.children.length === 0 && ['Discover', 'Import Link'].includes(element.textContent?.trim()));
                if (labelNode) labelNode.textContent = label;
                else link.appendChild(Object.assign(document.createElement('span'), { textContent: label }));
                const icon = link.querySelector('.material-icons');
                if (icon) {
                    icon.className = `material-icons ${iconName}`;
                    icon.textContent = '';
                }
                return link;
            }

            function ensureManagedSubmenuLinks(children, template) {
                if (!children.querySelector('#localIspAddMediaMenuLink')) {
                    children.appendChild(createSubmenuLink(template, 'localIspAddMediaMenuLink', 'localispmediaadd', 'Add Media', 'add'));
                }
                if (!children.querySelector('#localIspFtpServersMenuLink')) {
                    children.appendChild(createSubmenuLink(template, 'localIspFtpServersMenuLink', 'localispservers', 'FTP Servers', 'storage'));
                }
            }

            function ensureNavigationGroup() {
                ensureNavigationStyles();
                const links = [
                    routeLink('localispdiscovery'),
                    routeLink('localispimportlink')
                ].filter(Boolean);
                if (links.length < 2) return;

                const existingGroup = links.map((link) => link.closest('.localIspPluginGroup')).find(Boolean);
                if (existingGroup) {
                    const children = existingGroup.querySelector('.localIspPluginChildren');
                    if (!children) return;
                    for (const link of links) {
                        if (!children.contains(link)) children.appendChild(link);
                    }
                    ensureManagedSubmenuLinks(children, links[0]);
                    return;
                }

                const navigation = links[0].parentElement;
                if (!navigation || links.some((link) => link.parentElement !== navigation)) return;

                const group = document.createElement('div');
                group.className = 'localIspPluginGroup';
                const parent = document.createElement('button');
                parent.type = 'button';
                parent.className = 'localIspPluginParent';
                parent.setAttribute('aria-expanded', 'true');
                parent.innerHTML = '<span class="material-icons storage" aria-hidden="true"></span><span>FTP Loader</span><span class="material-icons expand_more" aria-hidden="true"></span>';
                const children = document.createElement('div');
                children.className = 'localIspPluginChildren';
                parent.addEventListener('click', () => {
                    const collapsed = group.classList.toggle('localIspCollapsed');
                    parent.setAttribute('aria-expanded', String(!collapsed));
                });
                navigation.insertBefore(group, links[0]);
                group.append(parent, children);
                children.append(...links);
                ensureManagedSubmenuLinks(children, links[0]);
            }

            function isVisible(element) {
                return Boolean(element && element.isConnected && element.getClientRects().length);
            }

            function openAddMedia() {
                if (window.location.hash.startsWith(addMediaRoute)) {
                    document.querySelector('#searchTextInput')?.focus();
                    return;
                }
                window.location.hash = addMediaRoute;
            }

            function openImportLink() {
                window.location.hash = importLinkRoute;
            }

            function ensureUserMenuItem() {
                const menu = document.getElementById('app-user-menu');
                if (!menu || !isVisible(menu) || menu.querySelector(`#${userMenuItemId}`)) return;

                const enhancedLabel = Array.from(menu.querySelectorAll('*')).find((element) =>
                    element.children.length === 0 && element.textContent?.trim() === 'Jellyfin Enhanced');
                const enhancedItem = enhancedLabel?.closest('[role="menuitem"], a, button, li');
                if (!enhancedItem) return;

                const item = enhancedItem.cloneNode(true);
                item.id = userMenuItemId;
                item.removeAttribute('href');
                item.setAttribute('role', 'menuitem');
                item.setAttribute('tabindex', '0');

                const label = Array.from(item.querySelectorAll('*')).find((element) =>
                    element.children.length === 0 && element.textContent?.trim() === 'Jellyfin Enhanced');
                if (label) label.textContent = 'FTP Loader';

                const icon = item.querySelector('svg, .material-icons');
                if (icon) {
                    const storageIcon = document.createElement('span');
                    storageIcon.className = 'material-icons storage';
                    storageIcon.setAttribute('aria-hidden', 'true');
                    storageIcon.textContent = 'storage';
                    icon.replaceWith(storageIcon);
                }

                const activate = (event) => {
                    if (event.type === 'keydown' && event.key !== 'Enter' && event.key !== ' ') return;
                    event.preventDefault();
                    event.stopPropagation();
                    document.querySelector('[aria-controls="app-user-menu"]')?.click();
                    openImportLink();
                };
                item.addEventListener('click', activate);
                item.addEventListener('keydown', activate);
                enhancedItem.insertAdjacentElement('afterend', item);
            }

            function createButton() {
                const button = document.createElement('button');
                button.id = buttonId;
                button.type = 'button';
                button.className = 'headerButton headerButtonRight paper-icon-button-light headerAddMediaButton';
                button.setAttribute('is', 'paper-icon-button-light');
                button.title = 'Search & Add Media';
                button.setAttribute('aria-label', 'Search & Add Media');
                button.innerHTML = '<span class="material-icons search" aria-hidden="true"></span>';
                button.addEventListener('click', openAddMedia);
                return button;
            }

            function findVisibleSearchButton() {
                const candidates = Array.from(document.querySelectorAll(
                    '.headerSearchButton, .MuiToolbar-root button[aria-label], .MuiToolbar-root button[title]'))
                    .filter((candidate) => candidate.id !== buttonId);
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

            function applySearchReplacement() {
                const candidates = Array.from(document.querySelectorAll(
                    '.headerSearchButton, .MuiToolbar-root [aria-label], .MuiToolbar-root [title], [data-local-isp-hidden="true"]'))
                    .filter((candidate) => {
                        if (candidate.id === buttonId) return false;
                        const label = `${candidate.getAttribute('aria-label') || ''} ${candidate.title || ''}`;
                        return candidate.classList.contains('headerSearchButton')
                            || /search/i.test(label)
                            || candidate.dataset.localIspHidden === 'true';
                    });
                for (const nativeSearch of candidates) {
                    if (replaceDefaultSearch === true) {
                        nativeSearch.dataset.localIspHidden = 'true';
                        nativeSearch.style.setProperty('display', 'none', 'important');
                    } else if (nativeSearch.dataset.localIspHidden === 'true') {
                        nativeSearch.style.removeProperty('display');
                        delete nativeSearch.dataset.localIspHidden;
                    }
                }
            }

            async function loadSettings() {
                try {
                    const result = await ApiClient.ajax({
                        url: ApiClient.getUrl('Plugins/LinkAdder/SearchUiSettings'),
                        type: 'GET',
                        dataType: 'json'
                    });
                    replaceDefaultSearch = result.replaceDefaultSearch ?? result.ReplaceDefaultSearch ?? true;
                } catch (_) {
                    replaceDefaultSearch = false;
                }
                ensureEnhancements();
            }

            const remoteSectionId = 'localIspNativeSearchResults';
            const remoteStyleId = 'localIspNativeSearchStyles';
            let nativeSearchInput = null;
            let nativeSearchTimer = 0;
            let nativeSearchSequence = 0;
            let libraryOptions = null;
            let nativeLocalTitles = new Set();
            let nativeLocalItems = [];
            let nativeRemoteItems = [];

            const value = (item, name) => item?.[name[0].toLowerCase() + name.slice(1)] ?? item?.[name];
            const escapeHtml = (text) => String(text ?? '').replace(/[&<>"']/g, (character) => ({
                '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
            })[character]);
            const normalizeTitle = (text) => String(text ?? '').toLowerCase()
                .replace(/\b(19|20)\d{2}\b/g, ' ')
                .replace(/\b(2160p|1080p|720p|480p|4k|uhd|bluray|web[- .]?dl|webrip|brrip|hdrip|dvdrip|x26[45]|hevc|aac|esub|msubs?)\b.*$/i, ' ')
                .replace(/[^\p{L}\p{N}]+/gu, ' ').trim();

            async function pluginCall(path, method = 'GET', body) {
                return ApiClient.ajax({
                    url: ApiClient.getUrl('Plugins/LinkAdder/' + path),
                    type: method,
                    dataType: 'json',
                    contentType: 'application/json',
                    data: body ? JSON.stringify(body) : undefined
                });
            }

            function ensureNativeSearchStyles() {
                if (document.getElementById(remoteStyleId)) return;
                const style = document.createElement('style');
                style.id = remoteStyleId;
                style.textContent = `
                    #${remoteSectionId} { position:fixed;z-index:20;inset:4.2em 0 0;overflow:auto;background:#101010;padding:1.2em 0 5em; }
                    #${remoteSectionId} .localIspNativeFields { max-width:54em;margin:0 auto 1.5em;padding:0 3.3%; }
                    #${remoteSectionId} .localIspNativeInputRow { display:flex;align-items:center;gap:.7em; }
                    #${remoteSectionId} .localIspNativeInputRow .inputContainer { flex:1;margin:0; }
                    #${remoteSectionId} .localIspResultSection { margin-top:1.6em; }
                    #${remoteSectionId} .localIspRemoteGrid { display:grid;grid-template-columns:repeat(auto-fill,minmax(10.5em,1fr));gap:1.25em;padding:0 3.3%; }
                    #${remoteSectionId} .localIspRemoteCard { min-width:0;max-width:14em; }
                    #${remoteSectionId} .localIspPoster { display:block;position:absolute;inset:0;background-position:center;background-size:cover;background-color:rgba(255,255,255,.06);border-radius:.3em; }
                    #${remoteSectionId} .localIspPosterFallback { display:grid;place-items:center;color:rgba(255,255,255,.55);font-size:3em; }
                    #${remoteSectionId} .localIspSource { overflow:hidden;text-overflow:ellipsis;white-space:nowrap;opacity:.7;font-size:.86em; }
                    #${remoteSectionId} .localIspAddButton { width:100%;margin:.55em 0 0!important; }
                    #${remoteSectionId} .localIspSearchMessage { padding:0 3.3%;opacity:.75; }
                    @media(max-width:600px){#${remoteSectionId} .localIspRemoteGrid{grid-template-columns:repeat(2,minmax(0,1fr));gap:.7em;padding:0 1em}}
                `;
                document.head.appendChild(style);
            }

            function localSearchTitles() { return nativeLocalTitles; }

            function nativeSection() {
                let section = document.getElementById(remoteSectionId);
                if (section) return section;
                section = document.createElement('section');
                section.id = remoteSectionId;
                section.innerHTML = '<div class="localIspNativeFields"><div class="localIspNativeInputRow"><span class="material-icons search" aria-hidden="true"></span><div class="inputContainer"><input id="localIspNativeQuery" class="emby-input" type="text" placeholder="Search" autocomplete="off"></div></div></div><div id="localIspCombinedResults" class="localIspResultSection"><h2 class="sectionTitle sectionTitle-cards focuscontainer-x padded-left padded-right">Search Results</h2><div class="localIspSearchMessage">Enter at least two characters to search your library and configured media servers.</div></div>';
                document.body.appendChild(section);
                return section;
            }

            function localPoster(item) {
                const tag = item.ImageTags?.Primary;
                return tag ? ApiClient.getScaledImageUrl(item.Id, { type:'Primary', maxWidth:360, tag }) : '';
            }

            function localCard(item) {
                const image = localPoster(item);
                const poster = image ? `<span class="localIspPoster" role="img" aria-label="${escapeHtml(item.Name)} poster" style="background-image:url('${escapeHtml(image).replace(/'/g, '%27')}')"></span>`
                    : '<span class="localIspPoster localIspPosterFallback material-icons movie" aria-hidden="true"></span>';
                return `<div class="card overflowPortraitCard localIspRemoteCard"><div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowPortrait">${poster}</div></div><div class="cardText cardTextCentered">${escapeHtml(item.Name)}</div><div class="cardText cardTextCentered localIspSource">${escapeHtml(item.Type)}${item.ProductionYear ? ' · ' + item.ProductionYear : ''}</div><button is="emby-button" class="raised button-submit localIspAddButton localIspOpenButton" data-id="${escapeHtml(item.Id)}"><span>${item.Type === 'Movie' || item.Type === 'Episode' ? 'Play' : 'Open'}</span></button></div></div>`;
            }

            function renderLocalResults(items) {
                const seen = new Set();
                nativeLocalItems = items.filter((item) => {
                    const key = normalizeTitle(item.SeriesName || item.Name);
                    return key && !seen.has(key) && seen.add(key);
                });
                nativeLocalTitles = new Set(nativeLocalItems.map((item) => normalizeTitle(item.SeriesName || item.Name)));
                renderCombinedResults();
            }

            function remoteCard(item, index, exists) {
                const poster = item.posterUrl
                    ? `<span class="localIspPoster" role="img" aria-label="${escapeHtml(item.name)} poster" style="background-image:url('${escapeHtml(item.posterUrl).replace(/'/g, '%27')}')"></span>`
                    : '<span class="localIspPoster localIspPosterFallback material-icons movie" aria-hidden="true"></span>';
                const action = exists ? 'Already in Library' : 'Add & Play';
                return `<div class="card overflowPortraitCard localIspRemoteCard" data-index="${index}">
                    <div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowPortrait">${poster}</div></div>
                    <div class="cardText cardTextCentered">${escapeHtml(item.name)}</div><div class="cardText cardTextCentered localIspSource">${escapeHtml(item.source)}</div>
                    <button is="emby-button" class="raised ${exists ? '' : 'button-submit'} localIspAddButton" data-index="${index}" ${exists ? 'disabled' : ''}><span>${action}</span></button></div></div>`;
            }

            function renderRemoteResults(items) {
                const seen = new Set();
                nativeRemoteItems = items.filter((item) => {
                    const key = normalizeTitle(item.name);
                    return key && !seen.has(key) && seen.add(key);
                });
                renderCombinedResults();
            }

            function renderCombinedResults() {
                const section = document.querySelector('#localIspCombinedResults');
                if (!section) return;
                const localKeys = localSearchTitles();
                const remoteOnly = nativeRemoteItems.filter((item) => !localKeys.has(normalizeTitle(item.name)));
                const cards = nativeLocalItems.map(localCard).join('')
                    + remoteOnly.map((item, index) => remoteCard(item, index, false)).join('');
                section.innerHTML = '<h2 class="sectionTitle sectionTitle-cards focuscontainer-x padded-left padded-right">Search Results</h2>'
                    + (cards ? `<div class="localIspRemoteGrid">${cards}</div>` : '<div class="localIspSearchMessage">No matching titles found.</div>');
                section.querySelectorAll('.localIspOpenButton').forEach((button) => button.addEventListener('click', async () => {
                    const item = await ApiClient.getItem(ApiClient.getCurrentUserId(), button.dataset.id);
                    openIndexedItem(item);
                }));
                section.querySelectorAll('.localIspAddButton:not(.localIspOpenButton):not([disabled])').forEach((button) => {
                    button.addEventListener('click', () => addRemoteItem(remoteOnly[Number(button.dataset.index)], button));
                });
            }

            async function searchRemoteSources(query) {
                const sequence = ++nativeSearchSequence;
                const section = document.querySelector('#localIspCombinedResults');
                if (!section) return;
                if (query.length < 2) {
                    nativeLocalItems = []; nativeRemoteItems = []; nativeLocalTitles = new Set();
                    section.innerHTML = '<h2 class="sectionTitle sectionTitle-cards focuscontainer-x padded-left padded-right">Search Results</h2><div class="localIspSearchMessage">Enter at least two characters to search your library and configured media servers.</div>';
                    return;
                }
                section.innerHTML = '<h2 class="sectionTitle sectionTitle-cards focuscontainer-x padded-left padded-right">Search Results</h2><div class="localIspSearchMessage">Searching your library and media servers…</div>';
                const mediaTypes = ['movie', 'series', 'anime'];
                const settled = await Promise.allSettled(mediaTypes.map((mediaType) => pluginCall('Remote/Search', 'POST', { mediaType, query })
                    .then((rows) => rows.map((row) => ({ name:value(row, 'Name'), url:value(row, 'Url'), source:value(row, 'Source'), posterUrl:value(row, 'PosterUrl'), mediaType })) )));
                if (sequence !== nativeSearchSequence || !window.location.hash.startsWith('#/search')) return;
                const results = settled.flatMap((result) => result.status === 'fulfilled' ? result.value : [])
                    .filter((item, index, all) => all.findIndex((other) => other.url === item.url && other.mediaType === item.mediaType) === index);
                renderRemoteResults(results);
            }

            async function searchLocalLibrary(query, sequence) {
                const result = await ApiClient.getItems(ApiClient.getCurrentUserId(), {
                    SearchTerm:query, IncludeItemTypes:'Movie,Series,Episode', Recursive:true, Limit:30,
                    Fields:'PrimaryImageAspectRatio,ProductionYear,SeriesName,Path', ImageTypeLimit:1, EnableImageTypes:'Primary'
                });
                if (sequence !== nativeSearchSequence || !window.location.hash.startsWith('#/search')) return;
                renderLocalResults(result.Items || []);
            }

            async function libraries() {
                if (libraryOptions) return libraryOptions;
                const rows = await pluginCall('Libraries');
                libraryOptions = rows.map((row) => ({ name:value(row, 'Name'), path:value(row, 'Path'), mediaType:value(row, 'MediaType') }));
                return libraryOptions;
            }

            async function waitForIndexedItem(item) {
                const wanted = normalizeTitle(item.name);
                const deadline = Date.now() + 90000;
                while (Date.now() < deadline) {
                    const result = await ApiClient.getItems(ApiClient.getCurrentUserId(), {
                        SearchTerm:item.name, IncludeItemTypes:item.mediaType === 'movie' ? 'Movie' : 'Series', Recursive:true, Limit:20, Fields:'Path,ProductionYear'
                    });
                    const match = (result.Items || []).find((entry) => normalizeTitle(entry.Name) === wanted);
                    if (match) return match;
                    await new Promise((resolve) => setTimeout(resolve, 2500));
                }
                return null;
            }

            function openIndexedItem(item) {
                if (window.Dashboard?.navigate) Dashboard.navigate('details?id=' + encodeURIComponent(item.Id));
                else window.location.hash = '#/details?id=' + encodeURIComponent(item.Id);
            }

            async function addRemoteItem(item, button) {
                const label = button.querySelector('span');
                const original = label.textContent;
                button.disabled = true;
                try {
                    label.textContent = 'Adding…';
                    await pluginCall('Add', 'POST', { url:item.url, libraryPath:null, mediaType:item.mediaType, title:item.name });
                    label.textContent = 'Waiting for Jellyfin…';
                    const indexed = await waitForIndexedItem(item);
                    if (!indexed) { label.textContent = 'Added'; button.title = 'Jellyfin is still indexing this title.'; return; }
                    label.textContent = item.mediaType === 'movie' ? 'Play' : 'Open';
                    openIndexedItem(indexed);
                } catch (error) {
                    label.textContent = original;
                    button.disabled = false;
                    alert(error?.message || 'The title could not be added.');
                }
            }

            function attachNativeSearch() {
                if (!window.location.hash.startsWith('#/search')) {
                    document.getElementById(remoteSectionId)?.remove();
                    nativeSearchInput = null;
                    return;
                }
                ensureNativeSearchStyles();
                nativeSection();
                const input = document.querySelector('#localIspNativeQuery');
                if (!input || input === nativeSearchInput) return;
                nativeSearchInput = input;
                input.addEventListener('input', () => {
                    clearTimeout(nativeSearchTimer);
                    nativeSearchTimer = setTimeout(() => {
                        const query = input.value.trim();
                        if (query.length < 2) {
                            nativeLocalTitles = new Set();
                            searchRemoteSources(query);
                            return;
                        }
                        nativeLocalItems = [];
                        nativeRemoteItems = [];
                        nativeLocalTitles = new Set();
                        searchRemoteSources(query);
                        searchLocalLibrary(query, nativeSearchSequence);
                    }, 450);
                });
                setTimeout(() => input.focus(), 50);
            }

            let scheduled = false;
            const ensureEnhancements = () => {
                if (scheduled) return;
                scheduled = true;
                requestAnimationFrame(() => {
                    scheduled = false;
                    ensureButton();
                    ensureUserMenuItem();
                    applySearchReplacement();
                    ensureNavigationGroup();
                    attachNativeSearch();
                });
            };
            const observer = new MutationObserver(ensureEnhancements);

            function start() {
                ensureEnhancements();
                loadSettings();
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
