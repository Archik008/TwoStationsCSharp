'use strict';
// =====================================================================
//  ЭКСПОРТ В C# (ЧЕРНОВИК) — заготовка файла схемы в стиле Algabas/AlgabasLayout.cs:
//  BuildSections, BuildSwitches, BuildSignals, BuildRoutes, BuildGeometry.
//  Топологию Next() и направление перегона (DirectionLine) пока надо дописать руками.
// =====================================================================
var SE = window.SE = window.SE || {};

const TRANSLIT = {
    А: 'A', Б: 'B', В: 'V', Г: 'G', Д: 'D', Е: 'E', Ё: 'E', Ж: 'ZH', З: 'Z', И: 'I', Й: 'J', К: 'K', Л: 'L', М: 'M',
    Н: 'N', О: 'O', П: 'P', Р: 'R', С: 'S', Т: 'T', У: 'U', Ф: 'F', Х: 'H', Ц: 'C', Ч: 'CH', Ш: 'SH', Щ: 'SCH',
    Ъ: '', Ы: 'Y', Ь: '', Э: 'E', Ю: 'YU', Я: 'YA', Ә: 'A', Ғ: 'G', Қ: 'Q', Ң: 'NG', Ө: 'O', Ұ: 'U', Ү: 'U', Һ: 'H', І: 'I'
};

function translit(s) {
    return [...(s || '').toUpperCase()].map(ch => TRANSLIT[ch] !== undefined ? TRANSLIT[ch] : ch).join('');
}

const csStr = s => '"' + String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
const num = v => String(Math.round(v * 10) / 10);

