package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"sync"
	"time"
	"unicode/utf8"

	"github.com/gorilla/websocket"
)

// =====================================================================
//  ОДНО ПОДКЛЮЧЕНИЕ ПРОГРАММЫ СТАНЦИИ
//  Чтение — в горутине обработчика HTTP, запись — в своей горутине через очередь
//  (gorilla/websocket не разрешает писать из двух горутин сразу).
// =====================================================================

const (
	helloWait   = 10 * time.Second // за сколько надо представиться после подключения
	writeWait   = 5 * time.Second  // сколько ждать отправку одного сообщения
	pongWait    = 30 * time.Second // нет ответа на ping столько времени — связь потеряна
	pingPeriod  = 10 * time.Second // как часто сервер пингует станцию
	maxMsgBytes = 4 << 20          // [1.4] снимки экрана станций для преподавателя
)

var upgrader = websocket.Upgrader{
	ReadBufferSize:  4096,
	WriteBufferSize: 4096,
	// программы станций — не браузер, Origin не присылают; страницу статуса тоже не ограничиваем
	CheckOrigin: func(r *http.Request) bool { return true },
}

type Client struct {
	hub     *Hub
	conn    *websocket.Conn
	addr    string
	station string

	send      chan []byte
	done      chan struct{}
	closeOnce sync.Once
	kickMsg   string
}

// ServeWS — обработчик /ws.
func ServeWS(hub *Hub, w http.ResponseWriter, r *http.Request) {
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("не удалось открыть WebSocket с %s: %v", r.RemoteAddr, err)
		return
	}
	c := &Client{
		hub:  hub,
		conn: conn,
		addr: r.RemoteAddr,
		send: make(chan []byte, sendBufferSize),
		done: make(chan struct{}),
	}
	conn.SetReadLimit(maxMsgBytes)
	go c.writePump()

	hello, err := c.readHello()
	if err != nil {
		log.Printf("%s: отказ — %v", c.addr, err)
		c.SendJSON(ErrorMessage{Type: TypeError, Message: err.Error()})
		c.Kick(err.Error())
		return
	}
	c.station = hello.Station
	hub.Register(c, hello.Name)
	reason := c.readLoop()
	hub.Unregister(c, reason)
	c.Kick(reason)
}

func (c *Client) readHello() (*InMessage, error) {
	c.conn.SetReadDeadline(time.Now().Add(helloWait))
	var m InMessage
	if _, err := c.readJSON(&m); err != nil {
		return nil, fmt.Errorf("не получено приветствие hello: %v", err)
	}
	if m.Type != TypeHello {
		return nil, fmt.Errorf("первым сообщением должно быть hello, пришло %q", m.Type)
	}
	if !stationIDRe.MatchString(m.Station) {
		return nil, fmt.Errorf("неправильное имя станции %q (латиница a-z, цифры, - и _, до 32 символов)", m.Station)
	}
	if utf8.RuneCountInString(m.Name) > maxNameLen {
		return nil, errors.New("слишком длинное name")
	}
	if m.Name == "" {
		m.Name = m.Station
	}
	return &m, nil
}

// readLoop — читает сообщения до обрыва. Возвращает причину закрытия (для журнала).
func (c *Client) readLoop() string {
	c.conn.SetReadDeadline(time.Now().Add(pongWait))
	c.conn.SetPongHandler(func(string) error {
		c.conn.SetReadDeadline(time.Now().Add(pongWait))
		return nil
	})
	for {
		var m InMessage
		raw, err := c.readJSON(&m)
		if err != nil {
			var se *syntaxError
			if errors.As(err, &se) {
				c.SendJSON(ErrorMessage{Type: TypeError, Message: se.Error()})
				continue
			}
			return closeReason(err)
		}
		// любое сообщение от станции — тоже признак живой связи
		c.conn.SetReadDeadline(time.Now().Add(pongWait))

		switch m.Type {
		case TypeSignals:
			extra, err := extraFields(raw)
			if err == nil {
				err = validateState(m.Signals, m.Lines, m.Sections)
			}
			if err != nil {
				c.SendJSON(ErrorMessage{Type: TypeError, Message: err.Error()})
				continue
			}
			c.hub.UpdateState(c, m.Signals, m.Lines, m.Sections, extra)
		case TypeEvent:
			if err := validateEvent(c, &m); err != nil {
				c.SendJSON(ErrorMessage{Type: TypeError, Message: err.Error()})
				continue
			}
			if err := c.hub.Forward(c, m.To, m.Data); err != nil {
				c.SendJSON(ErrorMessage{Type: TypeError, Message: err.Error()})
			}
		case TypePing:
			c.Send(pongMessage)
		case TypeHello:
			c.SendJSON(ErrorMessage{Type: TypeError, Message: "hello уже был"})
		default:
			c.SendJSON(ErrorMessage{Type: TypeError, Message: fmt.Sprintf("неизвестный тип сообщения %q", m.Type)})
		}
	}
}

var pongMessage = []byte(`{"type":"pong"}`)

type syntaxError struct{ err error }

func (e *syntaxError) Error() string { return "неправильный JSON: " + e.err.Error() }

// readJSON — прочитать одно текстовое сообщение. Ошибка разбора JSON — *syntaxError (связь при этом жива).
func (c *Client) readJSON(v any) ([]byte, error) {
	_, data, err := c.conn.ReadMessage()
	if err != nil {
		return nil, err
	}
	if err := json.Unmarshal(data, v); err != nil {
		return nil, &syntaxError{err}
	}
	return data, nil
}

