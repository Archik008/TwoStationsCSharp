'use strict';
// =====================================================================
//  КУБИКИ СХЕМЫ И МОДЕЛЬ СТАНЦИИ
//
//  Схема — сетка клеток («кубиков»). Клетка соединяется с соседями через 6 выходов:
//  W, E — середины левой и правой стороны; NW, NE, SW, SE — углы.
//  Так собираются пути и съезды под 45°, как на схемах ДСП.
//
//  Станция (то, что сохраняется в файл .json):
//    tiles    — "x,y" → { t: тип кубика, v: вариант (поворот), sec: участок,
//                         name: номер стрелки, group: съезд (спаренные стрелки), plus: положение,
//                         sigs: [{ dir: 'R' | 'L', kind, name }] }
//    sections — участок → { type, color }
//    texts    — "x,y" → { text, size }
// =====================================================================
var SE = window.SE = window.SE || {};

SE.CELL = 40;   // размер клетки в проектных единицах (как DesignW/DesignH в SchemeGeometry.cs)

SE.DIRS = {
    E: { dx: 1, dy: 0, px: 1, py: 0.5 },
    NE: { dx: 1, dy: -1, px: 1, py: 0 },
    SE: { dx: 1, dy: 1, px: 1, py: 1 },
    W: { dx: -1, dy: 0, px: 0, py: 0.5 },
    NW: { dx: -1, dy: -1, px: 0, py: 0 },
    SW: { dx: -1, dy: 1, px: 0, py: 1 },
};
SE.OPP = { E: 'W', W: 'E', NE: 'SW', SW: 'NE', SE: 'NW', NW: 'SE' };
/** Выход ведёт вправо (движение «→», чётное) */
SE.isRight = d => d === 'E' || d === 'NE' || d === 'SE';

// ---------------------------------------------------------------------
//  ТИПЫ КУБИКОВ. variants — повороты / отражения (клавиша R перебирает их)
// ---------------------------------------------------------------------
SE.TYPES = {
    straight: {
        name: 'Путь', key: '1', hint: 'прямой участок пути: горизонталь или диагональ',
        variants: [{ edges: [['W', 'E']] }, { edges: [['SW', 'NE']] }, { edges: [['NW', 'SE']] }]
    },
    curve: {
        name: 'Изгиб', key: '2', hint: 'переход с горизонтали на диагональ (съезд, ответвление)',
        variants: [{ edges: [['W', 'NE']] }, { edges: [['W', 'SE']] }, { edges: [['E', 'NW']] }, { edges: [['E', 'SW']] }]
    },
    switch: {
        name: 'Стрелка', key: '3', hint: 'остряк, прямое направление (плюс) и отклонение (минус)',
        variants: [
            { trunk: 'W', plus: 'E', minus: 'NE' }, { trunk: 'W', plus: 'E', minus: 'SE' },
            { trunk: 'E', plus: 'W', minus: 'NW' }, { trunk: 'E', plus: 'W', minus: 'SW' }]
    },
    cross: {
        name: 'Пересечение', key: '4', hint: 'глухое пересечение путей',
        variants: [{ edges: [['W', 'E'], ['SW', 'NE']] }, { edges: [['W', 'E'], ['NW', 'SE']] }, { edges: [['SW', 'NE'], ['NW', 'SE']] }]
    },
    buffer: {
        name: 'Тупик', key: '5', hint: 'упор в конце тупикового пути',
        variants: [{ ends: ['W'] }, { ends: ['E'] }]
    },
    exit: {
        name: 'Выход', key: '6', hint: 'путь уходит за край схемы: перегон, тоннель, соседняя станция',
        variants: [{ ends: ['E'] }, { ends: ['W'] }]
    },
    crossing: {
        name: 'Переезд', key: '7', hint: 'путь с автомобильным переездом',
        variants: [{ edges: [['W', 'E']] }]
    },
};

SE.SIG_KINDS = {
    entry: 'Входной', exit: 'Выходной', shunt: 'Маневровый', block: 'Проходной', other: 'Чужой (соседа)'
};
SE.SEC_TYPES = {
    path: 'Путь (приём / отправление)', plain: 'Участок между светофорами', line: 'Перегон, блок-участок',
    dead: 'Тупик, вытяжка', switch: 'Стрелочный (сам — есть стрелка)'
};

SE.key = (x, y) => x + ',' + y;
SE.parse = k => k.split(',').map(Number);

SE.variant = t => {
    const vs = SE.TYPES[t.t].variants;
    return vs[t.v] || vs[0];
};

/** Рёбра кубика: пары выходов, по которым едет поезд. У тупика и выхода второй конец — null (центр клетки). */
SE.edgesOf = t => {
    const v = SE.variant(t);
    if (t.t === 'switch') return [{ a: v.trunk, b: v.plus, role: 'plus' }, { a: v.trunk, b: v.minus, role: 'minus' }];
    if (v.ends) return [{ a: v.ends[0], b: null }];
    return v.edges.map(e => ({ a: e[0], b: e[1] }));
};

SE.endsOf = t => {
    const s = new Set();
    for (const e of SE.edgesOf(t)) { s.add(e.a); if (e.b) s.add(e.b); }
    return [...s];
};

/** Куда можно выехать из кубика, въехав через сторону inSide.
 *  need: null — всегда; true / false — только при стрелке в плюсе / минусе. */
