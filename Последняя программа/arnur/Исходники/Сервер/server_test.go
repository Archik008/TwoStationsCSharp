package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

func startServer(t *testing.T) string {
	t.Helper()
	hub := NewHub()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { ServeWS(hub, w, r) }))
	t.Cleanup(srv.Close)
	return "ws" + strings.TrimPrefix(srv.URL, "http")
}

type station struct {
	t    *testing.T
	conn *websocket.Conn
}

func dial(t *testing.T, url string) *station {
	t.Helper()
	conn, _, err := websocket.DefaultDialer.Dial(url, nil)
	if err != nil {
		t.Fatalf("подключение: %v", err)
	}
	t.Cleanup(func() { conn.Close() })
	return &station{t, conn}
}

func connect(t *testing.T, url, id, name string) *station {
	s := dial(t, url)
	s.send(map[string]any{"type": "hello", "station": id, "name": name})
	if m := s.recv(); m["type"] != "welcome" || m["station"] != id {
		t.Fatalf("%s: ожидался welcome, пришло %v", id, m)
	}
	return s
}

func (s *station) send(v any) {
	if err := s.conn.WriteJSON(v); err != nil {
		s.t.Fatalf("отправка: %v", err)
	}
}

func (s *station) recv() map[string]any {
	s.t.Helper()
	s.conn.SetReadDeadline(time.Now().Add(2 * time.Second))
	_, data, err := s.conn.ReadMessage()
	if err != nil {
		s.t.Fatalf("приём: %v", err)
	}
	var m map[string]any
	if err := json.Unmarshal(data, &m); err != nil {
		s.t.Fatalf("JSON: %v", err)
	}
	return m
}

// nothing — за короткое время ничего не пришло
func (s *station) nothing() {
	s.t.Helper()
	s.conn.SetReadDeadline(time.Now().Add(300 * time.Millisecond))
	if _, data, err := s.conn.ReadMessage(); err == nil {
		s.t.Fatalf("ничего не ожидалось, пришло %s", data)
	}
	// после таймаута чтения соединение gorilla непригодно — дальше в тесте его не используем
}

func expectState(t *testing.T, m map[string]any, station string, online bool, signals map[string]string) {
	t.Helper()
	if m["type"] != "state" || m["station"] != station || m["online"] != online {
		t.Fatalf("ожидалось state %s online=%v, пришло %v", station, online, m)
	}
	got := map[string]string{}
	for k, v := range m["signals"].(map[string]any) {
		got[k] = v.(string)
	}
	if len(got) != len(signals) {
		t.Fatalf("показания %s: ожидалось %v, пришло %v", station, signals, got)
	}
	for k, v := range signals {
		if got[k] != v {
			t.Fatalf("показания %s: ожидалось %v, пришло %v", station, signals, got)
		}
	}
}

func TestRelayBetweenStations(t *testing.T) {
	url := startServer(t)

	ak := connect(t, url, "akzht", "АКЖТ + путевой пост")
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red", "1-ОП": "Dark"}})
	time.Sleep(100 * time.Millisecond) // подтверждения у signals нет — даём серверу его обработать

	alg := connect(t, url, "algabas", "Станция Алгабас")
	// новичок сразу получает показания АКЖТ
	expectState(t, alg.recv(), "akzht", true, map[string]string{"НА": "Red", "1-ОП": "Dark"})
	// АКЖТ узнаёт, что Алгабас подключился (показаний ещё нет)
	expectState(t, ak.recv(), "algabas", true, map[string]string{})

	alg.send(map[string]any{"type": "signals", "signals": map[string]string{"Ч": "Yellow", "2-ОП": "Green"}})
	expectState(t, ak.recv(), "algabas", true, map[string]string{"Ч": "Yellow", "2-ОП": "Green"})

	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "TwoYellow", "1-ОП": "Dark"}})
	expectState(t, alg.recv(), "akzht", true, map[string]string{"НА": "TwoYellow", "1-ОП": "Dark"})

	// одинаковый снимок повторно не рассылается; следующий изменённый — приходит
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "TwoYellow", "1-ОП": "Dark"}})
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red", "1-ОП": "Dark"}})
	expectState(t, alg.recv(), "akzht", true, map[string]string{"НА": "Red", "1-ОП": "Dark"})

	// отключение Алгабаса: АКЖТ получает online=false и пустые показания
	alg.conn.WriteMessage(websocket.CloseMessage, websocket.FormatCloseMessage(websocket.CloseNormalClosure, "выход"))
	alg.conn.Close()
	expectState(t, ak.recv(), "algabas", false, map[string]string{})
	time.Sleep(100 * time.Millisecond)

	// Алгабас вернулся: снова получает показания АКЖТ, АКЖТ — что Алгабас на связи
	alg2 := connect(t, url, "algabas", "Станция Алгабас")
	expectState(t, alg2.recv(), "akzht", true, map[string]string{"НА": "Red", "1-ОП": "Dark"})
	expectState(t, ak.recv(), "algabas", true, map[string]string{})
}

