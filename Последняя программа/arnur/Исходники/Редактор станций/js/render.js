'use strict';
// =====================================================================
//  РИСОВАНИЕ СХЕМЫ (canvas). Цвета — как у классической палитры программ АКЖТ / Алгабас
//  (Ui/SchemePalette.cs): зелёно-серый фон, белые пути, жёлтый маршрут, красная занятость.
// =====================================================================
var SE = window.SE = window.SE || {};

SE.PAL = {
    bg: '#7aa69b', grid: 'rgba(255,255,255,0.12)', gridMajor: 'rgba(255,255,255,0.28)',
    track: '#ffffff', route: '#ffeb00', occ: '#e11919', text: '#233732', dark: '#1b2a26',
    bad: '#d4141c', sel: '#ffeb00', lampRed: '#e11919', lampBlue: '#2b63d9', lampGreen: '#2bd65a',
    lampWhite: '#ffffff', lampOther: '#6b7472', road: '#3c4b47'
};

function pnt(x, y, cs, d) {
    const D = SE.DIRS[d];
    return [x + D.px * cs, y + D.py * cs];
}

function poly(ctx, pts) {
    ctx.beginPath();
    ctx.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
    ctx.stroke();
}

/** Нарисовать путь кубика. o.color — цвет пути, o.width — толщина. */
SE.drawTrack = (ctx, t, x, y, cs, o = {}) => {
    const color = o.color || SE.PAL.track;
    const w = o.width || Math.max(2, cs * 0.13);
    const c = [x + cs / 2, y + cs / 2];
    const v = SE.variant(t);
    ctx.save();
    ctx.strokeStyle = color;
    ctx.lineWidth = w;
    ctx.lineCap = 'butt';
    ctx.lineJoin = 'round';

    if (t.t === 'switch') {
        // остряк и включённое направление — сплошные, второе направление — с разрывом у остряка
        const tr = pnt(x, y, cs, v.trunk), pl = pnt(x, y, cs, v.plus), mi = pnt(x, y, cs, v.minus);
        const plusOn = t.plus !== false;
        const on = plusOn ? pl : mi, off = plusOn ? mi : pl;
        poly(ctx, [tr, c, on]);
        const g = [c[0] + (off[0] - c[0]) * 0.45, c[1] + (off[1] - c[1]) * 0.45];
        poly(ctx, [g, off]);
    } else if (t.t === 'buffer') {
        const e = pnt(x, y, cs, v.ends[0]);
        poly(ctx, [e, c]);
        ctx.lineWidth = w * 1.2;
        poly(ctx, [[c[0], c[1] - cs * 0.24], [c[0], c[1] + cs * 0.24]]);
    } else if (t.t === 'exit') {
        // внутрь станции — сплошная линия, наружу — пунктир со стрелкой «уходит дальше»
        const inn = pnt(x, y, cs, v.ends[0]), out = pnt(x, y, cs, SE.OPP[v.ends[0]]);
        poly(ctx, [inn, c]);
        ctx.setLineDash([cs * 0.1, cs * 0.08]);
        poly(ctx, [c, out]);
        ctx.setLineDash([]);
        const s = out[0] < c[0] ? -1 : 1;
        ctx.lineWidth = Math.max(1.5, w * 0.6);
        poly(ctx, [[out[0] - s * cs * 0.18, out[1] - cs * 0.16], out, [out[0] - s * cs * 0.18, out[1] + cs * 0.16]]);
    } else {
        for (const e of SE.edgesOf(t)) {
            const A = pnt(x, y, cs, e.a), B = pnt(x, y, cs, e.b);
            poly(ctx, SE.OPP[e.a] === e.b ? [A, B] : [A, c, B]);
        }
        if (t.t === 'crossing') {
            ctx.strokeStyle = o.road || SE.PAL.road;
            ctx.lineWidth = Math.max(1, cs * 0.05);
            for (const k of [0.28, 0.72]) poly(ctx, [[x + cs * k, y + cs * 0.1], [x + cs * k, y + cs * 0.9]]);
            if (o.crossingClosed) {
                ctx.fillStyle = SE.PAL.occ;
                ctx.fillRect(x + cs * 0.4, y + cs * 0.08, cs * 0.2, cs * 0.14);
            }
        }
    }
    ctx.restore();
};

// ---------------------------------------------------------------------
//  СВЕТОФОРЫ: «→» — под путём, «←» — над путём (как в SchemeGeometry.Sig);
//  мачта — на краю клетки по ходу движения, лампы уходят от мачты по ходу движения
//  (как SchemePanel.DrawSignal: у «←» лампы влево, у «→» вправо)
// ---------------------------------------------------------------------
SE.sigAnchor = (t, x, y, cs, dir) => {
    const want = dir === 'R' ? 'E' : 'W';
    const ends = SE.endsOf(t).filter(d => (dir === 'R') === SE.isRight(d));
    const d = ends.includes(want) ? want : (ends[0] || want);
    return pnt(x, y, cs, d);
};

