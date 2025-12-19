// Minimal JS helpers used by the WMS dashboard to scroll the page.
// Must be loaded after blazor.server.js so JS interop can find it.

(function () {
  const getWmsContainer = () => document.querySelector('.wms-container');

  const smoothScrollContainerToElement = (container, el) => {
    if (!container || !el) return false;
    const style = window.getComputedStyle(container);
    const oy = style.overflowY;
    const canScrollY = (oy === 'auto' || oy === 'scroll') && container.scrollHeight > container.clientHeight;
    if (!canScrollY) return false;

    const containerRect = container.getBoundingClientRect();
    const elRect = el.getBoundingClientRect();
    const delta = elRect.top - containerRect.top;
    const top = Math.max(0, container.scrollTop + delta - 12);

    try {
      container.scrollTo({ top, behavior: 'smooth' });
    } catch {
      container.scrollTop = top;
    }
    return true;
  };

  const getScrollableParent = (el) => {
    let cur = el && el.parentElement;
    while (cur) {
      const style = window.getComputedStyle(cur);
      const oy = style.overflowY;
      const canScrollY = (oy === 'auto' || oy === 'scroll') && cur.scrollHeight > cur.clientHeight;
      if (canScrollY) return cur;
      cur = cur.parentElement;
    }
    return document.scrollingElement || document.documentElement;
  };

  const smoothScrollToElement = (el) => {
    if (!el) return;
    const parent = getScrollableParent(el);

    // If parent is the document, native scrollIntoView is fine.
    if (parent === document.scrollingElement || parent === document.documentElement || parent === document.body) {
      try {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      } catch {
        el.scrollIntoView(true);
      }
      return;
    }

    // Otherwise, scroll the container so the element is visible.
    const parentRect = parent.getBoundingClientRect();
    const elRect = el.getBoundingClientRect();
    const delta = elRect.top - parentRect.top;
    const top = Math.max(0, parent.scrollTop + delta - 12);

    try {
      parent.scrollTo({ top, behavior: 'smooth' });
    } catch {
      parent.scrollTop = top;
    }
  };

  const waitForElementById = async (id, attempts = 30, delayMs = 50) => {
    if (!id) return null;
    for (let i = 0; i < attempts; i++) {
      const el = document.getElementById(id);
      if (el) return el;
      await new Promise((r) => setTimeout(r, delayMs));
    }
    return null;
  };

  window.scrollHelper = {
    scrollToElement: function (elementId) {
      waitForElementById(elementId).then((el) => {
        if (!el) return;
        smoothScrollToElement(el);
      });
    },

    // Convention: the viewer root has id="video-viewer-section" (see Dashboard.razor)
    scrollToViewer: function () {
      waitForElementById('video-viewer-section').then((el) => {
        if (!el) return;
        // WMS usa un contenedor con overflow (.wms-container). Scrollearlo explícitamente.
        const wms = getWmsContainer();
        if (wms) {
          const containerRect = wms.getBoundingClientRect();
          const elRect = el.getBoundingClientRect();
          const delta = elRect.top - containerRect.top;
          // Más offset para que el video quede más arriba (80px en lugar de 12px)
          const top = Math.max(0, wms.scrollTop + delta - 80);
          try {
            wms.scrollTo({ top, behavior: 'smooth' });
          } catch {
            wms.scrollTop = top;
          }
        } else {
          smoothScrollToElement(el);
        }

        // Segundo intento: al cerrar la lista de eventos cambia el layout/altura.
        setTimeout(() => {
          const el2 = document.getElementById('video-viewer-section');
          if (!el2) return;
          const wms2 = getWmsContainer();
          if (wms2) {
            const containerRect = wms2.getBoundingClientRect();
            const elRect = el2.getBoundingClientRect();
            const delta = elRect.top - containerRect.top;
            const top = Math.max(0, wms2.scrollTop + delta - 80);
            try {
              wms2.scrollTo({ top, behavior: 'smooth' });
            } catch {
              wms2.scrollTop = top;
            }
          } else {
            smoothScrollToElement(el2);
          }
        }, 120);
      });
    }
  };
})();
