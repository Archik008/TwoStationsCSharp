package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"log"
	"maps"
	"regexp"
	"slices"
	"sync"
	"time"
)

// =====================================================================
//  ХАБ: кто подключён и последнее состояние каждой станции.
//  Станция присылает своё состояние → хаб запоминает и рассылает всем остальным.
//  Разовые сообщения (event) хаб передаёт одной станции, не запоминая.
// =====================================================================

var stationIDRe = regexp.MustCompile(`^[a-z0-9_-]{1,32}$`)

const (
	maxSignals     = 64           // светофоров в одном снимке
	maxLines       = 16           // перегонов в одном снимке
	maxNameLen     = 64           // длина имени светофора / показания / станции
	maxEventBytes  = 4<<20 - 1024 // [1.4] размер data в event: снимки экрана и результаты занятий
	maxExtraFields = 16           // дополнительных (незнакомых серверу) полей в снимке
	sendBufferSize = 256          // сообщений в очереди на отправку одной станции
)

// stationState — что известно о станции (живёт и после отключения, чтобы было видно на странице).
type stationState struct {
	ID       string
	Name     string
	Online   bool
	Addr     string
	Since    time.Time // когда подключилась / отключилась
	Signals  map[string]string
	Lines    map[string]LineState
	Sections map[string]bool
	Extra    map[string]json.RawMessage // поля снимка, которых сервер не знает (пересылаются как есть)
	Updated  time.Time                  // когда последний раз менялось состояние
	client   *Client
}

type Hub struct {
	mu       sync.Mutex
	stations map[string]*stationState
}

func NewHub() *Hub {
	return &Hub{stations: map[string]*stationState{}}
}

func (s *stationState) message() StateMessage {
	m := StateMessage{
		Type:     TypeState,
		Station:  s.ID,
		Name:     s.Name,
		Online:   s.Online,
		Signals:  maps.Clone(s.Signals),
		Lines:    maps.Clone(s.Lines),
		Sections: maps.Clone(s.Sections),
		Extra:    maps.Clone(s.Extra),
	}
	if m.Signals == nil {
		m.Signals = map[string]string{}
	}
	if m.Lines == nil {
		m.Lines = map[string]LineState{}
	}
	if m.Sections == nil {
		m.Sections = map[string]bool{}
	}
	if !s.Updated.IsZero() {
		m.Updated = s.Updated.Format(time.RFC3339)
	}
	return m
}

// clearLocked — состояние станции больше не действительно (подключилась заново или отключилась).
func (s *stationState) clearLocked() {
	s.Signals = map[string]string{}
	s.Lines = map[string]LineState{}
	s.Sections = map[string]bool{}
	s.Extra = nil
	s.Updated = time.Time{}
}

// Register — станция представилась (hello). Старое подключение той же станции закрывается:
// после обрыва сети программа переподключается раньше, чем сервер заметит, что старое умерло.
func (h *Hub) Register(c *Client, name string) {
	h.mu.Lock()
	defer h.mu.Unlock()

	st := h.stations[c.station]
	if st == nil {
		st = &stationState{ID: c.station}
		h.stations[c.station] = st
	}
	if old := st.client; old != nil && old != c {
		log.Printf("[%s] новое подключение с %s — старое (%s) закрыто", c.station, c.addr, old.addr)
		old.Kick("станция подключилась заново с " + c.addr)
	}
	st.client = c
	st.Name = name
	st.Online = true
	st.Addr = c.addr
	st.Since = time.Now()
	st.clearLocked() // до первого снимка ничего не знаем

	c.SendJSON(WelcomeMessage{Type: TypeWelcome, Station: c.station, Server: ServerVersion})
	// новичку — состояние всех остальных станций
	for _, id := range slices.Sorted(maps.Keys(h.stations)) {
		if id != c.station {
			c.SendJSON(h.stations[id].message())
		}
	}
	h.broadcastLocked(st)
	log.Printf("[%s] подключена: %q с %s", c.station, name, c.addr)
}

// Unregister — подключение закрылось. Если станция уже переподключилась новым соединением — ничего не делаем.
func (h *Hub) Unregister(c *Client, reason string) {
	h.mu.Lock()
	defer h.mu.Unlock()

	st := h.stations[c.station]
	if st == nil || st.client != c {
		return
	}
	st.client = nil
	st.Online = false
	st.Since = time.Now()
	st.clearLocked() // состояние отключившейся станции больше не действительно
	h.broadcastLocked(st)
	log.Printf("[%s] отключена (%s)", c.station, reason)
}

