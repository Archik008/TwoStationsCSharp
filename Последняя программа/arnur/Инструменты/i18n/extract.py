"""[ДОБАВЛЕНО 22.10.2026, этап 8] Русские строки из исходников C# — ключи словарей перевода (Lang/Strings.*.json).

Берёт обычные "…", @"…", $"…" (дырки интерполяции → {0}, {1}…, строки внутри дырок — тоже) и склеивает
подряд идущие строки через + в одну ("… {a} " + $"на {b} мин" → "… {0} на {1} мин"): так в журнал попадает
целое сообщение, его и надо переводить. Комментарии пропускаются.

Используется из check.py; можно и отдельно:
    python tools/i18n/extract.py out.json <папки с .cs>
"""
import re, sys, json, glob, os

CYR = re.compile('[А-Яа-яЁё]')
HOLE = re.compile(r'\{(\d+)\}')


def read_string(src, i, interp, verbatim, nested):
    """Строка с позиции i (после открывающей кавычки) → (текст, число дырок, позиция после строки)."""
    buf = []; hole = 0; n = len(src)
    while i < n:
        c = src[i]
        if verbatim and c == '"':
            if i + 1 < n and src[i + 1] == '"': buf.append('"'); i += 2; continue
            return ''.join(buf), hole, i + 1
        if not verbatim and c == '\\':
            esc = src[i + 1]
            buf.append({'n': '\n', 't': '\t', '"': '"', '\\': '\\', '0': '\0', 'r': '\r', "'": "'"}.get(esc, '\\' + esc)); i += 2; continue
        if not verbatim and c == '"':
            return ''.join(buf), hole, i + 1
        if interp and c == '{':
            if i + 1 < n and src[i + 1] == '{': buf.append('{'); i += 2; continue
            depth = 1; j = i + 1
            while j < n and depth > 0:
                cj = src[j]
                if cj == '"' or (cj in '$@' and j + 1 < n and src[j + 1] in '"$@'):
                    k = j; pre = ''
                    while src[k] in '$@': pre += src[k]; k += 1
                    inner, _, j = read_string(src, k + 1, '$' in pre, '@' in pre, nested)
                    nested.append(inner); continue
                if cj == "'":
                    j += 1
                    if src[j] == '\\': j += 1
                    j += 2; continue
                if cj in '({[': depth += 1
                elif cj in ')}]': depth -= 1
                j += 1
            buf.append('{%d}' % hole); hole += 1
            i = j; continue
        if interp and c == '}' and i + 1 < n and src[i + 1] == '}':
            buf.append('}'); i += 2; continue
        buf.append(c); i += 1
    return ''.join(buf), hole, i


def tokenize(src):
    """Все строковые литералы файла: (текст, интерполированная?, дырок, начало, конец) + вложенные строки."""
    i, n = 0, len(src)
    out, nested = [], []
    while i < n:
        c = src[i]
        if src.startswith('//', i):
            j = src.find('\n', i); i = n if j < 0 else j; continue
        if src.startswith('/*', i):
            j = src.find('*/', i + 2); i = n if j < 0 else j + 2; continue
        if c == "'":
            j = i + 1
            if j < n and src[j] == '\\': j += 2
            else: j += 1
            while j < n and src[j] != "'": j += 1
            i = j + 1; continue
        m = re.match(r'(\$@|@\$|\$|@)?"', src[i:i + 3])
        if m and not (i > 0 and (src[i - 1].isalnum() or src[i - 1] == '_')):
            pre = m.group(1) or ''
            start = i
            s, holes, i = read_string(src, i + len(m.group(0)), '$' in pre, '@' in pre, nested)
            out.append((s, '$' in pre, holes, start, i))
            continue
        i += 1
    return out, nested


def merge(src, toks):
    """Склеить литералы, между которыми только «+» и пробелы."""
    res = []
    cur = None
    for t in toks:
        s, interp, holes, a, b = t
        if cur is not None and re.fullmatch(r'\s*\+\s*', src[cur[4]:a]) and not (not interp and HOLE.search(s) and cur[2] > 0):
            off = cur[2]
            part = HOLE.sub(lambda m: '{%d}' % (int(m.group(1)) + off), s) if interp else s
            cur = (cur[0] + part, cur[1] or interp, off + (holes if interp else 0), cur[3], b)
            continue
        if cur is not None: res.append(cur[0])
        cur = t
    if cur is not None: res.append(cur[0])
    return res


def files(roots):
    for r in roots:
        for f in sorted(glob.glob(os.path.join(r, '**', '*.cs'), recursive=True)):
            p = f.replace(os.sep, '/')
            if '/obj/' in p or '/bin/' in p or 'Station_AKZH/StationVS' in p: continue
            yield p


def strings(roots):
    """Русские строки по порядку появления: {текст: файл}."""
    seen = {}
    for f in files(roots):
        src = open(f, encoding='utf-8-sig').read()
        toks, nested = tokenize(src)
        merged = merge(src, toks)
        for s in [t[0] for t in toks] + merged + nested:
            if CYR.search(s) and s not in seen:
                seen[s] = f
    return seen


if __name__ == '__main__':
    res = strings(sys.argv[2:])
    json.dump(res, open(sys.argv[1], 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
    print(len(res))