func validateState(sig map[string]string, lines map[string]LineState, sections map[string]bool) error {
	if len(sig) > maxSignals {
		return fmt.Errorf("слишком много светофоров в снимке: %d (не больше %d)", len(sig), maxSignals)
	}
	for k, v := range sig {
		if k == "" || utf8.RuneCountInString(k) > maxNameLen || utf8.RuneCountInString(v) > maxNameLen {
			return fmt.Errorf("неправильная запись светофора %q=%q", k, v)
		}
	}
	if len(lines) > maxLines {
		return fmt.Errorf("слишком много перегонов в снимке: %d (не больше %d)", len(lines), maxLines)
	}
	for k, l := range lines {
		if k == "" || utf8.RuneCountInString(k) > maxNameLen || (l.Dir != "Right" && l.Dir != "Left") || l.Ver < 0 {
			return fmt.Errorf("неправильная запись перегона %q: dir=%q ver=%d (dir — Right или Left)", k, l.Dir, l.Ver)
		}
	}
	if len(sections) > maxSignals {
		return fmt.Errorf("слишком много участков в снимке: %d (не больше %d)", len(sections), maxSignals)
	}
	for k := range sections {
		if k == "" || utf8.RuneCountInString(k) > maxNameLen {
			return fmt.Errorf("неправильное имя участка %q", k)
		}
	}
	return nil
}

func validateEvent(c *Client, m *InMessage) error {
	if !stationIDRe.MatchString(m.To) {
		return fmt.Errorf("event: неправильный адресат %q", m.To)
	}
	if m.To == c.station {
		return errors.New("event: нельзя отправить самому себе")
	}
	if len(m.Data) == 0 || len(m.Data) > maxEventBytes || m.Data[0] != '{' {
		return fmt.Errorf("event: data должен быть JSON-объектом не длиннее %d байт", maxEventBytes)
	}
	return nil
}

func closeReason(err error) string {
	var ce *websocket.CloseError
	if errors.As(err, &ce) {
		if ce.Text != "" {
			return "станция закрыла связь: " + ce.Text
		}
		return fmt.Sprintf("станция закрыла связь (код %d)", ce.Code)
	}
	var ne interface{ Timeout() bool }
	if errors.As(err, &ne) && ne.Timeout() {
		return fmt.Sprintf("нет ответа больше %d с", int(pongWait.Seconds()))
	}
	return "обрыв связи: " + err.Error()
}

// Send — поставить готовое сообщение в очередь. Никогда не ждёт: если станция не успевает
// принимать (очередь полна), подключение закрывается — программа переподключится и получит свежее состояние.
func (c *Client) Send(data []byte) {
	select {
	case <-c.done:
	case c.send <- data:
	default:
		c.Kick("станция не успевает принимать сообщения")
	}
}

func (c *Client) SendJSON(v any) {
	data, err := json.Marshal(v)
	if err != nil {
		log.Printf("ошибка JSON: %v", err)
		return
	}
	c.Send(data)
}

// Kick — закрыть подключение (можно вызывать сколько угодно раз и из любой горутины).
func (c *Client) Kick(reason string) {
	c.closeOnce.Do(func() {
		c.kickMsg = reason
		close(c.done)
	})
}

func (c *Client) writePump() {
	ticker := time.NewTicker(pingPeriod)
	defer func() {
		ticker.Stop()
		c.conn.Close()
	}()
	for {
		select {
		case data := <-c.send:
			c.conn.SetWriteDeadline(time.Now().Add(writeWait))
			if err := c.conn.WriteMessage(websocket.TextMessage, data); err != nil {
				c.Kick("ошибка отправки: " + err.Error())
				return
			}
		case <-ticker.C:
			c.conn.SetWriteDeadline(time.Now().Add(writeWait))
			if err := c.conn.WriteMessage(websocket.PingMessage, nil); err != nil {
				c.Kick("ошибка отправки ping: " + err.Error())
				return
			}
		case <-c.done:
			// дописать то, что уже в очереди (например, сообщение об ошибке), и закрыть
			for {
				select {
				case data := <-c.send:
					c.conn.SetWriteDeadline(time.Now().Add(writeWait))
					if c.conn.WriteMessage(websocket.TextMessage, data) != nil {
						return
					}
					continue
				default:
				}
				break
			}
			reason := c.kickMsg
			if len(reason) > 120 { // close-фрейм не длиннее 125 байт
				reason = reason[:120]
			}
			for !utf8.ValidString(reason) {
				reason = reason[:len(reason)-1]
			}
			c.conn.WriteControl(websocket.CloseMessage,
				websocket.FormatCloseMessage(websocket.CloseNormalClosure, reason),
				time.Now().Add(writeWait))
			return
		}
	}
}

// knownFields — поля снимка, которые сервер разбирает сам; остальные пересылаются как есть.
var knownFields = map[string]bool{"type": true, "station": true, "name": true, "signals": true, "lines": true,
	"sections": true, "to": true, "data": true, "online": true, "updated": true}

// extraFields — [ДОБАВЛЕНО 12.10.2026] поля снимка, которых сервер не знает (например, "routes"):
// он их не разбирает, а пересылает другим станциям как есть. Так новые данные не требуют новой версии сервера.
func extraFields(raw []byte) (map[string]json.RawMessage, error) {
	var all map[string]json.RawMessage
	if err := json.Unmarshal(raw, &all); err != nil {
		return nil, fmt.Errorf("неправильный JSON: %v", err)
	}
	extra := map[string]json.RawMessage{}
	for k, v := range all {
		if knownFields[k] {
			continue
		}
		if !stationIDRe.MatchString(k) {
			return nil, fmt.Errorf("неправильное имя поля %q (латиница a-z, цифры, - и _)", k)
		}
		extra[k] = v
	}
	if len(extra) > maxExtraFields {
		return nil, fmt.Errorf("слишком много дополнительных полей: %d (не больше %d)", len(extra), maxExtraFields)
	}
	return extra, nil
}