// UpdateState — новый полный снимок состояния станции (показания и направления перегонов).
func (h *Hub) UpdateState(c *Client, signals map[string]string, lines map[string]LineState, sections map[string]bool,
	extra map[string]json.RawMessage) {
	h.mu.Lock()
	defer h.mu.Unlock()

	st := h.stations[c.station]
	if st == nil || st.client != c {
		return
	}
	if maps.Equal(st.Signals, signals) && maps.Equal(st.Lines, lines) && maps.Equal(st.Sections, sections) &&
		maps.EqualFunc(st.Extra, extra, func(a, b json.RawMessage) bool { return bytes.Equal(a, b) }) && !st.Updated.IsZero() {
		return // ничего не поменялось — не рассылаем
	}
	if *verbose {
		for id, l := range lines {
			if old, ok := st.Lines[id]; !ok || old != l {
				log.Printf("[%s] перегон %s: направление %s (версия %d)", c.station, id, l.Dir, l.Ver)
			}
		}
		if !maps.Equal(st.Signals, signals) {
			log.Printf("[%s] показания: %s", c.station, formatSignals(signals))
		}
		if !maps.Equal(st.Sections, sections) {
			log.Printf("[%s] занятость: %s", c.station, formatSections(sections))
		}
	}
	st.Signals = maps.Clone(signals)
	st.Lines = maps.Clone(lines)
	st.Sections = maps.Clone(sections)
	st.Extra = maps.Clone(extra)
	st.Updated = time.Now()
	h.broadcastLocked(st)
}

// Forward — передать разовое сообщение станции to. Ошибка — если её нет на связи.
func (h *Hub) Forward(c *Client, to string, data json.RawMessage) error {
	h.mu.Lock()
	defer h.mu.Unlock()

	st := h.stations[to]
	if st == nil || st.client == nil {
		return fmt.Errorf("станции %q нет на связи — сообщение не доставлено", to)
	}
	st.client.SendJSON(EventMessage{Type: TypeEvent, From: c.station, Data: data})
	if *verbose {
		log.Printf("[%s] → [%s]: %s", c.station, to, data)
	}
	return nil
}

// broadcastLocked — разослать состояние станции всем остальным подключённым. Вызывать под h.mu.
func (h *Hub) broadcastLocked(st *stationState) {
	msg := st.message()
	data, err := json.Marshal(msg)
	if err != nil {
		log.Printf("ошибка JSON: %v", err)
		return
	}
	for id, other := range h.stations {
		if id != st.ID && other.client != nil {
			other.client.Send(data)
		}
	}
}

// Snapshot — копия состояния для страницы статуса.
func (h *Hub) Snapshot() []StationStatus {
	h.mu.Lock()
	defer h.mu.Unlock()

	out := make([]StationStatus, 0, len(h.stations))
	for _, id := range slices.Sorted(maps.Keys(h.stations)) {
		st := h.stations[id]
		s := StationStatus{
			Station:  st.ID,
			Name:     st.Name,
			Online:   st.Online,
			Addr:     st.Addr,
			Since:    st.Since.Format("02.01.2006 15:04:05"),
			Signals:  maps.Clone(st.Signals),
			Lines:    maps.Clone(st.Lines),
			Sections: maps.Clone(st.Sections),
		}
		if !st.Updated.IsZero() {
			s.Updated = st.Updated.Format("15:04:05")
		}
		out = append(out, s)
	}
	return out
}

// StationStatus — строка таблицы на странице статуса (и в /status).
type StationStatus struct {
	Station  string               `json:"station"`
	Name     string               `json:"name"`
	Online   bool                 `json:"online"`
	Addr     string               `json:"addr"`
	Since    string               `json:"since"`
	Signals  map[string]string    `json:"signals"`
	Lines    map[string]LineState `json:"lines"`
	Sections map[string]bool      `json:"sections"`
	Updated  string               `json:"updated,omitempty"`
}

func formatSignals(sig map[string]string) string {
	if len(sig) == 0 {
		return "—"
	}
	s := ""
	for _, k := range slices.Sorted(maps.Keys(sig)) {
		if s != "" {
			s += ", "
		}
		s += k + "=" + sig[k]
	}
	return s
}

func formatLines(lines map[string]LineState) string {
	if len(lines) == 0 {
		return "—"
	}
	s := ""
	for _, k := range slices.Sorted(maps.Keys(lines)) {
		if s != "" {
			s += "   "
		}
		arrow := "→"
		if lines[k].Dir == "Left" {
			arrow = "←"
		}
		s += fmt.Sprintf("%s %s (в.%d)", k, arrow, lines[k].Ver)
	}
	return s
}

func formatSections(sec map[string]bool) string {
	if len(sec) == 0 {
		return "—"
	}
	s := ""
	for _, k := range slices.Sorted(maps.Keys(sec)) {
		if s != "" {
			s += "   "
		}
		if sec[k] {
			s += k + ": ЗАНЯТ"
		} else {
			s += k + ": свободен"
		}
	}
	return s
}
