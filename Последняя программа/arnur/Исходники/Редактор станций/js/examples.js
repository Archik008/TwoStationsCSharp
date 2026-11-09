'use strict';
// =====================================================================
//  ПРИМЕРЫ СХЕМ. Алгабас — по Algabas/AlgabasLayout.cs (пути 1–4, вытяжка, тупик М6Т,
//  однопутный перегон к путевому посту), собран из кубиков.
// =====================================================================
var SE = window.SE = window.SE || {};

SE.EXAMPLES = {
    algabas: { name: 'Станция Алгабас', build: buildAlgabas },
    razezd: { name: 'Разъезд (2 пути)', build: buildRazezd },
};

// варианты кубиков (см. SE.TYPES в core.js)
const H = 0, UP = 1, DOWN = 2;                      // straight: ─  ╱  ╲
const W_NE = 0, W_SE = 1, E_NW = 2, E_SW = 3;       // curve
const SW_UP = 0, SW_DOWN = 1, SW_UP_L = 2, SW_DOWN_L = 3;   // switch: остряк слева / справа, отклонение вверх / вниз

function builder(name) {
    const st = SE.newStation();
    st.name = name;
    const b = {
        st,
        put(x, y, t, v, sec, extra) { st.tiles[SE.key(x, y)] = Object.assign({ t, v, sec }, extra || {}); return b; },
        run(x1, x2, y, sec) { for (let x = x1; x <= x2; x++) b.put(x, y, 'straight', H, sec); return b; },
        sig(x, y, dir, kind, name) {
            const t = st.tiles[SE.key(x, y)];
            (t.sigs = t.sigs || []).push({ dir, kind, name });
            return b;
        },
        text(x, y, text, size) { st.texts[SE.key(x, y)] = { text, size: size || 14 }; return b; },
        type(sec, type) { st.sections[sec] = Object.assign(st.sections[sec] || {}, { type }); return b; },
    };
    return b;
}

function buildAlgabas() {
    const b = builder('Станция Алгабас');

    // ---- перегон к путевому посту (путь 1, ряд 8) ----
    b.put(0, 8, 'exit', 0, 'НАУ').run(1, 5, 8, 'НАУ').run(6, 10, 8, 'Б12');
    b.sig(1, 8, 'L', 'other', 'НА').sig(5, 8, 'R', 'block', '2').sig(6, 8, 'L', 'block', '1').sig(10, 8, 'R', 'entry', 'Ч');

    // ---- горловина: Ч-М2, стрелки 2 и 4 ----
    b.run(11, 12, 8, 'Ч-М2').sig(12, 8, 'R', 'shunt', 'М2');
    b.put(13, 8, 'switch', SW_DOWN, '2-4СП', { name: '2' });
    b.run(14, 14, 8, '2-4СП');
    b.put(15, 8, 'switch', SW_UP, '2-4СП', { name: '4', group: '4/6' });

    // ---- путь 1, стрелка 1, вытяжка ----
    b.run(16, 27, 8, '1П').sig(16, 8, 'L', 'exit', 'Н1').sig(27, 8, 'R', 'shunt', 'М8');
    b.put(28, 8, 'switch', SW_DOWN_L, '1СП', { name: '1' }).run(29, 29, 8, '1СП');
    b.run(30, 32, 8, 'ВТ').sig(30, 8, 'L', 'shunt', 'М1').put(33, 8, 'buffer', 0, 'ВТ');

    // ---- путь 3 (ряд 11) ----
    b.put(14, 9, 'straight', DOWN, '2-4СП').put(15, 10, 'straight', DOWN, '2-4СП').put(16, 11, 'curve', E_NW, '2-4СП');
    b.run(17, 24, 11, '3П').sig(17, 11, 'L', 'exit', 'Н3').sig(24, 11, 'R', 'shunt', 'М10');
    b.put(25, 11, 'curve', W_NE, '1СП').put(26, 10, 'straight', UP, '1СП').put(27, 9, 'straight', UP, '1СП');

    // ---- съезд 4/6, тупик М6Т, путь 2 (ряд 5) ----
    b.put(16, 7, 'straight', UP, '2-4СП').put(17, 6, 'straight', UP, '6СП');
    b.put(18, 5, 'switch', SW_DOWN_L, '6СП', { name: '6', group: '4/6' });
    b.put(13, 5, 'buffer', 1, 'М6Т').run(14, 17, 5, 'М6Т').sig(17, 5, 'R', 'shunt', 'М6');
    b.put(19, 5, 'switch', SW_UP, '8СП', { name: '8' }).run(20, 20, 5, '8СП');
    b.run(21, 31, 5, '2П').sig(21, 5, 'L', 'exit', 'Н2').put(32, 5, 'buffer', 0, '2П');

    // ---- путь 4 (ряд 2) ----
    b.put(20, 4, 'straight', UP, '8СП').put(21, 3, 'straight', UP, '8СП').put(22, 2, 'curve', E_SW, '8СП');
    b.run(23, 31, 2, '4П').sig(23, 2, 'L', 'exit', 'Н4').put(32, 2, 'buffer', 0, '4П');

    // ---- типы участков и надписи ----
    b.type('НАУ', 'line').type('Б12', 'line').type('Ч-М2', 'plain').type('ВТ', 'dead').type('М6Т', 'dead');
    b.text(22, 7, '1', 18).text(26, 4, '2', 18).text(21, 10, '3', 18).text(26, 1, '4', 18);
    b.text(0, 10, '← к путевому посту', 13).text(1, 6, 'Однопутный перегон', 13).text(13, 0, 'Станция Алгабас', 20);
    return b.st;
}

function buildRazezd() {
    const b = builder('Разъезд');
    b.put(0, 6, 'exit', 0, 'НП').run(1, 4, 6, 'НП').sig(4, 6, 'R', 'entry', 'Ч');
    b.put(5, 6, 'switch', SW_UP, '1СП', { name: '1' });
    b.run(6, 17, 6, 'IП').sig(6, 6, 'L', 'exit', 'Н1').sig(17, 6, 'R', 'exit', 'Ч1');
    b.put(6, 5, 'straight', UP, '1СП').put(7, 4, 'curve', E_SW, '1СП');
    b.run(8, 15, 4, '3П').sig(8, 4, 'L', 'exit', 'Н3').sig(15, 4, 'R', 'exit', 'Ч3');
    b.put(16, 4, 'curve', W_SE, '2СП').put(17, 5, 'straight', DOWN, '2СП');
    b.put(18, 6, 'switch', SW_UP_L, '2СП', { name: '2' });
    b.run(19, 22, 6, 'ЧП').sig(19, 6, 'L', 'entry', 'Н').put(23, 6, 'exit', 1, 'ЧП');
    b.type('НП', 'line').type('ЧП', 'line');
    b.text(10, 7, '← от станции А', 13).text(17, 8, 'к станции Б →', 13).text(10, 2, 'Разъезд', 20);
    return b.st;
}
