/**
 * ⚛️ React 18 Offline Hook & Virtual DOM Runtime Fallback (`window.React` / `window.ReactDOM`)
 * Guarantees 0ms local startup even without internet / CDN access.
 * If official React 18 UMD is loaded first, this file is a no-op.
 */
(function () {
  if (window.React && window.ReactDOM) return;

  let currentInstance = null;
  let hookIndex = 0;

  function createElement(type, props, ...children) {
    const flatChildren = [];
    const pushChild = (c) => {
      if (Array.isArray(c)) c.forEach(pushChild);
      else if (c !== null && c !== undefined && c !== false && c !== true) flatChildren.push(c);
    };
    children.forEach(pushChild);
    return { type, props: props || {}, children: flatChildren };
  }

  function useState(initialValue) {
    const inst = currentInstance;
    const idx = hookIndex++;
    if (inst.hooks.length <= idx) {
      inst.hooks[idx] = typeof initialValue === 'function' ? initialValue() : initialValue;
    }
    const setState = (nextVal) => {
      const prev = inst.hooks[idx];
      const resolved = typeof nextVal === 'function' ? nextVal(prev) : nextVal;
      if (resolved !== prev) {
        inst.hooks[idx] = resolved;
        scheduleRender(inst);
      }
    };
    return [inst.hooks[idx], setState];
  }

  function useRef(initialValue) {
    const inst = currentInstance;
    const idx = hookIndex++;
    if (inst.hooks.length <= idx) {
      inst.hooks[idx] = { current: initialValue };
    }
    return inst.hooks[idx];
  }

  function useCallback(fn, deps) {
    const inst = currentInstance;
    const idx = hookIndex++;
    const prev = inst.hooks[idx];
    if (!prev || !depsChanged(prev.deps, deps)) {
      if (prev) return prev.fn;
    }
    inst.hooks[idx] = { fn, deps };
    return fn;
  }

  function useEffect(effectFn, deps) {
    const inst = currentInstance;
    const idx = hookIndex++;
    const prev = inst.hooks[idx];
    if (!prev || depsChanged(prev.deps, deps)) {
      inst.hooks[idx] = { deps, cleanup: prev ? prev.cleanup : null, pending: effectFn };
    }
  }

  function depsChanged(oldDeps, newDeps) {
    if (!oldDeps || !newDeps) return true;
    if (oldDeps.length !== newDeps.length) return true;
    for (let i = 0; i < oldDeps.length; i++) {
      if (oldDeps[i] !== newDeps[i]) return true;
    }
    return false;
  }

  function renderVNode(vnode) {
    if (typeof vnode === 'string' || typeof vnode === 'number') {
      return document.createTextNode(String(vnode));
    }
    if (!vnode || typeof vnode !== 'object') {
      return document.createTextNode('');
    }
    if (typeof vnode.type === 'function') {
      return renderVNode(vnode.type({ ...vnode.props, children: vnode.children }));
    }
    const el = document.createElement(vnode.type);
    const props = vnode.props || {};
    Object.keys(props).forEach((k) => {
      const val = props[k];
      if (k === 'className') {
        el.className = val || '';
      } else if (k === 'style' && val && typeof val === 'object') {
        Object.assign(el.style, val);
      } else if (k.startsWith('on') && typeof val === 'function') {
        const evt = k.slice(2).toLowerCase();
        el.addEventListener(evt === 'change' && el.tagName === 'INPUT' ? 'input' : evt, val);
      } else if (k === 'value' && el.tagName === 'INPUT') {
        el.value = val != null ? val : '';
      } else if (k !== 'key' && k !== 'children' && val != null && val !== false) {
        el.setAttribute(k, val);
      }
    });
    (vnode.children || []).forEach((ch) => {
      el.appendChild(renderVNode(ch));
    });
    return el;
  }

  function scheduleRender(inst) {
    if (inst.scheduled) return;
    inst.scheduled = true;
    requestAnimationFrame(() => {
      inst.scheduled = false;
      performRender(inst);
    });
  }

  function performRender(inst) {
    // Preserve active input focus & cursor position across re-renders
    const activeEl = document.activeElement;
    const isInputFocused = activeEl && activeEl.classList && activeEl.classList.contains('jarvis-quick-input');
    const selStart = isInputFocused ? activeEl.selectionStart : null;
    const selEnd = isInputFocused ? activeEl.selectionEnd : null;

    currentInstance = inst;
    hookIndex = 0;
    const vtree = inst.RootComponent({});
    currentInstance = null;

    const dom = renderVNode(vtree);
    inst.container.innerHTML = '';
    inst.container.appendChild(dom);

    if (isInputFocused) {
      const newInput = inst.container.querySelector('.jarvis-quick-input');
      if (newInput) {
        newInput.focus();
        try { newInput.setSelectionRange(selStart, selEnd); } catch (_) {}
      }
    }

    inst.hooks.forEach((hObj) => {
      if (hObj && typeof hObj.pending === 'function') {
        const fn = hObj.pending;
        hObj.pending = null;
        if (typeof hObj.cleanup === 'function') {
          try { hObj.cleanup(); } catch (_) {}
        }
        const cleanup = fn();
        if (typeof cleanup === 'function') hObj.cleanup = cleanup;
      }
    });
  }

  window.React = {
    createElement,
    useState,
    useRef,
    useCallback,
    useEffect
  };

  window.ReactDOM = {
    createRoot(container) {
      const inst = { container, hooks: [], RootComponent: null, scheduled: false };
      return {
        render(element) {
          inst.RootComponent = typeof element.type === 'function' ? element.type : () => element;
          performRender(inst);
        }
      };
    },
    render(element, container) {
      this.createRoot(container).render(element);
    }
  };
})();
