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
    window.addEventListener('hashchange', () => queueRenderBurst(JE), true);
    document.addEventListener('click', () => queueRenderBurst(JE), true);
  }

  // Initialize
  waitForDownloads((JE) => {
    watchForContainer(JE);
  });

})();