SE.through = (t, inSide) => {
    if (t.t === 'switch') {
        const v = SE.variant(t);
        if (inSide === v.trunk) return [{ out: v.plus, need: true }, { out: v.minus, need: false }];
        if (inSide === v.plus) return [{ out: v.trunk, need: true }];
        if (inSide === v.minus) return [{ out: v.trunk, need: false }];
        return [];
    }
    const res = [];
    for (const e of SE.edgesOf(t)) {
        if (!e.b) continue;
        if (e.a === inSide) res.push({ out: e.b, need: null });
        else if (e.b === inSide) res.push({ out: e.a, need: null });
    }
    return res;
};

/** Сосед через выход dir, если он стыкуется с этим выходом (иначе null). */
SE.neighbor = (st, key, dir) => {
    const [x, y] = SE.parse(key);
    const D = SE.DIRS[dir];
    const nk = SE.key(x + D.dx, y + D.dy);
    const nt = st.tiles[nk];
    if (!nt || !SE.endsOf(nt).includes(SE.OPP[dir])) return null;
    return { key: nk, tile: nt, inSide: SE.OPP[dir] };
};

// ---------------------------------------------------------------------
//  СТАНЦИЯ
// ---------------------------------------------------------------------
SE.newStation = () => ({ format: 'station-editor', version: 1, name: 'Новая станция', tiles: {}, sections: {}, texts: {} });

SE.clone = o => JSON.parse(JSON.stringify(o));

/** Проверка и дочистка загруженного файла. Бросает ошибку с понятным текстом. */
SE.normalize = raw => {
    if (!raw || typeof raw !== 'object' || !raw.tiles) throw new Error('это не файл редактора станций (нет раздела tiles)');
    const st = Object.assign(SE.newStation(), raw);
    for (const [k, t] of Object.entries(st.tiles)) {
        if (!SE.TYPES[t.t]) { delete st.tiles[k]; continue; }
        if (!(t.v >= 0 && t.v < SE.TYPES[t.t].variants.length)) t.v = 0;
        t.sec = t.sec || '';
        if (t.sigs && !t.sigs.length) delete t.sigs;
    }
    st.sections = st.sections || {};
    st.texts = st.texts || {};
    return st;
};

SE.tilesBySection = st => {
    const m = new Map();
    for (const [k, t] of Object.entries(st.tiles)) {
        if (!t.sec) continue;
        if (!m.has(t.sec)) m.set(t.sec, []);
        m.get(t.sec).push(k);
    }
    return m;
};

/** Все участки: и описанные в sections, и встреченные на клетках. */
SE.sectionNames = st => {
    const s = new Set(Object.keys(st.sections));
    for (const t of Object.values(st.tiles)) if (t.sec) s.add(t.sec);
    return [...s].sort((a, b) => a.localeCompare(b, 'ru', { numeric: true }));
};

/** Тип участка по названию — как принято на схемах (1П, 2-4СП, НАУ, Б12, ВТ…). Можно поменять вручную. */
SE.guessSecType = name => {
    const n = (name || '').toUpperCase();
    if (/СП$/.test(n)) return 'switch';
    if (/^(I|II|III|IV|V|\d+)П$/.test(n)) return 'path';
    if (/БУ$|^Б\d|^НАУ$|^ЧУ$|^НУ$|^У|ПЕРЕГОН/.test(n)) return 'line';
    if (/Т$|ТУП|^ВТ/.test(n)) return 'dead';
    return 'plain';
};

SE.secType = (st, name) => {
    if (!name) return '';
    for (const t of Object.values(st.tiles)) if (t.sec === name && t.t === 'switch') return 'switch';
    const s = st.sections[name];
    if (s && s.type && s.type !== 'auto') return s.type;
    const g = SE.guessSecType(name);
    return g === 'switch' ? 'plain' : g;    // «…СП» без стрелки — просто участок
};

SE.secColor = (st, name) => {
    const s = st.sections[name];
    if (s && s.color) return s.color;
    let h = 0;
    for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) % 360;
    return `hsl(${(h * 137) % 360}, 75%, 52%)`;
};

/** Группа стрелки: спаренные (одинаковый «съезд») переводятся вместе. */
SE.switchGroup = (key, t) => t.group ? 'g:' + t.group : 'k:' + key;

SE.allSignals = st => {
    const res = [];
    for (const [k, t] of Object.entries(st.tiles))
        (t.sigs || []).forEach((s, i) => res.push({ key: k, idx: i, sig: s, tile: t }));
    return res;
};

/** Имя для нового светофора: Ч / Н, Ч1… / Н1…, маневровые М2, М4… (→) и М1, М3… (←), проходные 1, 2… */
SE.suggestSignalName = (st, kind, dir) => {
    const used = new Set(SE.allSignals(st).map(s => s.sig.name));
    const letter = dir === 'R' ? 'Ч' : 'Н';
    if (kind === 'entry' && !used.has(letter)) return letter;
    let prefix = '', n = 1, step = 1;
    if (kind === 'entry' || kind === 'exit') prefix = letter;
    else if (kind === 'shunt') { prefix = 'М'; n = dir === 'R' ? 2 : 1; step = 2; }
    else if (kind === 'block') { n = dir === 'R' ? 2 : 1; step = 2; }
    else prefix = 'С';
    while (used.has(prefix + n)) n += step;
    return prefix + n;
};
