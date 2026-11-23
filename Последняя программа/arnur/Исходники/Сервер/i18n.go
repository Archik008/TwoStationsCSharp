package main

// =====================================================================
//  [ДОБАВЛЕНО 23.11.2026] ЯЗЫК СООБЩЕНИЙ СЕРВЕРА: русский / казахский / английский
//
//  Как в программах станций: в коде строки русские — это ключи, здесь — перевод.
//  Язык: PromServer.exe -lang kk (ru, kk, en); без ключа — переменная окружения PROMSERVER_LANG;
//  иначе русский. «Преподаватель» запускает сервер на своём языке.
//  Переводится то, что читает человек: окно сервера (журнал), страница состояния, ошибки для станций.
// =====================================================================

import (
	"fmt"
	"log"
	"os"
	"strings"
)

var lang = "ru"

// setLang — выбрать язык (неизвестный код — русский).
func setLang(code string) {
	code = strings.ToLower(strings.TrimSpace(code))
	if code == "kk" || code == "en" {
		lang = code
	} else {
		lang = "ru"
	}
}

func initLang(flagValue string) {
	if flagValue == "" {
		flagValue = os.Getenv("PROMSERVER_LANG")
	}
	setLang(flagValue)
}

// T — перевод строки на текущий язык (нет перевода — как есть).
func T(s string) string {
	if lang == "ru" {
		return s
	}
	if tr, ok := dict[s]; ok {
		if lang == "kk" {
			return tr[0]
		}
		return tr[1]
	}
	return s
}

// Tf — перевод шаблона и подстановка, как fmt.Sprintf.
func Tf(format string, args ...any) string { return fmt.Sprintf(T(format), args...) }

// logf — запись в журнал окна сервера на текущем языке.
func logf(format string, args ...any) { log.Print(Tf(format, args...)) }

// errf — ошибка на текущем языке, как fmt.Errorf.
func errf(format string, args ...any) error { return fmt.Errorf(T(format), args...) }

