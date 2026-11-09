'use strict';
// =====================================================================
//  МАРШРУТЫ (автопоиск по кубикам) и ПРОВЕРКА СХЕМЫ
//
//  От каждого входного, выходного и маневрового светофора идём по путям в его сторону,
//  перебирая положения стрелок (спаренные — только вместе). Маршрут кончается:
//    поездной   — на пути приёма-отправления (тип участка «путь») или на перегоне (отправление);
//    маневровый — на первом нестрелочном участке (путь, участок между светофорами, тупик);
//                 на перегон маневровые не выходят.
//  Выходной светофор даёт и поездные, и маневровые маршруты (белый огонь).
//  Это то, что в программах АКЖТ и Алгабаса сейчас пишется руками в BuildRoutes().
// =====================================================================
var SE = window.SE = window.SE || {};

SE.DIR_ARROW = { E: '→', W: '←', NE: '↗', NW: '↖', SE: '↘', SW: '↙' };

SE.findRoutes = st => {
    const bySec = SE.tilesBySection(st);
    const types = {};
    const stype = n => n in types ? types[n] : (types[n] = SE.secType(st, n));

    // спаренные стрелки: группа → клетки
    const groupKeys = new Map();
    for (const [k, t] of Object.entries(st.tiles)) {
        if (t.t !== 'switch') continue;
        const g = SE.switchGroup(k, t);
        if (!groupKeys.has(g)) groupKeys.set(g, []);
        groupKeys.get(g).push(k);
    }

    const routes = [];

    for (const s of SE.allSignals(st)) {
        const modes = [];
        if (s.sig.kind === 'entry' || s.sig.kind === 'exit') modes.push(false);
        if (s.sig.kind === 'shunt' || s.sig.kind === 'exit') modes.push(true);
        for (const shunt of modes) search(s, shunt);
    }

    function search(s, shunt) {
        const startSec = s.tile.sec || '';
        const want = s.sig.dir === 'R' ? 'E' : 'W';
        const outs = SE.endsOf(s.tile).filter(d => (s.sig.dir === 'R') === SE.isRight(d));
        if (!outs.length) return;
        const first = outs.includes(want) ? want : outs[0];

        const finish = (tiles, sws, dest, exit) => {
            const uniq = [...new Set(tiles)];
            const sections = [];
            for (const k of uniq) {
                const sec = st.tiles[k].sec;
                if (sec && sec !== startSec && !sections.includes(sec)) sections.push(sec);
            }
            if (exit) {
                const i = sections.indexOf(dest);
                if (i >= 0) sections.splice(i, 1);
            }
            const switches = [...sws.entries()].map(([g, plus]) => {
                const keys = groupKeys.get(g) || [];
                const t0 = st.tiles[keys[0]];
                return { group: g, plus, keys, name: (t0 && (t0.group || t0.name)) || '?' };
            });
            routes.push({
                start: { key: s.key, idx: s.idx }, startName: s.sig.name || '?', dir: s.sig.dir,
                shunt, dest, exit, tiles: uniq, sections, switches,
                diverging: switches.some(x => !x.plus)
            });
        };

        const dfs = (key, outDir, path, sws, seen) => {
            const nb = SE.neighbor(st, key, outDir);
            if (!nb) return;                                   // разрыв пути — маршрута нет
            const vk = nb.key + '|' + nb.inSide;
            if (seen.has(vk)) return;
            const sec = nb.tile.sec || '';
            const type = stype(sec);

            if (sec && sec !== startSec) {
                if (type === 'line') { if (!shunt) finish(path, sws, sec, true); return; }
                if (!shunt && type === 'path') { finish([...path, ...bySec.get(sec)], sws, sec, false); return; }
                if (shunt && type !== 'switch') { finish([...path, ...bySec.get(sec)], sws, sec, false); return; }
            }
            if (nb.tile.t === 'exit') { if (!shunt) finish([...path, nb.key], sws, sec || 'перегон', true); return; }
            if (nb.tile.t === 'buffer') { if (shunt) finish([...path, nb.key], sws, sec || 'тупик', false); return; }

            const path2 = [...path, nb.key];
            const seen2 = new Set(seen).add(vk);
            for (const o of SE.through(nb.tile, nb.inSide)) {
                let sws2 = sws;
                if (o.need !== null) {
                    const g = SE.switchGroup(nb.key, nb.tile);
                    const prev = sws.get(g);
                    if (prev !== undefined && prev !== o.need) continue;   // спаренная стрелка уже нужна в другом положении
                    sws2 = new Map(sws).set(g, o.need);
                }
                dfs(nb.key, o.out, path2, sws2, seen2);
            }
        };
        dfs(s.key, first, [], new Map(), new Set());
    }

    // имена: «Ч → 1П», одинаковые — с номером варианта
    const count = {};
    for (const r of routes) {
        const base = r.exit ? `${r.startName} → ${r.dest} (перегон)` : `${r.startName} → ${r.dest}`;
        const id = (r.shunt ? 'М:' : 'П:') + base;
        count[id] = (count[id] || 0) + 1;
        r.name = count[id] > 1 ? `${base} (вариант ${count[id]})` : base;
        r.id = (r.shunt ? 'М:' : 'П:') + r.name;
    }
    routes.sort((a, b) => (a.shunt - b.shunt) || a.startName.localeCompare(b.startName, 'ru', { numeric: true }) ||
        a.name.localeCompare(b.name, 'ru', { numeric: true }));
    return routes;
};

