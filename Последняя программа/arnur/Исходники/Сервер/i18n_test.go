package main

// [ДОБАВЛЕНО 23.11.2026] Проверка перевода сообщений сервера (i18n.go).

import (
	"regexp"
	"slices"
	"testing"
)

var verbRe = regexp.MustCompile(`%[-+# 0]*[0-9]*(\.[0-9]+)?[vsqdfx%]`)

// В переводе — те же подстановки (%s, %d, %q…) в том же порядке, что в русской строке: иначе Sprintf перепутает значения.
func TestDictVerbs(t *testing.T) {
	for ru, tr := range dict {
		want := verbRe.FindAllString(ru, -1)
		for i, s := range tr {
			if s == "" {
				t.Errorf("%q: пустой перевод (%d)", ru, i)
			}
			if got := verbRe.FindAllString(s, -1); !slices.Equal(got, want) {
				t.Errorf("%q → %q: подстановки %v, нужно %v", ru, s, got, want)
			}
		}
	}
}

func TestLang(t *testing.T) {
	defer setLang("ru")
	setLang("en")
	if got := Tf("[%s] отключена (%s)", "akzht", "x"); got != "[akzht] disconnected (x)" {
		t.Errorf("en: %q", got)
	}
	setLang("kk")
	if got := T("Остановка сервера..."); got != "Серверді тоқтату..." {
		t.Errorf("kk: %q", got)
	}
	setLang("xx")
	if lang != "ru" || T("Остановка сервера...") != "Остановка сервера..." {
		t.Errorf("неизвестный язык должен давать русский, а lang=%q", lang)
	}
	t.Setenv("PROMSERVER_LANG", "EN")
	initLang("")
	if lang != "en" {
		t.Errorf("PROMSERVER_LANG=EN: lang=%q", lang)
	}
	initLang("kk") // ключ -lang главнее переменной окружения
	if lang != "kk" {
		t.Errorf("-lang kk: lang=%q", lang)
	}
}

// Страница состояния собирается на всех трёх языках.
func TestStatusPageLang(t *testing.T) {
	defer setLang("ru")
	for _, l := range []string{"ru", "kk", "en"} {
		setLang(l)
		var b []byte
		w := &bufWriter{&b}
		if err := statusPage.Execute(w, NewHub().Snapshot()); err != nil {
			t.Fatalf("%s: %v", l, err)
		}
		want := map[string]string{"ru": "Ни одна станция", "kk": "Әлі бірде-бір станция", "en": "No station has connected"}[l]
		if !regexp.MustCompile(regexp.QuoteMeta(want)).Match(b) {
			t.Errorf("%s: на странице нет %q", l, want)
		}
	}
}

type bufWriter struct{ b *[]byte }

func (w *bufWriter) Write(p []byte) (int, error) { *w.b = append(*w.b, p...); return len(p), nil }