// dict: русский → {казахский, английский}
var dict = map[string][2]string{
	"адрес и порт сервера":                         {"сервердің мекенжайы мен порты", "server address and port"},
	"писать в журнал каждое изменение показаний":   {"көрсеткіштердің әр өзгерісін журналға жазу", "log every change of indications"},
	"язык сообщений: ru, kk или en":                {"хабарламалар тілі: ru, kk немесе en", "message language: ru, kk or en"},
	"страница состояния: %v":                       {"күй беті: %v", "status page: %v"},
	"Не удалось открыть порт %s: %v\n(порт занят другой программой? запустите с -addr :9000)": {
		"%s портын ашу мүмкін болмады: %v\n(порт басқа бағдарламамен бос емес пе? -addr :9000 арқылы іске қосыңыз)",
		"Could not open port %s: %v\n(is the port used by another program? start with -addr :9000)"},
	"Сервер остановлен с ошибкой: %v": {"Сервер қатемен тоқтады: %v", "Server stopped with an error: %v"},
	"Остановка сервера...":            {"Серверді тоқтату...", "Stopping the server..."},
	" — сервер связи диспетчеров АКЖТ <-> Алгабас": {" — АКЖТ <-> Алгабас диспетчерлерінің байланыс сервері", " — dispatcher link server AKZHT <-> Algabas"},
	" Сетевых адресов не найдено — работает только на этом ПК:": {" Желілік мекенжайлар табылмады — тек осы ДК-да жұмыс істейді:", " No network addresses found — works on this PC only:"},
	" В программах станций укажите адрес сервера:":               {" Станция бағдарламаларында сервер мекенжайын көрсетіңіз:", " Enter the server address in the station programs:"},
	" Состояние связи в браузере:":                               {" Байланыс күйі браузерде:", " Link status in a browser:"},
	" Остановить сервер — Ctrl+C или закрыть окно.":              {" Серверді тоқтату — Ctrl+C немесе терезені жабу.", " Stop the server — Ctrl+C or close the window."},
	"Нажмите Enter, чтобы закрыть окно.":                         {"Терезені жабу үшін Enter басыңыз.", "Press Enter to close the window."},

	// страница состояния
	"Связь диспетчеров":         {"Диспетчерлер байланысы", "Dispatcher link"},
	"Связь диспетчеров АКЖТ ↔ Алгабас": {"АКЖТ ↔ Алгабас диспетчерлерінің байланысы", "Dispatcher link AKZHT ↔ Algabas"},
	"Станция":                   {"Станция", "Station"},
	"Связь":                     {"Байланыс", "Link"},
	"Адрес":                     {"Мекенжай", "Address"},
	"С":                         {"Бастап", "Since"},
	"Показания светофоров":      {"Бағдаршамдардың көрсеткіштері", "Signal indications"},
	"Направление перегонов":     {"Аралықтардың бағыты", "Line directions"},
	"Занятость общих участков":  {"Ортақ учаскелердің бос еместігі", "Shared section occupancy"},
	"Изменены":                  {"Өзгертілді", "Updated"},
	"есть":                      {"бар", "yes"},
	"нет":                       {"жоқ", "no"},
	"Ни одна станция ещё не подключалась.": {"Әлі бірде-бір станция қосылған жоқ.", "No station has connected yet."},
	"Страница обновляется каждые 2 с.":     {"Бет әр 2 с сайын жаңарады.", "The page refreshes every 2 s."},
	"%s %s (в.%d)":                         {"%s %s (н.%d)", "%s %s (v.%d)"},
	": ЗАНЯТ":                              {": БОС ЕМЕС", ": OCCUPIED"},
	": свободен":                           {": бос", ": free"},

	// журнал подключений
	"[%s] новое подключение с %s — старое (%s) закрыто": {"[%s] %s мекенжайынан жаңа қосылым — ескісі (%s) жабылды", "[%s] new connection from %s — the old one (%s) is closed"},
	"станция подключилась заново с %s":                   {"станция %s мекенжайынан қайта қосылды", "the station reconnected from %s"},
	"[%s] подключена: %q с %s":                           {"[%s] қосылды: %q, %s мекенжайынан", "[%s] connected: %q from %s"},
	"[%s] отключена (%s)":                                {"[%s] ажыратылды (%s)", "[%s] disconnected (%s)"},
	"[%s] перегон %s: направление %s (версия %d)":        {"[%s] %s аралығы: бағыты %s (нұсқа %d)", "[%s] line %s: direction %s (version %d)"},
	"[%s] показания: %s":                                 {"[%s] көрсеткіштер: %s", "[%s] indications: %s"},
	"[%s] занятость: %s":                                 {"[%s] бос еместік: %s", "[%s] occupancy: %s"},
	"станции %q нет на связи — сообщение не доставлено":  {"%q станциясы байланыста жоқ — хабарлама жеткізілмеді", "station %q is offline — the message was not delivered"},
	"ошибка JSON: %v":                                    {"JSON қатесі: %v", "JSON error: %v"},
	"не удалось открыть WebSocket с %s: %v":              {"%s мекенжайымен WebSocket ашу мүмкін болмады: %v", "could not open a WebSocket with %s: %v"},
	"%s: отказ — %v":                                     {"%s: бас тарту — %v", "%s: refused — %v"},

	// ошибки протокола (уходят и станции)
	"не получено приветствие hello: %v":                    {"hello сәлемдесуі алынбады: %v", "no hello greeting received: %v"},
	"первым сообщением должно быть hello, пришло %q":       {"бірінші хабарлама hello болуы керек, %q келді", "the first message must be hello, got %q"},
	"неправильное имя станции %q (латиница a-z, цифры, - и _, до 32 символов)": {
		"станцияның қате атауы %q (a-z латын әріптері, сандар, - және _, 32 таңбаға дейін)",
		"invalid station name %q (Latin a-z, digits, - and _, up to 32 characters)"},
	"слишком длинное name":            {"name тым ұзын", "name is too long"},
	"hello уже был":                   {"hello бұрын болған", "hello was already sent"},
	"неизвестный тип сообщения %q":    {"хабарламаның белгісіз түрі %q", "unknown message type %q"},
	"неправильный JSON: %s":           {"қате JSON: %s", "invalid JSON: %s"},
	"неправильный JSON: %v":           {"қате JSON: %v", "invalid JSON: %v"},
	"неправильное имя поля %q (латиница a-z, цифры, - и _)":    {"өрістің қате атауы %q (a-z латын әріптері, сандар, - және _)", "invalid field name %q (Latin a-z, digits, - and _)"},
	"слишком много дополнительных полей: %d (не больше %d)":    {"қосымша өрістер тым көп: %d (%d артық емес)", "too many extra fields: %d (at most %d)"},
	"слишком много светофоров в снимке: %d (не больше %d)": {"суретте бағдаршамдар тым көп: %d (%d артық емес)", "too many signals in the snapshot: %d (at most %d)"},
	"неправильная запись светофора %q=%q":                  {"бағдаршамның қате жазбасы %q=%q", "invalid signal entry %q=%q"},
	"слишком много перегонов в снимке: %d (не больше %d)":  {"суретте аралықтар тым көп: %d (%d артық емес)", "too many lines in the snapshot: %d (at most %d)"},
	"неправильная запись перегона %q: dir=%q ver=%d (dir — Right или Left)": {
		"аралықтың қате жазбасы %q: dir=%q ver=%d (dir — Right немесе Left)",
		"invalid line entry %q: dir=%q ver=%d (dir is Right or Left)"},
	"слишком много участков в снимке: %d (не больше %d)": {"суретте учаскелер тым көп: %d (%d артық емес)", "too many sections in the snapshot: %d (at most %d)"},
	"неправильное имя участка %q":                         {"учаскенің қате атауы %q", "invalid section name %q"},
	"event: неправильный адресат %q":                      {"event: қате алушы %q", "event: invalid recipient %q"},
	"event: нельзя отправить самому себе":                 {"event: өзіңізге жіберуге болмайды", "event: cannot send to yourself"},
	"event: data должен быть JSON-объектом не длиннее %d байт": {"event: data %d байттан аспайтын JSON-объект болуы керек", "event: data must be a JSON object of at most %d bytes"},
	"станция закрыла связь: %s":                           {"станция байланысты жапты: %s", "the station closed the link: %s"},
	"станция закрыла связь (код %d)":                      {"станция байланысты жапты (код %d)", "the station closed the link (code %d)"},
	"нет ответа больше %d с":                              {"%d с артық жауап жоқ", "no reply for more than %d s"},
	"обрыв связи: %s":                                     {"байланыс үзілді: %s", "link lost: %s"},
	"станция не успевает принимать сообщения":             {"станция хабарламаларды қабылдап үлгермейді", "the station cannot keep up with messages"},
	"ошибка отправки: %s":                                 {"жіберу қатесі: %s", "send error: %s"},
	"ошибка отправки ping: %s":                            {"ping жіберу қатесі: %s", "ping send error: %s"},
}
