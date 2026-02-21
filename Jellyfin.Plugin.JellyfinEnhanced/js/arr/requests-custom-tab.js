/**
 * Requests Custom Tab
 * Creates <div class="jellyfinenhanced requests"></div> for CustomTabs plugin
 */

(function () {
  'use strict';

  if (!window.JellyfinEnhanced?.pluginConfig?.DownloadsPageEnabled) {
    return;
  }

  // Only initialize if custom tabs are enabled
  if (!window.JellyfinEnhanced?.pluginConfig?.DownloadsUseCustomTabs) {
    return;
  }

  // Inject custom styles
  const style = document.createElement('style');
  style.textContent = `
    .jellyfinenhanced.requests {
      padding: 12px 3vw;
    }
    .backgroundContainer.withBackdrop:has(~ .mainAnimatedPages #indexPage .tabContent.is-active .jellyfinenhanced.requests) {
      background: rgba(0, 0, 0, 0.7) !important;
    }
  `;
  document.head.appendChild(style);

  let activeContainer = null;
  let activeInstanceId = null;
  let renderTimer = null;
  let pendingTabIndex = null;
  let pendingTabLabel = '';
  let pendingTabAt = 0;
  const managedTabInfo = {
    loaded: false,
    indices: new Set(),
    buttonIds: new Set(),
    instanceByIndex: new Map()
  };

  // Wait for JE.downloadsPage to be ready
  function waitForDownloads(callback) {
    const check = setInterval(() => {
      const JE = window.JE || window.JellyfinEnhanced;
      if (JE?.downloadsPage) {
        clearInterval(check);
        callback(JE);
      }
    }, 100);
  }

  function getInstanceId(container) {
    const raw = container.getAttribute('data-je-seerr-instance-id');
    return raw ? raw.trim() : '';
  }

  function normalizeLabel(value) {
    return (value || '').trim().toLowerCase();
  }

  function getConfiguredInstanceNames() {
    const raw = window.JellyfinEnhanced?.pluginConfig?.JellyseerrInstanceNames || '';
    return raw
      .split(/\r?\n/)
      .map((v) => v.trim())
      .filter(Boolean);
  }

  function parseInstanceIdFromHtml(html) {
    if (!html) {
      return '';
    }

    const match = /data-je-seerr-instance-id\s*=\s*["']([^"']+)["']/i.exec(html);
    return match && match[1] ? match[1].trim() : '';
  }

  function isManagedRequestsHtml(html) {
    if (!html) {
      return false;
    }

    return /data-je-managed\s*=\s*["']requests-seerr["']/i.test(html)
      || /class\s*=\s*["'][^"']*\bjellyfinenhanced\b[^"']*\brequests\b[^"']*["']/i.test(html);
  }

  async function loadManagedTabMetadata() {
    if (managedTabInfo.loaded) {
      return;
    }

    managedTabInfo.loaded = true;

    try {
      const requestUrl = (typeof ApiClient !== 'undefined' && typeof ApiClient.getUrl === 'function')
        ? ApiClient.getUrl('/CustomTabs/Config')
        : '/CustomTabs/Config';

      const response = await fetch(requestUrl, { credentials: 'same-origin' });
      if (!response.ok) {
        return;
      }

      const configs = await response.json();
      if (!Array.isArray(configs)) {
        return;
      }

      configs.forEach((entry, index) => {
        const html = entry?.ContentHtml || entry?.contentHtml || '';
        if (!isManagedRequestsHtml(html)) {
          return;
        }

        const tabIndex = index + 2;
        managedTabInfo.indices.add(tabIndex);
        managedTabInfo.buttonIds.add(`customTabButton_${index}`);

        const parsedInstanceId = parseInstanceIdFromHtml(html);
        if (parsedInstanceId) {
          managedTabInfo.instanceByIndex.set(tabIndex, parsedInstanceId);
        }
      });
    } catch (_) {
      // Ignore; fallbacks below still apply.
    }
  }

  function isPendingTabFresh() {
    return Number.isFinite(pendingTabIndex)
      && pendingTabAt > 0
      && (Date.now() - pendingTabAt) < 2000;
  }

  function rememberPendingTab(button) {
    const index = getActiveTabIndex(button);
    if (index === null) {
      return;
    }

    pendingTabIndex = index;
    pendingTabLabel = normalizeLabel(
      button?.querySelector('.emby-button-foreground')?.textContent || button?.textContent || '',
    );
    pendingTabAt = Date.now();
  }

  function clearPendingTab() {
    pendingTabIndex = null;
    pendingTabLabel = '';
    pendingTabAt = 0;
  }

  function getManagedButtonCandidatesByIndex(index) {
    return Array.from(document.querySelectorAll(`.emby-tabs-slider .emby-tab-button[data-index="${index}"]`))
      .filter(isLikelyManagedRequestsButton);
  }

  function findPendingManagedButton() {
    if (!isPendingTabFresh()) {
      clearPendingTab();
      return null;
    }

    const candidates = getManagedButtonCandidatesByIndex(pendingTabIndex);
    if (!candidates.length) {
      return null;
    }

    const labelMatches = candidates.find((candidate) => {
      const text = normalizeLabel(
        candidate.querySelector('.emby-button-foreground')?.textContent || candidate.textContent || '',
      );
      return pendingTabLabel && text === pendingTabLabel;
    });

    return labelMatches || candidates[0] || null;
  }

  function setManagedButtonActive(button) {
    if (!button) {
      return;
    }

    document.querySelectorAll('.tabs-viewmenubar .emby-tab-button-active, .emby-tabs-slider .emby-tab-button-active')
      .forEach((node) => node.classList.remove('emby-tab-button-active'));
    button.classList.add('emby-tab-button-active');
  }

  function ensureTabContentForIndex(tabIndex, activeButton) {
    if (tabIndex === null) {
      return null;
    }

    let tabContent = document.querySelector(`#indexPage .tabContent[data-index="${tabIndex}"]`)
      || document.querySelector(`.tabContent[data-index="${tabIndex}"]`);

    if (!tabContent) {
      if (!isLikelyManagedRequestsButton(activeButton)) {
        return null;
      }

      const indexPage = document.querySelector('#indexPage');
      if (!indexPage) {
        return null;
      }

      tabContent = document.createElement('div');
      tabContent.className = 'tabContent pageTabContent';
      tabContent.id = `je-recovered-requests-tab-${tabIndex}`;
      tabContent.setAttribute('data-index', String(tabIndex));
      indexPage.appendChild(tabContent);
    }

    return tabContent;
  }

  function setTabContentActive(tabContent) {
    if (!tabContent) {
      return;
    }

    document.querySelectorAll('.tabContent.pageTabContent.is-active').forEach((node) => {
      if (node !== tabContent) {
        node.classList.remove('is-active');
      }
    });
    tabContent.classList.add('is-active');
    tabContent.classList.remove('hide');
    tabContent.style.display = '';
  }

  function stabilizeManagedTabSelection(button) {
    if (!isLikelyManagedRequestsButton(button)) {
      return;
    }

    rememberPendingTab(button);
    const tabIndex = getActiveTabIndex(button);
    if (tabIndex === null) {
      return;
    }

    const applySelection = () => {
      const currentButton = findPendingManagedButton() || button;
      const currentIndex = getActiveTabIndex(currentButton);
      if (currentIndex === null) {
        return;
      }

      const tabContent = ensureTabContentForIndex(currentIndex, currentButton);
      if (!tabContent) {
        return;
      }

      setManagedButtonActive(currentButton);
      setTabContentActive(tabContent);
    };

    applySelection();
    setTimeout(applySelection, 120);
    setTimeout(applySelection, 320);
  }

  function getActiveTabButton() {
    const active = document.querySelector('.tabs-viewmenubar .emby-tab-button-active')
      || document.querySelector('.emby-tabs-slider .emby-tab-button-active');
    if (isLikelyManagedRequestsButton(active)) {
      return active;
    }

    return findPendingManagedButton() || active;
  }

  function getActiveTabIndex(button) {
    if (!button) {
      return null;
    }

    const raw = button.getAttribute('data-index');
    const parsed = raw === null ? NaN : Number.parseInt(raw, 10);
    return Number.isFinite(parsed) ? parsed : null;
  }

  function isLikelyManagedRequestsButton(button) {
    if (!button) {
      return false;
    }

    const id = button.id || '';
    const index = getActiveTabIndex(button);
    if (id && managedTabInfo.buttonIds.has(id)) {
      return true;
    }
    if (index !== null && managedTabInfo.indices.has(index)) {
      return true;
    }

    if (id.startsWith('customTabButton_')) {
      return true;
    }

    const text = normalizeLabel(
      button.querySelector('.emby-button-foreground')?.textContent || button.textContent || '',
    );
    if (!text) {
      return false;
    }

    const names = getConfiguredInstanceNames().map(normalizeLabel);
    if (names.length > 0) {
      return names.includes(text);
    }

    return text === 'requests';
  }

  function inferInstanceId(button, tabIndex) {
    if (Number.isFinite(tabIndex)) {
      const fromManagedMap = managedTabInfo.instanceByIndex.get(tabIndex);
      if (fromManagedMap) {
        return fromManagedMap;
      }
    }

    const text = normalizeLabel(
      button?.querySelector('.emby-button-foreground')?.textContent || button?.textContent || '',
    );
    const names = getConfiguredInstanceNames();
    const namedIndex = names.findIndex((name) => normalizeLabel(name) === text);
    if (namedIndex >= 0) {
      return `seerr-${namedIndex + 1}`;
    }

    const buttonId = button?.id || '';
    const match = /customTabButton_(\d+)/.exec(buttonId);
    if (match) {
      const indexFromButton = Number.parseInt(match[1], 10);
      if (Number.isFinite(indexFromButton)) {
        return `seerr-${indexFromButton + 1}`;
      }
    }

    if (Number.isFinite(tabIndex) && tabIndex > 1) {
      return `seerr-${tabIndex - 1}`;
    }

    return '';
  }

  function ensureRequestsContainerForActiveTab() {
    const activeButton = getActiveTabButton();
    const activeTabIndex = getActiveTabIndex(activeButton);
    if (activeTabIndex === null) {
      return null;
    }

    const tabContent = ensureTabContentForIndex(activeTabIndex, activeButton);
    if (!tabContent) {
      return null;
    }

    let requestsContainer = tabContent.querySelector('.jellyfinenhanced.requests');
    if (!requestsContainer) {
      if (!isLikelyManagedRequestsButton(activeButton)) {
        return null;
      }

      requestsContainer = document.createElement('div');
      requestsContainer.className = 'jellyfinenhanced requests';
      requestsContainer.setAttribute('data-je-managed', 'requests-seerr');

      const inferredInstanceId = inferInstanceId(activeButton, activeTabIndex);
      if (inferredInstanceId) {
        requestsContainer.setAttribute('data-je-seerr-instance-id', inferredInstanceId);
      }

      tabContent.appendChild(requestsContainer);
    }

    setTabContentActive(tabContent);

    return requestsContainer;
  }

  function isContainerVisible(container) {
    if (!container || !document.contains(container)) {
      return false;
    }

    if (container.classList.contains('hide')) {
      return false;
    }

    const style = window.getComputedStyle(container);
    if (style.display === 'none' || style.visibility === 'hidden') {
      return false;
    }

    const rect = container.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function getActiveContainer() {
    const fromActiveTab = ensureRequestsContainerForActiveTab();
    if (fromActiveTab) {
      return fromActiveTab;
    }

    const candidates = Array.from(document.querySelectorAll('.jellyfinenhanced.requests'));
    if (!candidates.length) {
      return null;
    }

    // Preferred: explicit active-tab marker
    const explicit = document.querySelector('.tabContent.is-active .jellyfinenhanced.requests');
    if (explicit) {
      return explicit;
    }

    // Fallback: whichever requests container is visible
    const visible = candidates.find(isContainerVisible);
    if (visible) {
      return visible;
    }

    // If only one exists, use it.
    if (candidates.length === 1) {
      return candidates[0];
    }

    // Preserve current active container if still in DOM.
    if (activeContainer && document.contains(activeContainer)) {
      return activeContainer;
    }

    return null;
  }

  // Render downloads in the currently active custom tab container
  function renderDownloads(JE) {
    const container = getActiveContainer();
    if (!container) {
      return;
    }

    const instanceId = getInstanceId(container);
    const changedContainer = activeContainer !== container;
    const changedInstance = activeInstanceId !== instanceId;

    if (!changedContainer && !changedInstance && container.querySelector('#je-downloads-container')) {
      return;
    }

    if (changedContainer && activeContainer) {
      activeContainer.innerHTML = '';
    }

    activeContainer = container;
    activeInstanceId = instanceId;

    container.classList.remove('hide');
    container.style.display = '';
    document.querySelectorAll('.jellyfinenhanced.requests #je-downloads-container').forEach((node) => {
      if (!container.contains(node)) {
        node.remove();
      }
    });
    if (!container.querySelector('#je-downloads-container')) {
      container.innerHTML = '<div id="je-downloads-container"></div>';
    }

    // Use dedicated custom tab rendering method
    JE.downloadsPage.renderForCustomTab?.({ instanceId: instanceId || undefined });
  }

  function queueRender(JE) {
    if (renderTimer) {
      clearTimeout(renderTimer);
    }

    renderTimer = setTimeout(() => {
      renderDownloads(JE);
    }, 50);
  }

  function queueRenderBurst(JE) {
    queueRender(JE);
    setTimeout(() => queueRender(JE), 120);
    setTimeout(() => queueRender(JE), 320);
  }

  // Watch for tab/container changes
  function watchForContainer(JE) {
    queueRenderBurst(JE);

    const observerTarget = document.querySelector('.mainAnimatedPages') || document.body;
    const observer = new MutationObserver(() => {
      queueRender(JE);
    });
    observer.observe(observerTarget, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class', 'style']
    });

    document.addEventListener('viewshow', () => queueRenderBurst(JE), true);
    document.addEventListener('beforetabchange', () => queueRenderBurst(JE), true);
    document.addEventListener('tabchange', () => queueRenderBurst(JE), true);
    window.addEventListener('hashchange', () => queueRenderBurst(JE), true);
    document.addEventListener('click', (event) => {
      const button = event.target?.closest?.('.emby-tabs-slider .emby-tab-button');
      if (button && isLikelyManagedRequestsButton(button)) {
        stabilizeManagedTabSelection(button);
      }
      queueRenderBurst(JE);
    }, true);
  }

  // Initialize
  waitForDownloads((JE) => {
    loadManagedTabMetadata().finally(() => {
      queueRenderBurst(JE);
    });
    watchForContainer(JE);
  });

})();
