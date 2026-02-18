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

  function getActiveContainer() {
    return document.querySelector('.tabContent.is-active .jellyfinenhanced.requests')
      || document.querySelector('.jellyfinenhanced.requests');
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

  // Watch for tab/container changes
  function watchForContainer(JE) {
    queueRender(JE);

    const observerTarget = document.querySelector('.mainAnimatedPages') || document.body;
    const observer = new MutationObserver(() => {
      queueRender(JE);
    });
    observer.observe(observerTarget, {
      childList: true,
      subtree: true
    });

    document.addEventListener('viewshow', () => queueRender(JE), true);
    window.addEventListener('hashchange', () => queueRender(JE), true);
  }

  // Initialize
  waitForDownloads((JE) => {
    watchForContainer(JE);
  });

})();