/** Светофор конца маршрута — как End в RailRoute: для приёма и манёвров — светофор встречного
 *  направления на участке назначения, для отправления — первый светофор перегона по ходу. */
SE.routeEndSignal = (st, r) => {
    const sigs = SE.allSignals(st).filter(s => s.tile.sec === r.dest);
    const dirWanted = r.exit ? r.dir : (r.dir === 'R' ? 'L' : 'R');
    const c = sigs.filter(s => s.sig.dir === dirWanted);
    if (!c.length) return null;
    const [sx] = SE.parse(r.start.key);
    c.sort((a, b) => Math.abs(SE.parse(a.key)[0] - sx) - Math.abs(SE.parse(b.key)[0] - sx));
    return c[0];
};

// ---------------------------------------------------------------------
//  ПРОВЕРКА СХЕМЫ: { level: 'err' | 'warn' | 'info', text, key }
// ---------------------------------------------------------------------
SE.validate = (st, routes) => {
    const out = [];
    const add = (level, text, key) => out.push({ level, text, key });
    const tiles = Object.entries(st.tiles);

    if (!tiles.length) { add('info', 'Схема пустая: выберите кубик слева и щёлкните по сетке.'); return out; }

    // разрывы
    for (const [k, t] of tiles) {
        if (t.t === 'exit') continue;
        for (const d of SE.endsOf(t)) {
            if (SE.neighbor(st, k, d)) continue;
            add('err', `Разрыв пути: клетка ${k}, выход ${SE.DIR_ARROW[d]} никуда не ведёт (поставьте кубик, тупик или «Выход»)`, k);
        }
    }

    // клетки без участка
    const noSec = tiles.filter(([, t]) => !t.sec);
    noSec.slice(0, 12).forEach(([k]) => add('warn', `Клетка ${k} не отнесена ни к одному участку (инструмент «Участок»)`, k));
    if (noSec.length > 12) add('warn', `…и ещё ${noSec.length - 12} клеток без участка`);

    // стрелки
    const groups = new Map();
    for (const [k, t] of tiles) {
        if (t.t !== 'switch') continue;
        if (!t.name) add('err', `Стрелка в клетке ${k} без номера`, k);
        if (t.group) {
            if (!groups.has(t.group)) groups.set(t.group, []);
            groups.get(t.group).push(k);
        }
    }
    for (const [g, ks] of groups) {
        if (ks.length === 1) add('warn', `Съезд «${g}» из одной стрелки — вторую стрелку съезда не нашли`, ks[0]);
        if (ks.length > 2) add('warn', `В съезде «${g}» ${ks.length} стрелки — обычно их две`, ks[0]);
    }

    // светофоры
    const names = new Map();
    for (const s of SE.allSignals(st)) {
        if (!s.sig.name) add('err', `Светофор в клетке ${s.key} без названия`, s.key);
        else {
            if (!names.has(s.sig.name)) names.set(s.sig.name, []);
            names.get(s.sig.name).push(s.key);
        }
        if (!SE.endsOf(s.tile).some(d => (s.sig.dir === 'R') === SE.isRight(d)))
            add('warn', `Светофор ${s.sig.name || '?'} (${s.key}) смотрит туда, где у клетки нет пути`, s.key);
    }
    for (const [n, ks] of names) if (ks.length > 1) add('err', `Два светофора с названием «${n}»: ${ks.join('; ')}`, ks[0]);

    // участок, разорванный на куски
    for (const [sec, keys] of SE.tilesBySection(st)) {
        const left = new Set(keys);
        let parts = 0;
        while (left.size) {
            parts++;
            const stack = [left.values().next().value];
            left.delete(stack[0]);
            while (stack.length) {
                const k = stack.pop();
                for (const d of SE.endsOf(st.tiles[k])) {
                    const nb = SE.neighbor(st, k, d);
                    if (nb && left.has(nb.key)) { left.delete(nb.key); stack.push(nb.key); }
                }
            }
        }
        if (parts > 1) add('warn', `Участок «${sec}» разорван на ${parts} части — между ними чужие клетки или разрыв`, keys[0]);
    }

    // маршруты
    const sigs = SE.allSignals(st);
    if (!sigs.length) add('info', 'Светофоров пока нет — маршрутов не будет (инструмент «Светофор»).');
    else {
        const train = routes.filter(r => !r.shunt).length, shunt = routes.length - train;
        add('info', `Найдено маршрутов: поездных ${train}, маневровых ${shunt}.`);
        for (const s of sigs) {
            if (s.sig.kind !== 'entry' && s.sig.kind !== 'exit' && s.sig.kind !== 'shunt') continue;
            if (!routes.some(r => r.start.key === s.key && r.start.idx === s.idx))
                add('warn', `От светофора ${s.sig.name || '?'} не найдено ни одного маршрута — проверьте разрывы и типы участков впереди (есть ли «путь»)`, s.key);
        }
    }
    return out;
};