func TestReconnectReplacesOldConnection(t *testing.T) {
	url := startServer(t)
	ak := connect(t, url, "akzht", "АКЖТ")
	old := connect(t, url, "algabas", "Алгабас")
	old.recv() // состояние АКЖТ
	ak.recv()  // Алгабас на связи

	// та же станция подключилась ещё раз (старое соединение «зависло» после обрыва сети)
	connect(t, url, "algabas", "Алгабас").recv()
	// старое закрыто сервером
	old.conn.SetReadDeadline(time.Now().Add(2 * time.Second))
	if _, _, err := old.conn.ReadMessage(); !websocket.IsCloseError(err, websocket.CloseNormalClosure) {
		t.Fatalf("старое подключение должно быть закрыто, ошибка: %v", err)
	}
	// АКЖТ видит Алгабас на связи (новое подключение) и НЕ получает «нет связи» от закрытия старого
	expectState(t, ak.recv(), "algabas", true, map[string]string{})
	ak.nothing()
}

func TestBadHello(t *testing.T) {
	url := startServer(t)
	for _, hello := range []map[string]any{
		{"type": "signals", "signals": map[string]string{"Ч": "Red"}},
		{"type": "hello", "station": "Алгабас"}, // не латиница
		{"type": "hello", "station": ""},
	} {
		s := dial(t, url)
		s.send(hello)
		if m := s.recv(); m["type"] != "error" {
			t.Fatalf("на %v ожидалась ошибка, пришло %v", hello, m)
		}
	}
}

func TestBadMessagesKeepConnection(t *testing.T) {
	url := startServer(t)
	ak := connect(t, url, "akzht", "АКЖТ")
	alg := connect(t, url, "algabas", "Алгабас")
	alg.recv()
	ak.recv()

	ak.conn.WriteMessage(websocket.TextMessage, []byte("{не json"))
	if m := ak.recv(); m["type"] != "error" {
		t.Fatalf("ожидалась ошибка, пришло %v", m)
	}
	ak.send(map[string]any{"type": "что-то"})
	if m := ak.recv(); m["type"] != "error" {
		t.Fatalf("ожидалась ошибка, пришло %v", m)
	}
	// после ошибок связь жива; ping → pong
	ak.send(map[string]any{"type": "ping"})
	if m := ak.recv(); m["type"] != "pong" {
		t.Fatalf("ожидался pong, пришло %v", m)
	}
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Yellow"}})
	expectState(t, alg.recv(), "akzht", true, map[string]string{"НА": "Yellow"})
}

