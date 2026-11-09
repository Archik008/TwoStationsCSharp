"""[ДОБАВЛЕНО 22.10.2026, этап 8] Словари перевода интерфейса: сборка и проверка.

Источник перевода — Инструменты/i18n/strings.tsv: строка = «русский<TAB>казахский<TAB>английский»
(переносы строк внутри текста — \\n, табуляция — \\t). Пустой перевод — оставить русский.
Строки, которые переводить не нужно (подписи Ч1, НП, фамилии работников…), — в Инструменты/i18n/skip.txt (комментарии там — «##»).

    python Инструменты/i18n/check.py           — показать русские строки из кода, которых нет в strings.tsv и skip.txt
    python Инструменты/i18n/check.py --todo F  — то же, записать их в файл F (дописать переводы и вставить в strings.tsv)
    python Инструменты/i18n/check.py --build   — собрать Lang/Strings.kk.json и Lang/Strings.en.json в АКЖТ и Алгабасе
                                           (программа «Преподаватель» берёт их из АКЖТ по ссылке)
После --build пересобрать программы (dotnet build / build_exe.bat).
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SRC = [os.path.join(ROOT, p) for p in ('Исходники/АКЖТ/Station', 'Исходники/Алгабас/Algabas', 'Исходники/Преподаватель')]
OUT = [os.path.join(ROOT, 'Исходники/АКЖТ/Station/Lang'), os.path.join(ROOT, 'Исходники/Алгабас/Algabas/Lang')]
TSV = os.path.join(HERE, 'strings.tsv')
SKIP = os.path.join(HERE, 'skip.txt')
sys.path.insert(0, HERE)
import extract  # noqa: E402
if hasattr(sys.stdout, 'reconfigure'): sys.stdout.reconfigure(encoding='utf-8')


def unesc(s):
    out, i = [], 0
    while i < len(s):
        if s[i] == '\\' and i + 1 < len(s):
            out.append({'n': '\n', 't': '\t', '\\': '\\', 'r': '\r'}.get(s[i + 1], '\\' + s[i + 1])); i += 2
        else:
            out.append(s[i]); i += 1
    return ''.join(out)


def esc(s):
    return s.replace('\\', '\\\\').replace('\n', '\\n').replace('\t', '\\t').replace('\r', '\\r')


def load_tsv():
    rows = {}
    if os.path.exists(TSV):
        for line in open(TSV, encoding='utf-8'):
            line = line.rstrip('\n')
            if not line or line.startswith('#'): continue
            p = line.split('\t')
            while len(p) < 3: p.append('')
            rows[unesc(p[0])] = (unesc(p[1]), unesc(p[2]))
    return rows


def load_skip():
    if not os.path.exists(SKIP): return set()
    return {unesc(l.rstrip('\n')) for l in open(SKIP, encoding='utf-8') if l.strip() and not l.startswith('##')}


def is_label(s):
    """Подписи объектов (Ч1, НП, 2-3СП, А:5/7…) — без строчных русских букв и короткие: не переводятся."""
    t = s.strip()
    return len(t) <= 10 and not re.search('[а-яё]', t) and not re.search('[А-ЯЁ]{4,}', t.replace('АКЖТ', ''))


def todo():
    rows, skip = load_tsv(), load_skip()
    have = {k.strip() for k in rows} | {k.strip() for k in skip}
    # шаблоны словаря («{0} → путь {1}») покрывают и строки без дырок («Ч → путь 1»)
    pats = []
    for k in rows:
        if extract.HOLE.search(k):
            parts = extract.HOLE.split(k.strip())
            pats.append(re.compile('^' + ''.join(re.escape(x) if i % 2 == 0 else '(.+?)' for i, x in enumerate(parts)) + '$', re.S))
    res = []
    for s, f in extract.strings(SRC).items():
        t = s.strip()
        if t in have or is_label(s): continue
        # шаблон покрывает строку, только если в его дырки попали подписи и числа, а не русские слова
        if not extract.HOLE.search(t) and any((m := p.match(t)) and not any(re.search('[а-яё]{2,}', g) for g in m.groups()) for p in pats): continue
        res.append((s, f))
    return res


def build():
    rows = load_tsv()
    for lang, idx in (('kk', 0), ('en', 1)):
        d = {'_': 'Словарь перевода интерфейса. Не править руками: источник — Инструменты/i18n/strings.tsv, сборка — check.py --build'}
        for ru, tr in sorted(rows.items()):
            if tr[idx].strip(): d[ru] = tr[idx]
        for out in OUT:
            os.makedirs(out, exist_ok=True)
            with open(os.path.join(out, f'Strings.{lang}.json'), 'w', encoding='utf-8', newline='\n') as f:
                json.dump(d, f, ensure_ascii=False, indent=0)
        print(f'{lang}: {len(d) - 1} строк')


if __name__ == '__main__':
    if '--build' in sys.argv:
        build()
    else:
        t = todo()
        if '--todo' in sys.argv:
            with open(sys.argv[sys.argv.index('--todo') + 1], 'w', encoding='utf-8') as f:
                cur = None
                for s, fl in t:
                    if fl != cur: cur = fl; f.write(f'# {os.path.relpath(fl, ROOT)}\n')
                    f.write(esc(s) + '\t\t\n')
        for s, fl in t[:40]: print(f'{os.path.relpath(fl, ROOT)}: {esc(s)}')
        print(f'без перевода: {len(t)}')