SE.exportCs = (st, routes) => {
    const C = SE.CELL;
    const used = new Set();
    const ident = (prefix, name) => {
        let s = prefix + translit(name).replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
        if (s === prefix) s = prefix + 'X';
        if (/^[0-9]/.test(s)) s = 'SIG_' + s;      // проходные «1», «2» — имя C# не может начинаться с цифры
        let r = s, i = 2;
        while (used.has(r)) r = s + '_' + i++;
        used.add(r);
        return r;
    };

    // ---- участки ----
    const secId = new Map();
    for (const n of SE.sectionNames(st)) secId.set(n, ident('S_', n));

    // ---- стрелки (спаренные — одна RailSwitch) ----
    const swGroups = new Map();     // группа → { label, keys }
    for (const [k, t] of Object.entries(st.tiles)) {
        if (t.t !== 'switch') continue;
        const g = SE.switchGroup(k, t);
        if (!swGroups.has(g)) swGroups.set(g, { label: t.group || t.name || k, keys: [] });
        swGroups.get(g).keys.push(k);
    }
    const swId = new Map();
    for (const [g, x] of swGroups) swId.set(g, ident('SW_', x.label));

    // ---- светофоры ----
    const sigs = SE.allSignals(st);
    const sigId = new Map();
    for (const s of sigs) sigId.set(s.key + '#' + s.idx, ident('', s.sig.name || 'SIG'));
    const sid = s => sigId.get(s.key + '#' + s.idx);

    // смещение: левый верхний кубик — в (1, 1) клетку
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const k of [...Object.keys(st.tiles), ...Object.keys(st.texts)]) {
        const [x, y] = SE.parse(k);
        minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
    }
    if (minX === Infinity) { minX = minY = 0; maxX = maxY = 0; }
    const X = gx => (gx - minX + 1) * C, Y = gy => (gy - minY + 1) * C;
    const pt = (x, y, d) => [X(x) + SE.DIRS[d].px * C, Y(y) + SE.DIRS[d].py * C];
    const ctr = (x, y) => [X(x) + C / 2, Y(y) + C / 2];
    const tup = p => `(${num(p[0])}, ${num(p[1])})`;

    const L = [];
    const w = s => L.push(s);
    const date = new Date().toLocaleString('ru-RU');

    w('// =====================================================================');
    w(`//  ЧЕРНОВИК СХЕМЫ «${st.name}» — сгенерирован редактором станций ${date}`);
    w('//  Стиль — как Algabas/AlgabasLayout.cs (там же вспомогательные S, Sw, Ctl, Sig, Route, P, M).');
    w('//  Проверьте названия, лампы и маршруты. Next() (топология) и DirectionLine — пока вручную.');
    w('// =====================================================================');
    w('');
    if (secId.size) w(`public TrackSection ${[...secId.values()].join(', ')};`);
    if (swId.size) w(`public RailSwitch ${[...swId.values()].join(', ')};`);
    if (sigs.length) w(`public RailSignal ${sigs.map(sid).join(', ')};`);
    w('');

    // ---------------- BuildSections ----------------
    w('void BuildSections()');
    w('{');
    const SEC_TITLE = { path: 'Путь', plain: 'Участок', line: 'Блок-участок перегона', dead: 'Тупик', switch: 'Стрелочный участок' };
    for (const [n, idn] of secId) w(`    ${idn} = S(${csStr(n)}, ${csStr((SEC_TITLE[SE.secType(st, n)] || 'Участок') + ' ' + n)});`);
    w('}');
    w('');

    // ---------------- BuildSwitches ----------------
    w('void BuildSwitches()');
    w('{');
    for (const [g, x] of swGroups) {
        const secs = [...new Set(x.keys.map(k => st.tiles[k].sec).filter(Boolean))].map(n => secId.get(n));
        const title = x.keys.length > 1 ? `Съезд ${x.label}` : `Стрелка ${x.label}`;
        w(`    ${swId.get(g)} = Sw(${csStr(x.label)}, ${csStr(title)}${secs.length ? ', ' + secs.join(', ') : ''});`);
    }
    w('}');
    w('');

    // ---------------- BuildSignals ----------------
    const LAMPS = { entry: 'L_ENTRY', exit: 'L_EXIT', shunt: 'L_SHUNT_BLUE', block: 'L_BLOCK' };
    w('void BuildSignals()');
    w('{');
    for (const s of sigs) {
        const dir = s.sig.dir === 'R' ? 'Dir.Right' : 'Dir.Left';
        const name = s.sig.name || '?';
        const title = `${SE.SIG_KINDS[s.sig.kind]} ${name}`;
        if (s.sig.kind === 'block')
            w(`    ${sid(s)} = Sig(${csStr(name)}, ${csStr(name)}, ${csStr(title)}, SignalKind.Automatic, ${dir}, L_BLOCK);`);
        else if (s.sig.kind === 'other')
            w(`    ${sid(s)} = Sig(${csStr(name)}, ${csStr(name)}, ${csStr(title)}, SignalKind.Neighbor, ${dir}, new[] { "yellow", "red", "yellow1", "white" });`);
        else
            w(`    ${sid(s)} = Ctl(${csStr(name)}, ${csStr(title)}, ${dir}, ${LAMPS[s.sig.kind]});`);
    }
    const appr = sigs.filter(s => s.tile.sec && s.sig.kind !== 'other');
    if (appr.length) {
        w('');
        w('    // участки приближения (на чём стоит поезд перед светофором)');
        for (const s of appr) w(`    ${sid(s)}.Approach = ${secId.get(s.tile.sec)};`);
    }
    w('}');
    w('');

    // ---------------- BuildRoutes ----------------
    w('void BuildRoutes()');
    w('{');
    for (const shunt of [false, true]) {
        const list = routes.filter(r => r.shunt === shunt);
        if (!list.length) continue;
        w(`    // ======== ${shunt ? 'МАНЕВРОВЫЕ' : 'ПОЕЗДНЫЕ'} (найдены автоматически: ${list.length}) ========`);
        for (const r of list) {
            const startSig = sigs.find(s => s.key === r.start.key && s.idx === r.start.idx);
            const end = SE.routeEndSignal(st, r);
            const secs = r.sections.map(n => secId.get(n)).join(', ');
            const sws = r.switches.map(x => `${x.plus ? 'P' : 'M'}(${swId.get(x.group)})`).join(', ');
            let line = `    Route(${shunt}, ${sid(startSig)}, ${end ? sid(end) : 'null'}, ${secId.get(r.dest) || 'null'}, ${csStr(r.name)}, ` +
                `new TrackSection[] { ${secs} }, new (RailSwitch, bool)[] { ${sws} }`;
            if (r.diverging) line += ', div: true';
            if (r.exit && secId.get(r.dest)) line += `, exit: ${secId.get(r.dest)}`;
            w(line + ');');
        }
        w('');
    }
    if (!routes.length) w('    // маршрутов не найдено — расставьте светофоры и типы участков');
    w('}');
    w('');

    // ---------------- BuildGeometry ----------------
    w('void BuildGeometry()');
    w('{');
    w('    var g = G;');
    w(`    g.DesignW = ${num(X(maxX) + 2 * C)};`);
    w(`    g.DesignH = ${num(Y(maxY) + 2 * C)};`);
    w('    string id(TrackSection s) => s.Id;');
    w('');
    let pieceN = 0;
    const pid = () => csStr('e' + (++pieceN));
    const secRef = t => t.sec ? `id(${secId.get(t.sec)})` : '""';

    // горизонтальные пути одного участка подряд — одним куском
    const flat = Object.entries(st.tiles).filter(([, t]) => (t.t === 'straight' && t.v === 0) || t.t === 'crossing')
        .map(([k, t]) => ({ k, t, xy: SE.parse(k) })).sort((a, b) => a.xy[1] - b.xy[1] || a.xy[0] - b.xy[0]);
    const done = new Set();
    for (let i = 0; i < flat.length;) {
        let j = i;
        while (j + 1 < flat.length && flat[j + 1].xy[1] === flat[i].xy[1] && flat[j + 1].xy[0] === flat[j].xy[0] + 1 &&
               (flat[j + 1].t.sec || '') === (flat[i].t.sec || '')) j++;
        const a = flat[i], b = flat[j];
        w(`    g.Piece(${pid()}, ${secRef(a.t)}, ${tup(pt(a.xy[0], a.xy[1], 'W'))}, ${tup(pt(b.xy[0], b.xy[1], 'E'))});`);
        for (let q = i; q <= j; q++) {
            done.add(flat[q].k);
            if (flat[q].t.t === 'crossing') w(`    // переезд у ${tup(ctr(...flat[q].xy))} — LevelCrossing добавить вручную`);
        }
        i = j + 1;
    }
    for (const [k, t] of Object.entries(st.tiles)) {
        if (done.has(k)) continue;
        const [x, y] = SE.parse(k);
        const c = ctr(x, y);
        const v = SE.variant(t);
        if (t.t === 'switch') {
            const sw = swId.get(SE.switchGroup(k, t));
            w(`    g.Piece(${pid()}, ${secRef(t)}, ${tup(pt(x, y, v.trunk))}, ${tup(c)});`);
            w(`    g.Branch(${pid()}, ${secRef(t)}, ${sw}.Id, true, true, ${tup(c)}, ${tup(pt(x, y, v.plus))});`);
            w(`    g.Branch(${pid()}, ${secRef(t)}, ${sw}.Id, false, true, ${tup(c)}, ${tup(pt(x, y, v.minus))});`);
        } else if (t.t === 'buffer') {
            w(`    g.Piece(${pid()}, ${secRef(t)}, ${tup(pt(x, y, v.ends[0]))}, ${tup(c)});`);
            w(`    g.Buffers.Add(new PointF(${num(c[0])}, ${num(c[1])}));`);
        } else if (t.t === 'exit') {
            w(`    g.Piece(${pid()}, ${secRef(t)}, ${tup(pt(x, y, v.ends[0]))}, ${tup(pt(x, y, SE.OPP[v.ends[0]]))});   // выход за край схемы`);
        } else {
            for (const e of SE.edgesOf(t)) {
                const pts = SE.OPP[e.a] === e.b ? [pt(x, y, e.a), pt(x, y, e.b)] : [pt(x, y, e.a), c, pt(x, y, e.b)];
                w(`    g.Piece(${pid()}, ${secRef(t)}, ${pts.map(tup).join(', ')});`);
            }
        }
    }
    w('');
    w('    // изолирующие стыки — там, где соседние кубики относятся к разным участкам');
    for (const [k, t] of Object.entries(st.tiles)) {
        const [x, y] = SE.parse(k);
        for (const d of SE.endsOf(t)) {
            const nb = SE.neighbor(st, k, d);
            if (nb && SE.isRight(d) && (t.sec || '') !== (nb.tile.sec || '')) {
                const p = pt(x, y, d);
                w(`    g.Joint(${num(p[0])}, ${num(p[1])});`);
            }
        }
    }
    w('');
    w('    // светофоры: «←» над путём, «→» под путём');
    for (const s of sigs) {
        const [x, y] = SE.parse(s.key);
        const want = s.sig.dir === 'R' ? 'E' : 'W';
        const ends = SE.endsOf(s.tile).filter(d => (s.sig.dir === 'R') === SE.isRight(d));
        const p = pt(x, y, ends.includes(want) ? want : (ends[0] || want));
        w(`    g.Sig(${sid(s)}, ${num(p[0])}, ${num(p[1])}, ${s.sig.dir === 'L'});`);
    }
    w('');
    w('    // номера стрелок');
    for (const [k, t] of Object.entries(st.tiles)) {
        if (t.t !== 'switch') continue;
        const [x, y] = SE.parse(k);
        const below = SE.variant(t).minus.includes('N');
        w(`    g.SwLabel(${csStr(t.name || '?')}, ${swId.get(SE.switchGroup(k, t))}, ${num(X(x) + C / 2)}, ${num(Y(y) + (below ? C * 0.9 : C * 0.1))});`);
    }
    const texts = Object.entries(st.texts);
    if (texts.length) {
        w('');
        w('    // надписи');
        for (const [k, tx] of texts) {
            const [x, y] = SE.parse(k);
            w(`    g.Text(${csStr(tx.text)}, ${num(X(x) + 3)}, ${num(Y(y) + C / 2 - 8)}, ${num((tx.size || 14) * 0.7)}f, true);`);
        }
    }
    w('');
    w('    g.AutoSectionLabels();');
    w('}');
    return L.join('\r\n');
};