SE.sigGeom = (t, sig, x, y, cs) => {
    const [ax, ay] = SE.sigAnchor(t, x, y, cs, sig.dir);
    const side = sig.dir === 'R' ? 1 : -1, fwd = side;
    const r = cs * 0.12, leg = cs * 0.12;
    const sy = ay + side * cs * 0.4;
    const n = sig.kind === 'entry' ? 2 : 1;
    const lamps = [];
    for (let i = 0; i < n; i++) lamps.push([ax + fwd * (leg + r + i * 2 * r), sy]);
    const xs = [ax, ...lamps.map(l => l[0] + fwd * r)];
    const label = [ax + fwd * (leg + r * n), sy + side * (r + cs * 0.22)];
    return {
        ax, ay, sy, side, fwd, r, lamps, label,
        box: { x1: Math.min(...xs) - 3, x2: Math.max(...xs) + 3, y1: Math.min(ay, label[1]) - cs * 0.15, y2: Math.max(ay, label[1]) + cs * 0.15 }
    };
};

SE.lampColor = (sig, open) => {
    if (sig.kind === 'other') return SE.PAL.lampOther;
    if (sig.kind === 'shunt') return open ? SE.PAL.lampWhite : SE.PAL.lampBlue;
    return open ? SE.PAL.lampGreen : SE.PAL.lampRed;
};

