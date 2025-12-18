// Minimal JS helpers used by the WMS dashboard to scroll the page.
// Must be loaded after blazor.server.js so JS interop can find it.

(function () {
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

    // Convention: the viewer root has id="viewer-layout" (see CameraView.razor)
    scrollToViewer: function () {
      waitForElementById('viewer-layout').then((el) => {
        if (!el) return;
        smoothScrollToElement(el);
      });
    }
  };
})();
