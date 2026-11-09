'use strict';
// =====================================================================
//  РЕДАКТОР: окно, инструменты, мышь и клавиатура, панели справа, режим испытания.
//  Схема хранится в переменной st (формат — в core.js), автосохранение — в localStorage браузера.
// =====================================================================
(function () {
    const SE = window.SE;
    const $ = id => document.getElementById(id);
    const STORE = 'stationEditor.autosave.v1';
    const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

    let st = SE.newStation();

    const ui = {
        mode: 'edit',
        tool: 'place',              // select | place | erase | brush | signal | text
        placeType: 'straight',
        variant: {},                // тип кубика → выбранный вариант
        cam: { x: 80, y: 60, z: 1 },
        sel: new Set(),
        selSig: null,               // { key, idx }
        showGrid: true, showSecColors: true, showSecNames: true,
        hover: null, ghost: null, hoverColor: null, band: null, moving: null, flashSec: null, preview: null,
        brushSec: '',
        sigKind: 'entry', sigDir: 'R',
        tab: 'props',
        routeFilter: 'all',
        test: { occ: new Set(), set: [], routeTiles: new Set(), openSigs: new Set(), pending: null, savedPlus: null },
    };

    // =================================================================
    //  ИСТОРИЯ (отмена / возврат) И АВТОСОХРАНЕНИЕ
    // =================================================================
    const undoStack = [], redoStack = [];
    let gestureSnap = null;
    let routesCache = null;
    const routes = () => routesCache || (routesCache = SE.findRoutes(st));

    function begin() { if (gestureSnap === null) gestureSnap = JSON.stringify(st); }
    function end() {
        if (gestureSnap === null) return;
        const now = JSON.stringify(st);
        if (now !== gestureSnap) {
            undoStack.push(gestureSnap);
            if (undoStack.length > 200) undoStack.shift();
            redoStack.length = 0;
            changed();
        }
        gestureSnap = null;
    }
    function edit(fn) { begin(); fn(); end(); draw(); }

    function changed() {
        routesCache = null;
        ui.preview = null;
        save();
        refreshPanels();
        draw();
    }

    let saveTimer = 0;
    function save() {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => {
            try { localStorage.setItem(STORE, JSON.stringify(st)); } catch (e) { /* без автосохранения */ }
        }, 300);
    }

    function load(newSt, fit) {
        if (ui.mode === 'test') setMode('edit');
        st = newSt;
        ui.sel.clear();
        ui.selSig = null;
        ui.flashSec = null;
        routesCache = null;
        $('stName').value = st.name;
        refreshPanels();
        if (fit) fitView(); else draw();
        save();
    }

    function replaceStation(newSt) {
        undoStack.push(JSON.stringify(st));
        redoStack.length = 0;
        load(newSt, true);
    }

    function undo() {
        if (ui.mode === 'test' || !undoStack.length) return;
        redoStack.push(JSON.stringify(st));
        load(JSON.parse(undoStack.pop()), false);
        msg('Отменено');
    }
    function redo() {
        if (ui.mode === 'test' || !redoStack.length) return;
        undoStack.push(JSON.stringify(st));
        load(JSON.parse(redoStack.pop()), false);
        msg('Возвращено');
    }

    // =================================================================
    //  ХОЛСТ
    // =================================================================
    const cv = $('cv');
    const ctx = cv.getContext('2d');
    let W = 0, H = 0, drawQueued = false;

    function resize() {
        const r = $('stage').getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        W = r.width; H = r.height;
        cv.width = Math.round(W * dpr);
        cv.height = Math.round(H * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        draw();
    }

    function draw() {
        if (drawQueued) return;
        drawQueued = true;
        requestAnimationFrame(() => {
            drawQueued = false;
            SE.render(ctx, W, H, st, ui);
            $('zVal').textContent = Math.round(ui.cam.z * 100) + '%';
        });
    }

    const cs = () => SE.CELL * ui.cam.z;
    const cellAt = (mx, my) => [Math.floor((mx - ui.cam.x) / cs()), Math.floor((my - ui.cam.y) / cs())];

    function signalAt(mx, my) {
        const [gx, gy] = cellAt(mx, my);
        const c = cs();
        for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
            const k = SE.key(gx + dx, gy + dy);
            const t = st.tiles[k];
            if (!t || !t.sigs) continue;
            for (let i = 0; i < t.sigs.length; i++) {
                const b = SE.sigGeom(t, t.sigs[i], ui.cam.x + (gx + dx) * c, ui.cam.y + (gy + dy) * c, c).box;
                if (mx >= b.x1 && mx <= b.x2 && my >= b.y1 && my <= b.y2) return { key: k, idx: i };
            }
        }
        return null;
    }

    function zoomAt(mx, my, z) {
        z = Math.max(0.2, Math.min(3, z));
        const wx = (mx - ui.cam.x) / ui.cam.z, wy = (my - ui.cam.y) / ui.cam.z;
        ui.cam.z = z;
        ui.cam.x = mx - wx * z;
        ui.cam.y = my - wy * z;
        draw();
    }

    function fitView() {
        const keys = [...Object.keys(st.tiles), ...Object.keys(st.texts)];
        if (!keys.length) { ui.cam = { x: 80, y: 60, z: 1 }; draw(); return; }
        let x1 = Infinity, y1 = Infinity, x2 = -Infinity, y2 = -Infinity;
        for (const k of keys) {
            const [x, y] = SE.parse(k);
            x1 = Math.min(x1, x); y1 = Math.min(y1, y); x2 = Math.max(x2, x); y2 = Math.max(y2, y);
        }
        const bw = (x2 - x1 + 3) * SE.CELL, bh = (y2 - y1 + 3) * SE.CELL;
        const z = Math.max(0.2, Math.min(2, Math.min(W / bw, H / bh)));
        ui.cam.z = z;
        ui.cam.x = (W - (x2 - x1 + 1) * SE.CELL * z) / 2 - x1 * SE.CELL * z;
        ui.cam.y = (H - (y2 - y1 + 1) * SE.CELL * z) / 2 - y1 * SE.CELL * z;
        draw();
    }

    function centerOn(key) {
        const [x, y] = SE.parse(key);
        ui.cam.x = W / 2 - (x + 0.5) * cs();
        ui.cam.y = H / 2 - (y + 0.5) * cs();
        draw();
    }

    // =================================================================
    //  ДЕЙСТВИЯ НАД КЛЕТКАМИ
    // =================================================================
    const curVariant = () => ui.variant[ui.placeType] || 0;

    function makeTile(type, v, old) {
        const t = { t: type, v, sec: old ? old.sec : ui.brushSec };
        if (old && old.sigs) t.sigs = old.sigs;
        if (type === 'switch') {
            t.name = old && old.t === 'switch' ? old.name : '';
            if (old && old.group) t.group = old.group;
            if (old && old.plus === false) t.plus = false;
        }
        return t;
    }

    function placeAt(x, y, v) {
        const k = SE.key(x, y);
        st.tiles[k] = makeTile(ui.placeType, v === undefined ? curVariant() : v, st.tiles[k]);
    }

    function eraseAt(x, y) {
        const k = SE.key(x, y);
        delete st.tiles[k];
        delete st.texts[k];
        ui.sel.delete(k);
        if (ui.selSig && ui.selSig.key === k) ui.selSig = null;
    }

    function paintAt(x, y) {
        const t = st.tiles[SE.key(x, y)];
        if (t) t.sec = ui.brushSec;
    }

    /** Клетки на отрезке (для протягивания мышью без пропусков). */
    function lineCells(a, b) {
        const res = [];
        let [x, y] = a;
        const [x2, y2] = b;
        const dx = Math.abs(x2 - x), dy = -Math.abs(y2 - y), sx = x < x2 ? 1 : -1, sy = y < y2 ? 1 : -1;
        let err = dx + dy;
        for (;;) {
            res.push([x, y]);
            if (x === x2 && y === y2) break;
            const e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        return res;
    }

    function deleteSelection() {
        if (ui.selSig) {
            const { key, idx } = ui.selSig;
            edit(() => {
                const t = st.tiles[key];
                t.sigs.splice(idx, 1);
                if (!t.sigs.length) delete t.sigs;
                ui.selSig = null;
            });
            return;
        }
        if (!ui.sel.size) return;
        const n = ui.sel.size;
        edit(() => { for (const k of [...ui.sel]) eraseAt(...SE.parse(k)); });
        msg(`Удалено клеток: ${n}`);
    }

    function moveSelection(dx, dy) {
        if (!ui.sel.size || (!dx && !dy)) return;
        edit(() => {
            const tiles = {}, texts = {};
            for (const k of ui.sel) {
                if (st.tiles[k]) { tiles[k] = st.tiles[k]; delete st.tiles[k]; }
                if (st.texts[k]) { texts[k] = st.texts[k]; delete st.texts[k]; }
            }
            const nsel = new Set();
            const shift = k => { const [x, y] = SE.parse(k); return SE.key(x + dx, y + dy); };
            for (const [k, t] of Object.entries(tiles)) { st.tiles[shift(k)] = t; nsel.add(shift(k)); }
            for (const [k, t] of Object.entries(texts)) { st.texts[shift(k)] = t; nsel.add(shift(k)); }
            ui.sel = nsel;
            if (ui.selSig) ui.selSig = { key: shift(ui.selSig.key), idx: ui.selSig.idx };
        });
    }

    function rotateSelected(step) {
        if (ui.tool === 'place') {
            const n = SE.TYPES[ui.placeType].variants.length;
            ui.variant[ui.placeType] = (curVariant() + step + n) % n;
            refreshToolOpts();
            updateGhost();
            draw();
            return;
        }
        const keys = [...ui.sel].filter(k => st.tiles[k]);
        if (!keys.length) return;
        edit(() => {
            for (const k of keys) {
                const t = st.tiles[k];
                const n = SE.TYPES[t.t].variants.length;
                t.v = (t.v + step + n) % n;
            }
        });
    }

    function addSignal(k, dir, kind) {
        const t = st.tiles[k];
        if (!t) { msg('Светофор ставится на путь — сначала поставьте кубик пути', true); return; }
        const i = (t.sigs || []).findIndex(s => s.dir === dir);
        if (i >= 0) { selectSignal(k, i); msg('Здесь уже есть светофор этого направления — выбран'); return; }
        edit(() => {
            (t.sigs = t.sigs || []).push({ dir, kind, name: SE.suggestSignalName(st, kind, dir) });
            ui.sel = new Set([k]);
            ui.selSig = { key: k, idx: t.sigs.length - 1 };
        });
        showTab('props');
    }

    function selectSignal(k, i) {
        ui.sel = new Set([k]);
        ui.selSig = { key: k, idx: i };
        showTab('props');
        draw();
    }

    function editText(k) {
        const cur = st.texts[k];
        const s = prompt('Надпись (пусто — удалить):', cur ? cur.text : '');
        if (s === null) return;
        edit(() => {
            if (!s.trim()) delete st.texts[k];
            else st.texts[k] = { text: s.trim(), size: cur ? cur.size : 14 };
        });
    }

    function newSection(thenSelect) {
        const name = (prompt('Название участка (например 1П, 2-4СП, НАУ):', '') || '').trim();
        if (!name) return '';
        edit(() => { if (!st.sections[name]) st.sections[name] = { type: 'auto' }; });
        if (thenSelect !== false) ui.brushSec = name;
        refreshPanels();
        return name;
    }

    // =================================================================
    //  МЫШЬ
    // =================================================================
    let drag = null;            // текущий жест
    let spaceDown = false;

    cv.addEventListener('contextmenu', e => e.preventDefault());
    cv.addEventListener('wheel', e => {
        e.preventDefault();
        const r = cv.getBoundingClientRect();
        zoomAt(e.clientX - r.left, e.clientY - r.top, ui.cam.z * (e.deltaY < 0 ? 1.15 : 1 / 1.15));
    }, { passive: false });

    cv.addEventListener('pointerdown', e => {
        const r = cv.getBoundingClientRect();
        const mx = e.clientX - r.left, my = e.clientY - r.top;
        const [gx, gy] = cellAt(mx, my);
        const k = SE.key(gx, gy);
        cv.setPointerCapture(e.pointerId);

        if (e.button === 1 || (e.button === 0 && spaceDown)) {
            drag = { kind: 'pan', sx: e.clientX, sy: e.clientY, cx: ui.cam.x, cy: ui.cam.y };
            return;
        }
        if (ui.mode === 'test') {
            if (e.button === 0) testClick(gx, gy, signalAt(mx, my));
            if (e.button === 2) testOccupy(k);
            return;
        }
        if (e.button === 2) {
            begin();
            eraseAt(gx, gy);
            drag = { kind: 'erase', last: [gx, gy] };
            draw();
            return;
        }
        if (e.button !== 0) return;

        switch (ui.tool) {
            case 'select': {
                const sg = signalAt(mx, my);
                if (sg) { selectSignal(sg.key, sg.idx); refreshPanels(); return; }
                ui.selSig = null;
                const has = st.tiles[k] || st.texts[k];
                if (has && ui.sel.has(k) && !e.shiftKey) {
                    drag = { kind: 'move', start: [gx, gy] };
                    ui.moving = { keys: new Set(ui.sel), dx: 0, dy: 0 };
                } else if (has && !e.shiftKey) {
                    ui.sel = new Set([k]);
                    drag = { kind: 'move', start: [gx, gy] };
                    ui.moving = { keys: new Set(ui.sel), dx: 0, dy: 0 };
                    refreshPanels();
                } else {
                    if (!e.shiftKey) ui.sel.clear();
                    drag = { kind: 'band', add: e.shiftKey };
                    ui.band = { x1: gx, y1: gy, x2: gx, y2: gy };
                    refreshPanels();
                }
                break;
            }
            case 'place':
                begin();
                placeAt(gx, gy);
                drag = { kind: 'place', last: [gx, gy], first: [gx, gy], placed: [[gx, gy]] };
                if (ui.placeType === 'switch') { ui.sel = new Set([k]); ui.selSig = null; }
                break;
            case 'erase':
                begin();
                eraseAt(gx, gy);
                drag = { kind: 'erase', last: [gx, gy] };
                break;
            case 'brush':
                if (e.ctrlKey) {
                    const t = st.tiles[k];
                    if (t) { ui.brushSec = t.sec; refreshToolOpts(); msg(t.sec ? `Взят участок «${t.sec}»` : 'Взято: без участка'); }
                    return;
                }
                if (!ui.brushSec && !confirm('Участок не выбран — кисть будет СНИМАТЬ участок с клеток. Продолжить?')) return;
                begin();
                paintAt(gx, gy);
                drag = { kind: 'brush', last: [gx, gy] };
                break;
            case 'signal':
                addSignal(k, ui.sigDir, ui.sigKind);
                break;
            case 'text':
                editText(k);
                break;
        }
        draw();
    });

    cv.addEventListener('pointermove', e => {
        const r = cv.getBoundingClientRect();
        const mx = e.clientX - r.left, my = e.clientY - r.top;
        const [gx, gy] = cellAt(mx, my);
        const moved = !ui.hover || ui.hover[0] !== gx || ui.hover[1] !== gy;
        ui.hover = [gx, gy];
        if (moved) { updateGhost(); showCellInfo(gx, gy); }

        if (!drag) { if (moved) draw(); return; }
        if (drag.kind === 'pan') {
            ui.cam.x = drag.cx + e.clientX - drag.sx;
            ui.cam.y = drag.cy + e.clientY - drag.sy;
            draw();
            return;
        }
        if (!moved) return;
        if (drag.kind === 'band') { ui.band.x2 = gx; ui.band.y2 = gy; }
        else if (drag.kind === 'move') { ui.moving.dx = gx - drag.start[0]; ui.moving.dy = gy - drag.start[1]; }
        else {
            const cells = lineCells(drag.last, [gx, gy]).slice(1);
            for (const [x, y] of cells) {
                if (drag.kind === 'place') {
                    // прямой путь сам поворачивается по направлению протягивания: ─ ╱ ╲
                    let v;
                    if (ui.placeType === 'straight') {
                        const ddx = x - drag.last[0], ddy = y - drag.last[1];
                        v = ddy === 0 ? 0 : (ddx * ddy < 0 ? 1 : 2);
                        if (drag.placed.length === 1) placeAt(drag.first[0], drag.first[1], v);
                    }
                    placeAt(x, y, v);
                    drag.placed.push([x, y]);
                    drag.last = [x, y];
                } else if (drag.kind === 'erase') eraseAt(x, y);
                else if (drag.kind === 'brush') paintAt(x, y);
                drag.last = [x, y];
            }
        }
        draw();
    });

    function finishDrag() {
        if (!drag) return;
        const d = drag;
        drag = null;
        if (d.kind === 'band') {
            const b = ui.band;
            ui.band = null;
            for (let x = Math.min(b.x1, b.x2); x <= Math.max(b.x1, b.x2); x++)
                for (let y = Math.min(b.y1, b.y2); y <= Math.max(b.y1, b.y2); y++) {
                    const k = SE.key(x, y);
                    if (st.tiles[k] || st.texts[k]) ui.sel.add(k);
                }
            refreshPanels();
            if (ui.sel.size > 1) msg(`Выделено клеток: ${ui.sel.size}. Тяните — перенести, Delete — удалить, R — повернуть`);
        } else if (d.kind === 'move') {
            const m = ui.moving;
            ui.moving = null;
            if (m.dx || m.dy) moveSelection(m.dx, m.dy);
        } else if (d.kind !== 'pan') {
            end();
            refreshPanels();
        }
        draw();
    }
    cv.addEventListener('pointerup', finishDrag);
    cv.addEventListener('pointercancel', finishDrag);
    cv.addEventListener('pointerleave', () => { if (!drag) { ui.hover = null; ui.ghost = null; draw(); } });

    function updateGhost() {
        ui.ghost = null;
        ui.hoverColor = null;
        if (ui.mode !== 'edit' || !ui.hover) return;
        if (ui.tool === 'place') ui.ghost = { t: ui.placeType, v: curVariant() };
        if (ui.tool === 'brush') ui.hoverColor = ui.brushSec ? SE.secColor(st, ui.brushSec) : '#ffffff';
        if (ui.tool === 'erase') ui.hoverColor = '#d4141c';
    }

    function showCellInfo(gx, gy) {
        const k = SE.key(gx, gy);
        const t = st.tiles[k];
        let s = `Клетка ${gx}, ${gy}`;
        if (t) {
            s += ` · ${SE.TYPES[t.t].name}`;
            if (t.t === 'switch') s += ` ${t.name || '?'}`;
            s += t.sec ? ` · ${t.sec}` : ' · без участка';
        }
        $('sCell').textContent = s;
    }

    // =================================================================
    //  КЛАВИАТУРА
    // =================================================================
    document.addEventListener('keydown', e => {
        const tag = (e.target.tagName || '').toLowerCase();
        if (tag === 'input' || tag === 'select' || tag === 'textarea') {
            if (e.key === 'Escape') e.target.blur();
            return;
        }
        if (!$('modal').hidden) { if (e.key === 'Escape') closeModal(); return; }

        const k = e.key.toLowerCase();
        if (e.ctrlKey || e.metaKey) {
            if (k === 'z' || k === 'я') { e.preventDefault(); e.shiftKey ? redo() : undo(); }
            else if (k === 'y' || k === 'н') { e.preventDefault(); redo(); }
            else if (k === 's' || k === 'ы') { e.preventDefault(); saveFile(); }
            else if (k === 'o' || k === 'щ') { e.preventDefault(); $('fileIn').click(); }
            else if ((k === 'a' || k === 'ф') && ui.mode === 'edit') {
                e.preventDefault();
                ui.sel = new Set([...Object.keys(st.tiles), ...Object.keys(st.texts)]);
                setTool('select');
            }
            return;
        }
        if (e.code === 'Space') { spaceDown = true; cv.style.cursor = 'grab'; e.preventDefault(); return; }
        if (e.code === 'KeyF') { fitView(); return; }
        if (e.code === 'KeyM') { setMode(ui.mode === 'edit' ? 'test' : 'edit'); return; }
        if (e.key === 'Escape') {
            if (ui.mode === 'test') { ui.test.pending = null; msg('Набор маршрута отменён'); draw(); return; }
            ui.sel.clear(); ui.selSig = null; ui.flashSec = null; setTool('select'); return;
        }
        if (ui.mode !== 'edit') return;

        if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); deleteSelection(); return; }
        const arrows = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
        if (arrows[e.key] && ui.sel.size) { e.preventDefault(); moveSelection(...arrows[e.key]); return; }

        const byCode = { KeyV: 'select', KeyE: 'erase', KeyB: 'brush', KeyS: 'signal', KeyT: 'text' };
        if (byCode[e.code]) { setTool(byCode[e.code]); return; }
        if (e.code === 'KeyR') { rotateSelected(e.shiftKey ? -1 : 1); return; }
        const typ = Object.keys(SE.TYPES).find(t => SE.TYPES[t].key === e.key);
        if (typ) { ui.placeType = typ; setTool('place'); }
    });
    document.addEventListener('keyup', e => { if (e.code === 'Space') { spaceDown = false; cv.style.cursor = ''; } });

    // =================================================================
    //  РЕЖИМ ИСПЫТАНИЯ: маршруты, стрелки, занятость
    // =================================================================
    function setMode(m) {
        if (m === ui.mode) return;
        const T = ui.test;
        if (m === 'test') {
            ui.sel.clear(); ui.selSig = null; ui.ghost = null; ui.band = null; ui.flashSec = null; ui.preview = null;
            T.savedPlus = {};
            for (const [k, t] of Object.entries(st.tiles)) if (t.t === 'switch') T.savedPlus[k] = t.plus;
            T.occ = new Set(); T.set = []; T.pending = null;
            recomputeTest();
            if (ui.tab === 'props') ui.tab = 'routes';
            const n = routes();
            msg(`Испытание: найдено маршрутов ${n.length}`);
        } else {
            // положения стрелок после испытания не сохраняем — возвращаем как было
            for (const [k, p] of Object.entries(T.savedPlus || {})) {
                const t = st.tiles[k];
                if (!t) continue;
                if (p === false) t.plus = false; else delete t.plus;
            }
            T.set = []; T.occ = new Set(); T.pending = null;
            recomputeTest();
        }
        ui.mode = m;
        $('mEdit').classList.toggle('on', m === 'edit');
        $('mTest').classList.toggle('on', m === 'test');
        $('testBanner').hidden = m !== 'test';
        document.body.classList.toggle('testing', m === 'test');
        refreshPalette();
        refreshToolOpts();
        refreshPanels();
        draw();
    }

    function recomputeTest() {
        const T = ui.test;
        T.routeTiles = new Set();
        T.openSigs = new Set();
        for (const r of T.set) {
            r.tiles.forEach(k => T.routeTiles.add(k));
            const busy = r.sections.some(s => T.occ.has(s)) || (!r.exit && T.occ.has(r.dest));
            if (!busy) T.openSigs.add(r.start.key + '#' + r.start.idx);
        }
    }

    const sameStart = (r, s) => r.start.key === s.key && r.start.idx === s.idx;

    function setRoute(r, quiet) {
        const T = ui.test;
        const clash = r.tiles.find(k => T.routeTiles.has(k));
        if (clash) { if (!quiet) msg(`Маршрут ${r.name} не задан: пересекается с уже заданным маршрутом`, true); return false; }
        const busy = [...r.sections, ...(r.exit ? [r.dest] : [])].find(s => T.occ.has(s));
        if (busy) { if (!quiet) msg(`Маршрут ${r.name} не задан: участок ${busy} занят`, true); return false; }
        for (const x of r.switches) for (const k of x.keys) {
            const t = st.tiles[k];
            if (x.plus) delete t.plus; else t.plus = false;
        }
        T.set.push(r);
        recomputeTest();
        msg(`Задан маршрут ${r.shunt ? 'маневровый' : 'поездной'}: ${r.name}`);
        return true;
    }

    function cancelRoute(r) {
        const T = ui.test;
        T.set = T.set.filter(x => x !== r);
        recomputeTest();
        msg(`Маршрут ${r.name} отменён`);
    }

    function testClick(gx, gy, sg) {
        const T = ui.test;
        const k = SE.key(gx, gy);
        if (sg) {
            const set = T.set.find(r => sameStart(r, sg));
            const name = st.tiles[sg.key].sigs[sg.idx].name;
            if (set) cancelRoute(set);
            else if (T.pending && T.pending.key === sg.key && T.pending.idx === sg.idx) { T.pending = null; msg('Набор маршрута отменён'); }
            else if (!routes().some(r => sameStart(r, sg))) msg(`От светофора ${name} маршрутов нет`, true);
            else { T.pending = sg; msg(`Начало маршрута — ${name}. Щёлкните по пути назначения`); }
        } else if (T.pending) {
            const t = st.tiles[k];
            const from = routes().filter(r => sameStart(r, T.pending) && t);
            const exact = from.filter(r => r.dest === t.sec);
            const cand = exact.length ? exact : from.filter(r => r.tiles.includes(k));
            // поездной или маневровый — по фильтру на вкладке «Маршруты» (по умолчанию сначала поездной)
            if (ui.routeFilter === 'shunt') cand.sort((a, b) => b.shunt - a.shunt);
            else cand.sort((a, b) => a.shunt - b.shunt);
            const name = st.tiles[T.pending.key].sigs[T.pending.idx].name;
            T.pending = null;
            if (!cand.length) msg(`Маршрута от ${name} до ${t && t.sec ? t.sec : 'этой клетки'} нет`, true);
            else if (!cand.some(r => setRoute(r, true))) setRoute(cand[0]);
        } else {
            const t = st.tiles[k];
            if (t && t.t === 'switch') toggleSwitch(k);
        }
        refreshPanels();
        draw();
    }

    function toggleSwitch(k) {
        const T = ui.test;
        const t = st.tiles[k];
        const g = SE.switchGroup(k, t);
        const keys = Object.keys(st.tiles).filter(x => st.tiles[x].t === 'switch' && SE.switchGroup(x, st.tiles[x]) === g);
        if (keys.some(x => T.routeTiles.has(x))) { msg(`Стрелка ${t.name || '?'} замкнута в маршруте — сначала отмените маршрут`, true); return; }
        if (keys.some(x => st.tiles[x].sec && T.occ.has(st.tiles[x].sec))) { msg(`Стрелка ${t.name || '?'} занята — под составом не переводится`, true); return; }
        const plus = t.plus === false;
        for (const x of keys) { if (plus) delete st.tiles[x].plus; else st.tiles[x].plus = false; }
        msg(`Стрелка ${t.group || t.name || '?'} → ${plus ? 'плюс' : 'минус'}`);
    }

    function testOccupy(k) {
        const t = st.tiles[k];
        if (!t) return;
        if (!t.sec) { msg('Клетка не отнесена к участку — занять нечего', true); return; }
        const T = ui.test;
        if (T.occ.has(t.sec)) T.occ.delete(t.sec); else T.occ.add(t.sec);
        msg(`Участок ${t.sec} ${T.occ.has(t.sec) ? 'занят' : 'свободен'}`);
        recomputeTest();
        draw();
    }

    // =================================================================
    //  ЛЕВАЯ ПАНЕЛЬ: КУБИКИ И ИНСТРУМЕНТЫ
    // =================================================================
    const TOOLS = [
        { id: 'select', name: 'Выбор', key: 'V', ico: '⬚' },
        { id: 'erase', name: 'Ластик', key: 'E', ico: '⌫' },
        { id: 'brush', name: 'Участок', key: 'B', ico: '▦' },
        { id: 'signal', name: 'Светофор', key: 'S', ico: 'sig' },
        { id: 'text', name: 'Надпись', key: 'T', ico: 'Aa' },
    ];

    function tileCanvas(t, size, withBg) {
        const c = document.createElement('canvas');
        const dpr = window.devicePixelRatio || 1;
        c.width = size * dpr;
        c.height = size * dpr;
        const g = c.getContext('2d');
        g.scale(dpr, dpr);
        if (withBg !== false) { g.fillStyle = SE.PAL.bg; g.fillRect(0, 0, size, size); }
        const pad = size * 0.12;
        SE.drawTrack(g, t, pad, pad, size - 2 * pad, { width: Math.max(2, size * 0.09) });
        return c;
    }

    function signalCanvas(size) {
        const c = document.createElement('canvas');
        const dpr = window.devicePixelRatio || 1;
        c.width = size * dpr; c.height = size * dpr;
        const g = c.getContext('2d');
        g.scale(dpr, dpr);
        g.fillStyle = SE.PAL.bg; g.fillRect(0, 0, size, size);
        const t = { t: 'straight', v: 0 };
        const cs = size * 0.75, x = size * 0.25 - cs, y = size * 0.3 - cs / 2;
        SE.drawTrack(g, t, x, y, cs, { width: size * 0.08 });
        SE.drawTrack(g, t, x + cs, y, cs, { width: size * 0.08 });
        SE.drawSignal(g, t, { dir: 'R', kind: 'entry', name: '' }, x, y, cs, { label: false });
        return c;
    }

    function refreshPalette() {
        const p = $('palette');
        p.innerHTML = '';
        const item = (label, key, on, content, onClick, title) => {
            const d = document.createElement('div');
            d.className = 'pal-item' + (on ? ' on' : '');
            d.title = title || label;
            if (typeof content === 'string') {
                const s = document.createElement('div');
                s.className = 'ico';
                s.textContent = content;
                d.appendChild(s);
            } else d.appendChild(content);
            d.insertAdjacentHTML('beforeend', `<span>${esc(label)}</span>` + (key ? `<span class="k">${esc(key)}</span>` : ''));
            d.onclick = onClick;
            return d;
        };
        const sect = (title, items) => {
            p.insertAdjacentHTML('beforeend', `<div class="pal-title">${esc(title)}</div>`);
            const g = document.createElement('div');
            g.className = 'pal-grid';
            items.forEach(i => g.appendChild(i));
            p.appendChild(g);
        };
        if (ui.mode === 'test') {
            p.innerHTML = '<div class="hint">В режиме испытания схему не меняют. Вернитесь в «Редактор», чтобы рисовать.</div>';
            return;
        }
        sect('Кубики пути', Object.entries(SE.TYPES).map(([id, T]) =>
            item(T.name, T.key, ui.tool === 'place' && ui.placeType === id,
                tileCanvas({ t: id, v: id === ui.placeType ? curVariant() : 0 }, 44),
                () => { ui.placeType = id; setTool('place'); }, `${T.name} — ${T.hint} (клавиша ${T.key})`)));
        sect('Разметка и правка', TOOLS.map(T =>
            item(T.name, T.key, ui.tool === T.id, T.ico === 'sig' ? signalCanvas(44) : T.ico,
                () => setTool(T.id), `${T.name} (клавиша ${T.key})`)));
    }

    function secOptions(selected, withNew) {
        let h = `<option value="">— без участка —</option>`;
        for (const n of SE.sectionNames(st)) h += `<option value="${esc(n)}"${n === selected ? ' selected' : ''}>${esc(n)}</option>`;
        if (withNew) h += `<option value="__new">+ новый участок…</option>`;
        return h;
    }

    function refreshToolOpts() {
        const o = $('toolOpts');
        if (ui.mode === 'test') { o.innerHTML = ''; return; }
        let h = '';
        const hints = {
            select: 'Щелчок — выбрать клетку или светофор. Тяните по пустому месту — выделить рамкой, тяните выделенное — перенести. ' +
                'Delete — удалить, стрелки клавиатуры — сдвинуть, R — повернуть.',
            erase: 'Щелчок или протягивание — стереть кубики и надписи. Правая кнопка мыши стирает в любом инструменте.',
            brush: 'Протяните по кубикам — они войдут в участок. Ctrl+щелчок — взять участок с клетки. ' +
                'Стыки рисуются сами там, где участок меняется.',
            signal: 'Щелчок по кубику пути — поставить светофор. «→» встаёт под путём на правом краю клетки, «←» — над путём на левом.',
            text: 'Щелчок по клетке — написать или поправить надпись (номер пути, «к станции …»).',
        };
        if (ui.tool === 'place') {
            const T = SE.TYPES[ui.placeType];
            h += `<div class="pal-title">${esc(T.name)}: вариант (R)</div><div class="variants" id="varBox"></div>`;
            h += `<div class="row"><label>Участок новых</label><select id="optSec">${secOptions(ui.brushSec, true)}</select></div>`;
            h += `<div class="hint">${esc(T.hint)}. Щелчок или протягивание — поставить, R / Shift+R — повернуть.` +
                (ui.placeType === 'straight' ? ' Путь сам ложится по направлению протягивания: ─ ╱ ╲.' : '') + '</div>';
        } else if (ui.tool === 'brush') {
            h += `<div class="row"><label>Участок</label><select id="optSec">${secOptions(ui.brushSec, true)}</select></div>`;
            h += `<div class="hint">${hints.brush}</div>`;
        } else if (ui.tool === 'signal') {
            h += `<div class="row"><label>Тип</label><select id="optKind">${Object.entries(SE.SIG_KINDS).map(([k, n]) =>
                `<option value="${k}"${k === ui.sigKind ? ' selected' : ''}>${n}</option>`).join('')}</select></div>`;
            h += `<div class="row"><label>Направление</label><div class="dirbtns">
                <button id="dirR" class="${ui.sigDir === 'R' ? 'on' : ''}" title="чётное, вправо">→ Ч</button>
                <button id="dirL" class="${ui.sigDir === 'L' ? 'on' : ''}" title="нечётное, влево">← Н</button></div></div>`;
            h += `<div class="hint">${hints.signal} Имя подставится само (Ч, Н1, М2…) — поменять можно в «Свойствах».</div>`;
        } else h += `<div class="hint">${hints[ui.tool]}</div>`;
        o.innerHTML = h;

        const vb = $('varBox');
        if (vb) SE.TYPES[ui.placeType].variants.forEach((_, i) => {
            const c = tileCanvas({ t: ui.placeType, v: i }, 38);
            if (i === curVariant()) c.classList.add('on');
            c.onclick = () => { ui.variant[ui.placeType] = i; refreshToolOpts(); refreshPalette(); };
            vb.appendChild(c);
        });
        const os = $('optSec');
        if (os) os.onchange = () => {
            if (os.value === '__new') { newSection(); refreshToolOpts(); return; }
            ui.brushSec = os.value;
            updateGhost();
        };
        const ok = $('optKind');
        if (ok) ok.onchange = () => { ui.sigKind = ok.value; };
        if ($('dirR')) $('dirR').onclick = () => { ui.sigDir = 'R'; refreshToolOpts(); };
        if ($('dirL')) $('dirL').onclick = () => { ui.sigDir = 'L'; refreshToolOpts(); };
    }

    function setTool(t) {
        if (ui.mode === 'test') return;
        ui.tool = t;
        if (t !== 'select') { ui.moving = null; ui.band = null; }
        updateGhost();
        refreshPalette();
        refreshToolOpts();
        const hint = {
            select: 'Выбор: щелчок — выбрать, рамка — выделить несколько',
            place: `Ставим «${SE.TYPES[ui.placeType].name}»: щелчок / протягивание, R — повернуть, ПКМ — стереть`,
            erase: 'Ластик: щелчок / протягивание', brush: 'Участок: протяните по кубикам',
            signal: 'Светофор: щелчок по пути', text: 'Надпись: щелчок по клетке',
        }[t];
        $('sHint').textContent = hint + ' · колесо — масштаб, средняя кнопка или пробел+мышь — сдвинуть схему';
        draw();
    }

    // =================================================================
    //  ПРАВАЯ ПАНЕЛЬ: ВКЛАДКИ
    // =================================================================
    function showTab(t) { ui.tab = t; refreshPanels(); }

    document.querySelectorAll('#tabs button').forEach(b => b.onclick = () => {
        if (b.dataset.tab !== 'routes') ui.preview = null;
        showTab(b.dataset.tab);
        draw();
    });

    function refreshPanels() {
        document.querySelectorAll('#tabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === ui.tab));
        const errs = SE.validate(st, routes()).filter(p => p.level === 'err').length;
        const cb = document.querySelector('#tabs [data-tab=checks]');
        cb.innerHTML = 'Проверка' + (errs ? `<span class="badge">${errs}</span>` : '');
        const body = $('tabBody');
        body.innerHTML = '';
        ({ props: renderProps, secs: renderSecs, routes: renderRoutes, checks: renderChecks })[ui.tab](body);
        $('bUndo').disabled = !undoStack.length || ui.mode === 'test';
        $('bRedo').disabled = !redoStack.length || ui.mode === 'test';
    }

    // ---------------- Свойства ----------------
    function renderProps(body) {
        if (ui.mode === 'test') {
            body.innerHTML = '<div class="hint">В режиме испытания свойства не правятся. Маршруты — на вкладке «Маршруты».</div>';
            return;
        }
        if (!ui.sel.size) {
            body.innerHTML = `<h3>${esc(st.name)}</h3>
                <div class="hint">Ничего не выбрано. Инструментом «Выбор» (V) щёлкните по клетке или светофору.</div>
                <div class="hint">Кубиков: ${Object.keys(st.tiles).length}, участков: ${SE.sectionNames(st).length},
                светофоров: ${SE.allSignals(st).length}, маршрутов найдено: ${routes().length}.</div>
                <h4>С чего начать</h4>
                <div class="hint">1. Кубиками (клавиши 1–7) нарисуйте пути, стрелки, тупики, выходы на перегон.<br>
                2. Кистью «Участок» (B) разбейте пути на участки — 1П, 2-4СП, Ч-М2…<br>
                3. Поставьте светофоры (S) и пронумеруйте стрелки.<br>
                4. «Испытание» — проверьте маршруты, стрелки и занятость.<br>
                5. «Экспорт в C#» — черновик кода схемы для программы станции.</div>
                <div class="btns" style="margin-top:10px"><button id="pEx">Открыть пример «Алгабас»</button></div>`;
            $('pEx').onclick = () => loadExample('algabas');
            return;
        }
        if (ui.sel.size > 1) {
            const keys = [...ui.sel];
            const n = keys.filter(k => st.tiles[k]).length;
            body.innerHTML = `<h3>Выделено клеток: ${ui.sel.size}</h3>
                <div class="props"><div class="row"><label>Участок</label><select id="mSec">
                <option value="__keep">— не менять —</option>${secOptions('', true)}</select></div></div>
                <div class="hint">Кубиков пути: ${n}. Тяните выделение мышью или стрелками клавиатуры, R — повернуть каждый.</div>
                <button id="mDel" class="danger">Удалить выделенное</button>`;
            $('mSec').onchange = e => {
                let v = e.target.value;
                if (v === '__keep') return;
                if (v === '__new') { v = newSection(false); if (!v) { refreshPanels(); return; } }
                edit(() => keys.forEach(k => { if (st.tiles[k]) st.tiles[k].sec = v; }));
            };
            $('mDel').onclick = deleteSelection;
            return;
        }
        const k = [...ui.sel][0];
        const t = st.tiles[k];
        const tx = st.texts[k];
        let h = '<div class="props">';
        if (t) {
            const T = SE.TYPES[t.t];
            h += `<h3>${esc(T.name)} <span class="hint">клетка ${esc(k)}</span></h3>`;
            h += `<div class="row"><label>Вариант</label><div class="variants" id="pVar"></div></div>`;
            h += `<div class="row"><label>Участок</label><select id="pSec">${secOptions(t.sec, true)}</select></div>`;
            if (t.sec) h += `<div class="row"><label>Тип участка</label><span>${esc(SE.SEC_TYPES[SE.secType(st, t.sec)] || '')}</span></div>`;
            if (t.t === 'switch') {
                h += `<h4>Стрелка</h4>
                    <div class="row"><label>Номер</label><input id="pSwName" value="${esc(t.name || '')}" placeholder="например 2"></div>
                    <div class="row"><label>Съезд</label><input id="pSwGroup" value="${esc(t.group || '')}" placeholder="пусто — одиночная; 4/6 — спаренная"></div>
                    <div class="row"><label>Положение</label><select id="pSwPos">
                        <option value="plus"${t.plus !== false ? ' selected' : ''}>плюс (прямо)</option>
                        <option value="minus"${t.plus === false ? ' selected' : ''}>минус (на отклонение)</option></select></div>
                    <div class="hint">Стрелки с одинаковым «съездом» переводятся вместе (спаренные).</div>`;
            }
            h += '<h4>Светофоры на клетке</h4>';
            (t.sigs || []).forEach((s, i) => {
                const on = ui.selSig && ui.selSig.key === k && ui.selSig.idx === i;
                h += `<div class="sig-row"${on ? ' style="outline:2px solid #ffeb00;border-radius:6px"' : ''}>
                    <input data-i="${i}" data-f="name" value="${esc(s.name)}" placeholder="имя">
                    <select data-i="${i}" data-f="kind">${Object.entries(SE.SIG_KINDS).map(([kk, n]) =>
                        `<option value="${kk}"${kk === s.kind ? ' selected' : ''}>${n}</option>`).join('')}</select>
                    <select data-i="${i}" data-f="dir"><option value="R"${s.dir === 'R' ? ' selected' : ''}>→</option>
                        <option value="L"${s.dir === 'L' ? ' selected' : ''}>←</option></select>
                    <button data-i="${i}" data-f="del" class="danger" title="Удалить светофор">✕</button></div>`;
            });
            const dirs = (t.sigs || []).map(s => s.dir);
            h += `<div class="btns" style="display:flex;gap:4px">
                ${dirs.includes('R') ? '' : '<button id="pAddR">+ светофор →</button>'}
                ${dirs.includes('L') ? '' : '<button id="pAddL">+ светофор ←</button>'}</div>`;
        } else h += `<h3>Надпись <span class="hint">клетка ${esc(k)}</span></h3>`;

        h += `<h4>Надпись в клетке</h4>
            <div class="row"><label>Текст</label><input id="pText" value="${esc(tx ? tx.text : '')}" placeholder="нет"></div>
            <div class="row"><label>Размер</label><input id="pTextSize" type="number" min="8" max="48" value="${tx ? tx.size : 14}"></div>`;
        h += `<div style="margin-top:12px"><button id="pDel" class="danger">Удалить клетку</button></div></div>`;
        body.innerHTML = h;

        if (t) {
            const vb = $('pVar');
            SE.TYPES[t.t].variants.forEach((_, i) => {
                const c = tileCanvas({ t: t.t, v: i, plus: t.plus }, 38);
                if (i === t.v) c.classList.add('on');
                c.onclick = () => edit(() => { t.v = i; });
                vb.appendChild(c);
            });
            $('pSec').onchange = e => {
                let v = e.target.value;
                if (v === '__new') { v = newSection(false); if (!v) { refreshPanels(); return; } }
                edit(() => { t.sec = v; });
            };
            if (t.t === 'switch') {
                $('pSwName').onchange = e => edit(() => { t.name = e.target.value.trim(); });
                $('pSwGroup').onchange = e => edit(() => {
                    const g = e.target.value.trim();
                    if (g) t.group = g; else delete t.group;
                });
                $('pSwPos').onchange = e => edit(() => { if (e.target.value === 'minus') t.plus = false; else delete t.plus; });
            }
            body.querySelectorAll('[data-f]').forEach(el => {
                const i = +el.dataset.i, f = el.dataset.f;
                if (f === 'del') el.onclick = () => edit(() => {
                    t.sigs.splice(i, 1);
                    if (!t.sigs.length) delete t.sigs;
                    ui.selSig = null;
                });
                else el.onchange = () => edit(() => { t.sigs[i][f] = f === 'name' ? el.value.trim() : el.value; });
                el.onfocus = () => { ui.selSig = { key: k, idx: i }; draw(); };
            });
            if ($('pAddR')) $('pAddR').onclick = () => addSignal(k, 'R', ui.sigKind);
            if ($('pAddL')) $('pAddL').onclick = () => addSignal(k, 'L', ui.sigKind);
        }
        $('pText').onchange = e => edit(() => {
            const s = e.target.value.trim();
            if (s) st.texts[k] = { text: s, size: +$('pTextSize').value || 14 };
            else delete st.texts[k];
        });
        $('pTextSize').onchange = e => edit(() => { if (st.texts[k]) st.texts[k].size = Math.max(8, Math.min(48, +e.target.value || 14)); });
        $('pDel').onclick = () => edit(() => eraseAt(...SE.parse(k)));
    }

    // ---------------- Участки ----------------
    function toHex(color) {
        const c = document.createElement('canvas').getContext('2d');
        c.fillStyle = color;
        return c.fillStyle.startsWith('#') ? c.fillStyle : '#888888';
    }

    function renderSecs(body) {
        const by = SE.tilesBySection(st);
        const names = SE.sectionNames(st);
        const free = Object.values(st.tiles).filter(t => !t.sec).length;
        let h = `<h3>Участки (${names.length})</h3>
            <div class="hint">Тип участка нужен для автопоиска маршрутов: поездной маршрут кончается на «пути»,
            отправление — на «перегоне», маневровый — на первом нестрелочном участке.</div>
            <div class="list">`;
        for (const n of names) {
            const isSw = SE.secType(st, n) === 'switch';
            const cur = (st.sections[n] && st.sections[n].type) || 'auto';
            const auto = SE.secType(st, n);
            const opts = ['auto', 'path', 'plain', 'line', 'dead'].map(v =>
                `<option value="${v}"${v === cur ? ' selected' : ''}>${v === 'auto' ? 'авто: ' + shortType(auto) : shortType(v)}</option>`).join('');
            h += `<div class="sec-row${ui.flashSec === n ? ' on' : ''}" data-n="${esc(n)}">
                <input type="color" value="${toHex(SE.secColor(st, n))}" title="Цвет участка в редакторе">
                <span class="nm" title="Показать на схеме и выбрать для кисти">${esc(n)}</span>
                ${isSw ? '<span class="hint" style="margin:0">стрелочный</span>' : `<select title="Тип участка">${opts}</select>`}
                <span class="cnt" title="кубиков">${(by.get(n) || []).length}</span>
                <button data-a="ren" title="Переименовать">✎</button>
                <button data-a="del" title="Удалить участок (кубики останутся без участка)">✕</button></div>`;
        }
        h += `</div><div style="margin-top:10px;display:flex;gap:6px;align-items:center">
            <button id="sNew">+ Новый участок</button>
            ${free ? `<span class="hint" style="margin:0">без участка: ${free} кубиков</span>` : ''}</div>`;
        body.innerHTML = h;
        $('sNew').onclick = () => { newSection(); setTool('brush'); };

        body.querySelectorAll('.sec-row').forEach(row => {
            const n = row.dataset.n;
            row.querySelector('.nm').onclick = () => {
                ui.flashSec = ui.flashSec === n ? null : n;
                ui.brushSec = n;
                refreshToolOpts();
                refreshPanels();
                draw();
            };
            row.querySelector('input[type=color]').onchange = e =>
                edit(() => { st.sections[n] = Object.assign(st.sections[n] || {}, { color: e.target.value }); });
            const sel = row.querySelector('select');
            if (sel) sel.onchange = () => edit(() => { st.sections[n] = Object.assign(st.sections[n] || {}, { type: sel.value }); });
            row.querySelector('[data-a=ren]').onclick = () => {
                const nn = (prompt(`Новое название участка «${n}»:`, n) || '').trim();
                if (!nn || nn === n) return;
                edit(() => {
                    for (const t of Object.values(st.tiles)) if (t.sec === n) t.sec = nn;
                    if (st.sections[n]) { st.sections[nn] = Object.assign(st.sections[n], st.sections[nn] || {}); delete st.sections[n]; }
                    if (ui.brushSec === n) ui.brushSec = nn;
                });
                refreshToolOpts();
            };
            row.querySelector('[data-a=del]').onclick = () => {
                if (!confirm(`Удалить участок «${n}»? Его кубики останутся, но без участка.`)) return;
                edit(() => {
                    for (const t of Object.values(st.tiles)) if (t.sec === n) t.sec = '';
                    delete st.sections[n];
                    if (ui.brushSec === n) ui.brushSec = '';
                });
                refreshToolOpts();
            };
        });
    }

    const shortType = t => ({ path: 'путь', plain: 'участок', line: 'перегон', dead: 'тупик', switch: 'стрелочный' }[t] || t);

    // ---------------- Маршруты ----------------
    function renderRoutes(body) {
        const all = routes();
        const T = ui.test;
        const test = ui.mode === 'test';
        const f = ui.routeFilter;
        const list = all.filter(r => f === 'all' || (f === 'shunt') === r.shunt);
        let h = `<h3>Маршруты (${all.length})</h3>
            <div class="seg-small">
                <button data-f="all" class="${f === 'all' ? 'on' : ''}">Все</button>
                <button data-f="train" class="${f === 'train' ? 'on' : ''}">Поездные</button>
                <button data-f="shunt" class="${f === 'shunt' ? 'on' : ''}">Маневровые</button></div>`;
        h += test
            ? `<div class="hint">Щелчок — задать / отменить маршрут. Заданные выделены жёлтым.</div>
               <div style="margin-bottom:8px"><button id="rReset">Отменить все маршруты и освободить участки</button></div>`
            : `<div class="hint">Найдены автоматически по кубикам. Щелчок — показать маршрут на схеме.
               Задать и проверить — в режиме «Испытание».</div>`;
        h += '<div class="list">';
        let lastStart = null;
        for (const r of list) {
            const isSet = test && T.set.includes(r);
            const on = !test && ui.preview && ui.preview.id === r.id;
            if (r.startName !== lastStart) {
                h += `<div class="hint" style="margin:8px 4px 2px;font-weight:600">от ${esc(r.startName)}</div>`;
                lastStart = r.startName;
            }
            const sw = r.switches.map(x => `${x.name}${x.plus ? '+' : '−'}`).join(' ');
            h += `<div class="it${isSet ? ' set' : ''}${on ? ' on' : ''}" data-id="${esc(r.id)}">
                <span>${r.shunt ? '◇' : '◆'}</span>
                <span class="grow" title="${esc(r.sections.join(', '))}">${esc(r.name)}</span>
                <span class="sub">${esc(sw)}</span></div>`;
        }
        if (!list.length) h += '<div class="hint">Маршрутов нет. Нужны светофоры и участки с типом «путь» / «перегон».</div>';
        h += '</div>';
        body.innerHTML = h;
        body.querySelectorAll('[data-f]').forEach(b => b.onclick = () => { ui.routeFilter = b.dataset.f; refreshPanels(); });
        if ($('rReset')) $('rReset').onclick = () => { T.set = []; T.occ = new Set(); T.pending = null; recomputeTest(); refreshPanels(); draw(); msg('Всё сброшено'); };
        body.querySelectorAll('.it').forEach(el => el.onclick = () => {
            const r = all.find(x => x.id === el.dataset.id);
            if (!r) return;
            if (test) { if (T.set.includes(r)) cancelRoute(r); else setRoute(r); }
            else {
                ui.preview = ui.preview && ui.preview.id === r.id ? null : { id: r.id, tiles: new Set(r.tiles) };
                if (ui.preview) msg(`${r.name}: ${r.sections.join(' → ')}${r.switches.length ? ' · стрелки ' + r.switches.map(x => x.name + (x.plus ? '+' : '−')).join(', ') : ''}`);
            }
            refreshPanels();
            draw();
        });
    }

    // ---------------- Проверка ----------------
    function renderChecks(body) {
        const probs = SE.validate(st, routes());
        const n = l => probs.filter(p => p.level === l).length;
        let h = `<h3>Проверка схемы</h3>
            <div class="hint">Ошибок: ${n('err')}, предупреждений: ${n('warn')}. Щелчок по строке — показать место на схеме.</div>`;
        for (const p of probs) {
            const ico = { err: '✕', warn: '!', info: 'i' }[p.level];
            h += `<div class="prob ${p.level}"${p.key ? ` data-k="${esc(p.key)}"` : ''}><span class="lv">${ico}</span><span>${esc(p.text)}</span></div>`;
        }
        if (!n('err') && !n('warn') && probs.length) h += '<div class="hint" style="color:#1f7a68">Замечаний нет.</div>';
        body.innerHTML = h;
        body.querySelectorAll('[data-k]').forEach(el => el.onclick = () => {
            const k = el.dataset.k;
            if (ui.mode === 'edit') { ui.sel = new Set([k]); ui.selSig = null; }
            centerOn(k);
        });
    }

    // =================================================================
    //  ВЕРХНЯЯ ПОЛОСА, ФАЙЛЫ, ОКНА
    // =================================================================
    function saveFile() {
        const blob = new Blob([JSON.stringify(st, null, 1)], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = (st.name || 'станция').replace(/[\\/:*?"<>|]+/g, '_') + '.station.json';
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
        msg('Файл схемы сохранён в папку загрузок');
    }

    $('fileIn').onchange = e => {
        const f = e.target.files[0];
        e.target.value = '';
        if (!f) return;
        const rd = new FileReader();
        rd.onload = () => {
            try {
                replaceStation(SE.normalize(JSON.parse(rd.result)));
                msg(`Открыт файл ${f.name}`);
            } catch (err) { msg('Не удалось открыть файл: ' + err.message, true); }
        };
        rd.readAsText(f, 'utf-8');
    };

    function loadExample(id) {
        const ex = SE.EXAMPLES[id];
        if (!ex) return;
        if (Object.keys(st.tiles).length && !confirm(`Заменить текущую схему примером «${ex.name}»? (Отменить можно кнопкой ↶)`)) return;
        replaceStation(ex.build());
        msg(`Загружен пример «${ex.name}»`);
    }

    for (const [id, ex] of Object.entries(SE.EXAMPLES))
        $('bExample').insertAdjacentHTML('beforeend', `<option value="${id}">${esc(ex.name)}</option>`);
    $('bExample').onchange = e => { const v = e.target.value; e.target.value = ''; e.target.blur(); loadExample(v); };

    $('bNew').onclick = () => {
        if (Object.keys(st.tiles).length && !confirm('Начать новую пустую схему? (Отменить можно кнопкой ↶)')) return;
        replaceStation(SE.newStation());
    };
    $('bOpen').onclick = () => $('fileIn').click();
    $('bSave').onclick = saveFile;
    $('bUndo').onclick = undo;
    $('bRedo').onclick = redo;
    $('mEdit').onclick = () => setMode('edit');
    $('mTest').onclick = () => setMode('test');
    $('stName').onchange = e => edit(() => { st.name = e.target.value.trim() || 'Без названия'; });
    $('vGrid').onchange = e => { ui.showGrid = e.target.checked; draw(); };
    $('vColors').onchange = e => { ui.showSecColors = e.target.checked; draw(); };
    $('vNames').onchange = e => { ui.showSecNames = e.target.checked; draw(); };
    $('zIn').onclick = () => zoomAt(W / 2, H / 2, ui.cam.z * 1.25);
    $('zOut').onclick = () => zoomAt(W / 2, H / 2, ui.cam.z / 1.25);
    $('zFit').onclick = fitView;

    function openModal(title, html) {
        $('mTitle').textContent = title;
        $('mBody').innerHTML = html;
        $('modal').hidden = false;
    }
    function closeModal() { $('modal').hidden = true; }
    $('mClose').onclick = closeModal;
    $('modal').onclick = e => { if (e.target === $('modal')) closeModal(); };

    $('bExport').onclick = () => {
        const code = SE.exportCs(st, routes());
        openModal('Экспорт в C# — черновик схемы', `
            <div class="hint">Заготовка в стиле <b>Algabas/AlgabasLayout.cs</b>: участки, стрелки, светофоры, найденные маршруты и геометрия.
            Топологию <code>Next()</code> и направление перегона пока дописывают вручную — в полном редакторе это будет генерироваться тоже.</div>
            <textarea id="exCode" readonly spellcheck="false"></textarea>
            <div class="btns"><button id="exCopy">Копировать</button><button id="exSave">Сохранить .cs</button></div>`);
        $('exCode').value = code;
        $('exCopy').onclick = () => {
            $('exCode').select();
            (navigator.clipboard ? navigator.clipboard.writeText(code) : Promise.reject())
                .then(() => msg('Код скопирован'), () => { document.execCommand('copy'); msg('Код скопирован'); });
        };
        $('exSave').onclick = () => {
            const a = document.createElement('a');
            a.href = URL.createObjectURL(new Blob(['﻿' + code], { type: 'text/plain' }));
            a.download = (st.name || 'станция').replace(/[\\/:*?"<>|]+/g, '_') + 'Layout.cs';
            a.click();
        };
    };

    $('bHelp').onclick = () => openModal('Справка', `
        <table>
        <tr><td>Кубики</td><td><kbd>1</kbd> путь, <kbd>2</kbd> изгиб, <kbd>3</kbd> стрелка, <kbd>4</kbd> пересечение,
            <kbd>5</kbd> тупик, <kbd>6</kbd> выход за край схемы, <kbd>7</kbd> переезд. <kbd>R</kbd> / <kbd>Shift+R</kbd> — повернуть.</td></tr>
        <tr><td>Инструменты</td><td><kbd>V</kbd> выбор, <kbd>E</kbd> ластик, <kbd>B</kbd> участок (кисть), <kbd>S</kbd> светофор, <kbd>T</kbd> надпись</td></tr>
        <tr><td>Мышь</td><td>ЛКМ — поставить / выбрать, протягивание — рисовать линией. ПКМ — стереть.
            Колесо — масштаб, средняя кнопка или <kbd>Пробел</kbd>+мышь — сдвинуть схему.</td></tr>
        <tr><td>Выделение</td><td>Рамкой инструментом «Выбор», <kbd>Shift</kbd> — добавить. Тянуть мышью или стрелками — перенести.
            <kbd>Delete</kbd> — удалить, <kbd>Ctrl+A</kbd> — всё.</td></tr>
        <tr><td>Файл</td><td><kbd>Ctrl+S</kbd> — сохранить .json, <kbd>Ctrl+O</kbd> — открыть. Схема ещё и сама сохраняется в браузере.</td></tr>
        <tr><td>Отмена</td><td><kbd>Ctrl+Z</kbd> — отменить, <kbd>Ctrl+Y</kbd> — вернуть</td></tr>
        <tr><td>Вид</td><td><kbd>F</kbd> — вся схема, <kbd>M</kbd> — редактор / испытание</td></tr>
        <tr><td>Как стыкуются</td><td>У клетки 6 выходов: середины левой и правой стороны и 4 угла.
            Соседние кубики стыкуются, если их выходы смотрят друг на друга. Красная точка — выход, который никуда не ведёт.</td></tr>
        <tr><td>Участки</td><td>Стык рисуется сам между кубиками разных участков. Тип участка (путь, перегон, тупик…)
            подбирается по названию: 1П — путь, 2-4СП — стрелочный, НАУ / Б12 — перегон, ВТ — тупик. Поменять — вкладка «Участки».</td></tr>
        <tr><td>Светофоры</td><td>«→» (Ч, чётные) — под путём на правом краю клетки, «←» (Н, нечётные) — над путём на левом.
            Входные и выходные дают поездные маршруты, маневровые и выходные — маневровые.</td></tr>
        <tr><td>Испытание</td><td>ЛКМ по светофору, потом по пути — задать маршрут (стрелки встанут сами).
            ЛКМ по стрелке — перевести. ПКМ по пути — занять / освободить. Пересекающиеся маршруты и занятые участки не дают задать маршрут.</td></tr>
        </table>`);

    let msgTimer = 0;
    function msg(text, bad) {
        const el = $('sMsg');
        el.textContent = text;
        el.className = bad ? 'bad' : '';
        el.style.opacity = 1;
        clearTimeout(msgTimer);
        msgTimer = setTimeout(() => { el.style.opacity = 0; }, 5000);
    }

    // =================================================================
    //  ЗАПУСК
    // =================================================================
    let restored = null;
    try {
        const raw = localStorage.getItem(STORE);
        if (raw) restored = SE.normalize(JSON.parse(raw));
    } catch (e) { restored = null; }

    new ResizeObserver(resize).observe($('stage'));
    resize();
    if (restored && Object.keys(restored.tiles).length) {
        load(restored, true);
        msg('Открыта схема с прошлого раза');
    } else {
        load(SE.EXAMPLES.algabas.build(), true);
        msg('Для начала открыт пример «Алгабас». Новая схема — кнопка «Новая»');
    }
    setTool('place');
})();