SE.drawSignal = (ctx, t, sig, x, y, cs, o = {}) => {
    const g = SE.sigGeom(t, sig, x, y, cs);
    ctx.save();
    ctx.strokeStyle = SE.PAL.dark;
    ctx.lineWidth = Math.max(1, cs * 0.045);
    poly(ctx, [[g.ax, g.ay + g.side * cs * 0.1], [g.ax, g.sy], [g.ax + g.fwd * cs * 0.12, g.sy]]);
    for (const [lx, ly] of g.lamps) {
        ctx.beginPath();
        ctx.arc(lx, ly, g.r, 0, Math.PI * 2);
        ctx.fillStyle = SE.lampColor(sig, o.open);
        ctx.fill();
        ctx.stroke();
    }
    if (o.label !== false && cs >= 14) {
        ctx.fillStyle = sig.name ? SE.PAL.text : SE.PAL.bad;
        ctx.font = `bold ${Math.round(cs * 0.27)}px Segoe UI, Arial, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(sig.name || '?', g.label[0], g.label[1]);
    }
    if (o.ring) {
        ctx.strokeStyle = o.ringColor || SE.PAL.sel;
        ctx.lineWidth = 2;
        ctx.setLineDash([4, 3]);
        const b = g.box;
        ctx.strokeRect(b.x1, b.y1, b.x2 - b.x1, b.y2 - b.y1);
    }
    ctx.restore();
};

/** Где написать номер стрелки: с той стороны, куда НЕ уходит отклонение. */
SE.switchLabelPos = (t, x, y, cs) => {
    const v = SE.variant(t);
    const below = v.minus.includes('N');
    return [x + cs * 0.5, y + (below ? cs * 0.9 : cs * 0.1)];
};

/** Точка и наклон стыка на выходе dir. */
function drawJoint(ctx, x, y, cs, dir) {
    const [px, py] = pnt(x, y, cs, dir);
    const D = SE.DIRS[dir];
    const len = Math.hypot(D.dx, D.dy);
    const nx = -D.dy / len, ny = D.dx / len;
    const h = cs * 0.2;
    poly(ctx, [[px - nx * h, py - ny * h], [px + nx * h, py + ny * h]]);
}

// ---------------------------------------------------------------------
//  ВСЯ СХЕМА
// ---------------------------------------------------------------------
SE.render = (ctx, W, H, st, ui) => {
    const cs = SE.CELL * ui.cam.z;
    const ox = ui.cam.x, oy = ui.cam.y;
    const test = ui.mode === 'test';
    const T = ui.test;
    const sx = gx => ox + gx * cs, sy = gy => oy + gy * cs;

    ctx.fillStyle = SE.PAL.bg;
    ctx.fillRect(0, 0, W, H);

    const gx0 = Math.floor(-ox / cs) - 1, gx1 = Math.ceil((W - ox) / cs) + 1;
    const gy0 = Math.floor(-oy / cs) - 1, gy1 = Math.ceil((H - oy) / cs) + 1;
    const visible = k => {
        const [x, y] = SE.parse(k);
        return x >= gx0 && x <= gx1 && y >= gy0 && y <= gy1;
    };

    // ---- сетка ----
    if (ui.showGrid && !test && cs >= 8) {
        ctx.lineWidth = 1;
        for (let gx = gx0; gx <= gx1; gx++) {
            ctx.strokeStyle = gx % 5 === 0 ? SE.PAL.gridMajor : SE.PAL.grid;
            ctx.beginPath(); ctx.moveTo(Math.round(sx(gx)) + 0.5, 0); ctx.lineTo(Math.round(sx(gx)) + 0.5, H); ctx.stroke();
        }
        for (let gy = gy0; gy <= gy1; gy++) {
            ctx.strokeStyle = gy % 5 === 0 ? SE.PAL.gridMajor : SE.PAL.grid;
            ctx.beginPath(); ctx.moveTo(0, Math.round(sy(gy)) + 0.5); ctx.lineTo(W, Math.round(sy(gy)) + 0.5); ctx.stroke();
        }
    }

    const entries = Object.entries(st.tiles).filter(([k]) => visible(k));
    const hidden = ui.moving ? ui.moving.keys : null;   // клетки, которые сейчас перетаскивают

    // ---- цвета участков ----
    if (ui.showSecColors && !test) {
        ctx.save();
        ctx.globalAlpha = 0.38;
        for (const [k, t] of entries) {
            if (!t.sec || (hidden && hidden.has(k))) continue;
            const [x, y] = SE.parse(k);
            ctx.fillStyle = SE.secColor(st, t.sec);
            ctx.fillRect(sx(x), sy(y), cs, cs);
        }
        ctx.restore();
    }
    if (ui.flashSec && !test) {
        ctx.save();
        ctx.strokeStyle = SE.PAL.sel;
        ctx.lineWidth = 3;
        for (const [k, t] of entries) {
            if (t.sec !== ui.flashSec) continue;
            const [x, y] = SE.parse(k);
            ctx.strokeRect(sx(x) + 2, sy(y) + 2, cs - 4, cs - 4);
        }
        ctx.restore();
    }

    // ---- пути ----
    for (const [k, t] of entries) {
        if (hidden && hidden.has(k)) continue;
        const [x, y] = SE.parse(k);
        let color = SE.PAL.track;
        if (test) {
            if (t.sec && T.occ.has(t.sec)) color = SE.PAL.occ;
            else if (T.routeTiles.has(k)) color = SE.PAL.route;
        } else if (ui.preview && ui.preview.tiles.has(k)) color = SE.PAL.route;
        SE.drawTrack(ctx, t, sx(x), sy(y), cs, {
            color, crossingClosed: test && t.t === 'crossing' && t.sec && T.occ.has(t.sec)
        });
    }

    // ---- стыки (где соседние клетки — разные участки) и разрывы ----
    ctx.save();
    ctx.lineWidth = Math.max(1.5, cs * 0.06);
    for (const [k, t] of entries) {
        if (hidden && hidden.has(k)) continue;
        const [x, y] = SE.parse(k);
        for (const d of SE.endsOf(t)) {
            const nb = SE.neighbor(st, k, d);
            if (nb) {
                if (SE.isRight(d) && (t.sec || '') !== (nb.tile.sec || '')) {
                    ctx.strokeStyle = SE.PAL.dark;
                    drawJoint(ctx, sx(x), sy(y), cs, d);
                }
            } else if (!test && t.t !== 'exit') {
                const [px, py] = pnt(sx(x), sy(y), cs, d);
                ctx.fillStyle = SE.PAL.bad;
                ctx.beginPath();
                ctx.arc(px, py, Math.max(3, cs * 0.09), 0, Math.PI * 2);
                ctx.fill();
            }
        }
    }
    ctx.restore();

    // ---- номера стрелок ----
    if (cs >= 14) {
        ctx.save();
        ctx.font = `bold ${Math.round(cs * 0.3)}px Segoe UI, Arial, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        for (const [k, t] of entries) {
            if (t.t !== 'switch' || (hidden && hidden.has(k))) continue;
            const [x, y] = SE.parse(k);
            const [lx, ly] = SE.switchLabelPos(t, sx(x), sy(y), cs);
            ctx.fillStyle = t.name ? SE.PAL.text : SE.PAL.bad;
            ctx.fillText(t.name || '?', lx, ly);
        }
        ctx.restore();
    }

    // ---- названия участков ----
    if (ui.showSecNames && cs >= 14) {
        ctx.save();
        ctx.font = `${Math.round(cs * 0.26)}px Segoe UI, Arial, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillStyle = '#16302a';
        for (const [sec, keys] of SE.tilesBySection(st)) {
            const flat = keys.filter(k => { const t = st.tiles[k]; return (t.t === 'straight' && t.v === 0) || t.t === 'crossing'; });
            const pool = (flat.length ? flat : keys).map(SE.parse).sort((a, b) => a[0] - b[0] || a[1] - b[1]);
            const [x, y] = pool[Math.floor((pool.length - 1) / 2)];
            if (!visible(SE.key(x, y))) continue;
            ctx.fillText(sec, sx(x) + cs / 2, sy(y) + cs * 0.88);
        }
        ctx.restore();
    }

    // ---- надписи ----
    ctx.save();
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.fillStyle = SE.PAL.text;
    for (const [k, tx] of Object.entries(st.texts)) {
        if (!visible(k) || (hidden && hidden.has(k))) continue;
        const [x, y] = SE.parse(k);
        ctx.font = `bold ${Math.max(6, Math.round((tx.size || 14) * ui.cam.z))}px Segoe UI, Arial, sans-serif`;
        ctx.fillText(tx.text, sx(x) + 3, sy(y) + cs / 2);
    }
    ctx.restore();

    // ---- светофоры ----
    for (const [k, t] of entries) {
        if (!t.sigs || (hidden && hidden.has(k))) continue;
        const [x, y] = SE.parse(k);
        t.sigs.forEach((s, i) => {
            const id = k + '#' + i;
            const pending = test && T.pending && T.pending.key === k && T.pending.idx === i;
            const selected = !test && ui.selSig && ui.selSig.key === k && ui.selSig.idx === i;
            SE.drawSignal(ctx, t, s, sx(x), sy(y), cs, {
                open: test && T.openSigs.has(id),
                ring: selected || pending,
                ringColor: pending ? '#ffffff' : SE.PAL.sel
            });
        });
    }

    // ---- перетаскивание выделения ----
    if (ui.moving) {
        ctx.save();
        ctx.globalAlpha = 0.7;
        const { dx, dy } = ui.moving;
        for (const k of ui.moving.keys) {
            const t = st.tiles[k];
            const [x, y] = SE.parse(k);
            if (t) {
                SE.drawTrack(ctx, t, sx(x + dx), sy(y + dy), cs);
                (t.sigs || []).forEach(s => SE.drawSignal(ctx, t, s, sx(x + dx), sy(y + dy), cs));
            }
            if (st.texts[k]) {
                ctx.font = `bold ${Math.round((st.texts[k].size || 14) * ui.cam.z)}px Segoe UI, Arial, sans-serif`;
                ctx.fillStyle = SE.PAL.text;
                ctx.textBaseline = 'middle';
                ctx.fillText(st.texts[k].text, sx(x + dx) + 3, sy(y + dy) + cs / 2);
            }
        }
        ctx.restore();
    }

    // ---- выделение ----
    if (!test && ui.sel.size) {
        ctx.save();
        ctx.strokeStyle = SE.PAL.sel;
        ctx.lineWidth = 2;
        ctx.setLineDash([5, 3]);
        const d = ui.moving || { dx: 0, dy: 0 };
        for (const k of ui.sel) {
            const [x, y] = SE.parse(k);
            ctx.strokeRect(sx(x + d.dx) + 1, sy(y + d.dy) + 1, cs - 2, cs - 2);
        }
        ctx.restore();
    }
    if (ui.band) {
        const b = ui.band;
        ctx.save();
        ctx.fillStyle = 'rgba(255,235,0,0.12)';
        ctx.strokeStyle = SE.PAL.sel;
        ctx.setLineDash([4, 3]);
        const x1 = Math.min(b.x1, b.x2), y1 = Math.min(b.y1, b.y2);
        ctx.fillRect(sx(x1), sy(y1), (Math.abs(b.x2 - b.x1) + 1) * cs, (Math.abs(b.y2 - b.y1) + 1) * cs);
        ctx.strokeRect(sx(x1), sy(y1), (Math.abs(b.x2 - b.x1) + 1) * cs, (Math.abs(b.y2 - b.y1) + 1) * cs);
        ctx.restore();
    }

    // ---- подсказка под курсором (что поставится) ----
    if (ui.hover && !test) {
        const [x, y] = ui.hover;
        ctx.save();
        if (ui.ghost) {
            ctx.globalAlpha = 0.55;
            SE.drawTrack(ctx, ui.ghost, sx(x), sy(y), cs, { color: '#fffbd0' });
            ctx.globalAlpha = 1;
        }
        ctx.strokeStyle = ui.hoverColor || 'rgba(255,255,255,0.8)';
        ctx.lineWidth = 2;
        ctx.strokeRect(sx(x) + 1, sy(y) + 1, cs - 2, cs - 2);
        ctx.restore();
    }
};
