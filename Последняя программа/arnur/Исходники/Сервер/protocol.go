package main

import "encoding/json"

// =====================================================================
//  ПРОТОКОЛ (JSON поверх WebSocket, одно сообщение = один JSON-объект)
//
//  Программа станции → сервер:
//    {"type":"hello","station":"akzht","name":"АКЖТ + путевой пост"}
//        первое сообщение после подключения: кто я
//    {"type":"signals","signals":{"НА":"Yellow","1-ОП":"Dark"},
//     "lines":{"ОП":{"dir":"Right","ver":3}},"sections":{"НАУ":true,"Б12":false}}
//        ПОЛНЫЙ снимок состояния станции: показания светофоров, которыми она управляет,
//        направление общих однопутных перегонов (ver растёт при каждой смене —
//        по нему станции решают, чьё направление новее) и занятость общих участков
//        (как её видит сама станция: поезда, датчики, занятость вручную).
//        [1.3] Любые ДРУГИЕ поля снимка (например, "routes") сервер не разбирает, а пересылает
//        соседям в state как есть — новые данные не требуют новой версии сервера.
//        (присылать при изменении; можно и повторно — одинаковое сервер не пересылает)
//    {"type":"event","to":"algabas","data":{...}}
//        разовое сообщение другой станции (например, запрос смены направления).
//        Сервер data не разбирает, только пересылает. Если станции нет на связи — error.
//    {"type":"ping"}
//        проверка связи; сервер сразу отвечает {"type":"pong"}. Нужна клиентам, которые
//        сами не замечают пропажу сервера (ClientWebSocket в .NET 8 — без таймаута keep-alive)
//
//  Сервер → программа станции:
//    {"type":"welcome","station":"akzht","server":"PromServer 1.3"}
//    {"type":"state","station":"algabas","name":"Станция Алгабас","online":true,
//     "signals":{"Ч":"Red","2-ОП":"Green"},"lines":{"ОП":{"dir":"Right","ver":3}},
//     "updated":"2026-10-12T10:30:00+05:00"}
//        состояние ДРУГОЙ станции: при подключении (по каждой известной станции),
//        при каждом её изменении, при её отключении (online:false, signals/lines/sections пустые)
//    {"type":"event","from":"akzht","data":{...}}
//    {"type":"error","message":"..."}
//
//  Сервер ничего не знает о конкретных светофорах и перегонах: какие чьи и как
//  их показывать — решают программы станций. Значения показаний — имена
//  перечисления Aspect из C#-ядра (Red, Yellow, Green, Dark, ...), направления — Dir (Right, Left).
// =====================================================================

const (
	TypeHello   = "hello"
	TypeSignals = "signals"
	TypeEvent   = "event"
	TypeWelcome = "welcome"
	TypeState   = "state"
	TypeError   = "error"
	TypePing    = "ping"
	TypePong    = "pong"
)

// LineState — направление однопутного перегона у станции.
type LineState struct {
	Dir string `json:"dir"`
	Ver int64  `json:"ver"`
}

// InMessage — сообщение от программы станции.
type InMessage struct {
	Type    string               `json:"type"`
	Station string               `json:"station,omitempty"`
	Name    string               `json:"name,omitempty"`
	Signals map[string]string    `json:"signals,omitempty"`
	Lines   map[string]LineState `json:"lines,omitempty"`
	// занятость общих с соседом участков, как её видит сама станция (поезда, датчики, вручную)
	Sections map[string]bool `json:"sections,omitempty"`
	To       string          `json:"to,omitempty"`
	Data     json.RawMessage `json:"data,omitempty"`
}

// WelcomeMessage — ответ на hello.
type WelcomeMessage struct {
	Type    string `json:"type"`
	Station string `json:"station"`
	Server  string `json:"server"`
}

// StateMessage — состояние одной станции для остальных.
type StateMessage struct {
	Type     string               `json:"type"`
	Station  string               `json:"station"`
	Name     string               `json:"name,omitempty"`
	Online   bool                 `json:"online"`
	Signals  map[string]string    `json:"signals"`
	Lines    map[string]LineState `json:"lines"`
	Sections map[string]bool      `json:"sections"`
	Updated  string               `json:"updated,omitempty"`
	// поля снимка, которых сервер не знает (например, "routes"), — выкладываются рядом с остальными как есть
	Extra map[string]json.RawMessage `json:"-"`
}

// MarshalJSON — обычные поля плюс Extra на том же уровне.
func (m StateMessage) MarshalJSON() ([]byte, error) {
	type plain StateMessage
	data, err := json.Marshal(plain(m))
	if err != nil || len(m.Extra) == 0 {
		return data, err
	}
	var all map[string]json.RawMessage
	if err := json.Unmarshal(data, &all); err != nil {
		return nil, err
	}
	for k, v := range m.Extra {
		if _, taken := all[k]; !taken {
			all[k] = v
		}
	}
	return json.Marshal(all)
}

// EventMessage — разовое сообщение от другой станции.
type EventMessage struct {
	Type string          `json:"type"`
	From string          `json:"from"`
	Data json.RawMessage `json:"data"`
}

// ErrorMessage — отказ (неправильное сообщение и т.п.).
type ErrorMessage struct {
	Type    string `json:"type"`
	Message string `json:"message"`
}