func TestLinesAndEvents(t *testing.T) {
	url := startServer(t)
	ak := connect(t, url, "akzht", "АКЖТ")
	alg := connect(t, url, "algabas", "Алгабас")
	alg.recv()
	ak.recv()

	// направление перегона входит в состояние станции
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red"},
		"lines": map[string]any{"ОП": map[string]any{"dir": "Left", "ver": 3}}})
	m := alg.recv()
	expectState(t, m, "akzht", true, map[string]string{"НА": "Red"})
	l := m["lines"].(map[string]any)["ОП"].(map[string]any)
	if l["dir"] != "Left" || l["ver"] != float64(3) {
		t.Fatalf("направление: ожидалось Left/3, пришло %v", l)
	}
	// поменялось только направление — тоже рассылается
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red"},
		"lines": map[string]any{"ОП": map[string]any{"dir": "Right", "ver": 4}}})
	m = alg.recv()
	if l := m["lines"].(map[string]any)["ОП"].(map[string]any); l["dir"] != "Right" || l["ver"] != float64(4) {
		t.Fatalf("направление: ожидалось Right/4, пришло %v", l)
	}
	// неправильное направление — ошибка
	ak.send(map[string]any{"type": "signals", "lines": map[string]any{"ОП": map[string]any{"dir": "вверх", "ver": 1}}})
	if m := ak.recv(); m["type"] != "error" {
		t.Fatalf("ожидалась ошибка, пришло %v", m)
	}

	// разовое сообщение доходит только адресату, с пометкой from
	ak.send(map[string]any{"type": "event", "to": "algabas", "data": map[string]any{"kind": "dir_request", "id": 7}})
	m = alg.recv()
	if m["type"] != "event" || m["from"] != "akzht" || m["data"].(map[string]any)["kind"] != "dir_request" {
		t.Fatalf("ожидался event от akzht, пришло %v", m)
	}
	alg.send(map[string]any{"type": "event", "to": "akzht", "data": map[string]any{"kind": "dir_reply", "id": 7, "ok": true}})
	if m := ak.recv(); m["type"] != "event" || m["from"] != "algabas" || m["data"].(map[string]any)["ok"] != true {
		t.Fatalf("ожидался ответ от algabas, пришло %v", m)
	}
	// адресата нет на связи / сам себе / data не объект — ошибка
	for _, ev := range []map[string]any{
		{"type": "event", "to": "nobody", "data": map[string]any{"x": 1}},
		{"type": "event", "to": "akzht", "data": map[string]any{"x": 1}},
		{"type": "event", "to": "algabas", "data": 5},
	} {
		ak.send(ev)
		if m := ak.recv(); m["type"] != "error" {
			t.Fatalf("на %v ожидалась ошибка, пришло %v", ev, m)
		}
	}
}

func TestSections(t *testing.T) {
	url := startServer(t)
	ak := connect(t, url, "akzht", "АКЖТ")
	alg := connect(t, url, "algabas", "Алгабас")
	alg.recv()
	ak.recv()

	ak.send(map[string]any{"type": "signals", "signals": map[string]string{}, "sections": map[string]bool{"НАУ": true, "Б12": false}})
	m := alg.recv()
	sec := m["sections"].(map[string]any)
	if sec["НАУ"] != true || sec["Б12"] != false {
		t.Fatalf("занятость: ожидалось НАУ занят, Б12 свободен, пришло %v", sec)
	}
	// поменялась только занятость — тоже рассылается
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{}, "sections": map[string]bool{"НАУ": false, "Б12": true}})
	if sec := alg.recv()["sections"].(map[string]any); sec["НАУ"] != false || sec["Б12"] != true {
		t.Fatalf("занятость: ожидалось НАУ свободен, Б12 занят, пришло %v", sec)
	}
	// отключение — занятость соседа сброшена
	ak.conn.Close()
	if m := alg.recv(); m["online"] != false || len(m["sections"].(map[string]any)) != 0 {
		t.Fatalf("после отключения занятость должна быть пустой: %v", m)
	}
}

// Поля снимка, которых сервер не знает, пересылаются как есть.
func TestExtraFieldsRelayed(t *testing.T) {
	url := startServer(t)
	ak := connect(t, url, "akzht", "АКЖТ")
	alg := connect(t, url, "algabas", "Алгабас")
	alg.recv()
	ak.recv()

	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red"},
		"routes": map[string]string{"НАУ": "train", "Б12": "train"}})
	m := alg.recv()
	r, ok := m["routes"].(map[string]any)
	if !ok || r["НАУ"] != "train" || r["Б12"] != "train" {
		t.Fatalf("routes не переслан: %v", m)
	}
	expectState(t, m, "akzht", true, map[string]string{"НА": "Red"})
	// изменилось только незнакомое поле — тоже рассылается
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red"}, "routes": map[string]string{}})
	if r := alg.recv()["routes"].(map[string]any); len(r) != 0 {
		t.Fatalf("routes должен стать пустым: %v", r)
	}
	// то же самое повторно — не рассылается; плохое имя поля — ошибка
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red"}, "routes": map[string]string{}})
	ak.send(map[string]any{"type": "signals", "Плохое": 1})
	if m := ak.recv(); m["type"] != "error" {
		t.Fatalf("ожидалась ошибка, пришло %v", m)
	}
	// новичок получает и незнакомые поля
	ak.send(map[string]any{"type": "signals", "signals": map[string]string{"НА": "Red"}, "routes": map[string]string{"НАУ": "train"}})
	alg.recv()
	alg2 := connect(t, url, "algabas2", "Ещё станция")
	if r := alg2.recv()["routes"].(map[string]any); r["НАУ"] != "train" {
		t.Fatalf("новичку routes не пришёл: %v", r)
	}
}
